﻿using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Models;
using Elsa.Expressions.Services;
using Elsa.Extensions;
using Elsa.JavaScript.Contracts;
using Elsa.JavaScript.Notifications;
using Elsa.JavaScript.Options;
using Elsa.Mediator.Services;
using Elsa.Workflows.Core.Models;
using Elsa.Workflows.Management.Activities;
using Elsa.Workflows.Management.Extensions;
using Elsa.Workflows.Management.Services;
using Humanizer;
using Jint;
using Microsoft.Extensions.Options;

// ReSharper disable ConvertClosureToMethodGroup

namespace Elsa.JavaScript.Services;

/// <summary>
/// Provides a JavaScript evaluator using Jint.
/// </summary>
public class JintJavaScriptEvaluator : IJavaScriptEvaluator
{
    private readonly IEventPublisher _mediator;
    private readonly IActivityRegistry _activityRegistry;
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly JintOptions _jintOptions;

    /// <summary>
    /// Constructor.
    /// </summary>
    public JintJavaScriptEvaluator(IEventPublisher mediator, IActivityRegistry activityRegistry, IExpressionEvaluator expressionEvaluator, IOptions<JintOptions> scriptOptions)
    {
        _mediator = mediator;
        _activityRegistry = activityRegistry;
        _expressionEvaluator = expressionEvaluator;
        _jintOptions = scriptOptions.Value;
    }

    /// <inheritdoc />
    public async Task<object?> EvaluateAsync(string expression,
        Type returnType,
        ExpressionExecutionContext context,
        Action<Engine>? configureEngine = default,
        CancellationToken cancellationToken = default)
    {
        var engine = await GetConfiguredEngine(configureEngine, context, cancellationToken);
        var result = ExecuteExpressionAndGetResult(engine, expression);

        return result;
    }

    private async Task<Engine> GetConfiguredEngine(Action<Engine>? configureEngine, ExpressionExecutionContext context, CancellationToken cancellationToken)
    {
        var engine = new Engine(opts =>
        {
            if (_jintOptions.AllowClrAccess)
                opts.AllowClr();
        });

        configureEngine?.Invoke(engine);

        // Add common functions.
        engine.SetValue("getWorkflowInstanceId", (Func<string>)(() => context.GetActivityExecutionContext().WorkflowExecutionContext.Id));
        engine.SetValue("setCorrelationId", (Action<string?>)(value => context.GetActivityExecutionContext().WorkflowExecutionContext.CorrelationId = value));
        engine.SetValue("getCorrelationId", (Func<string?>)(() => context.GetActivityExecutionContext().WorkflowExecutionContext.CorrelationId));
        engine.SetValue("setCorrelationId", (Action<string?>)(value => context.GetActivityExecutionContext().WorkflowExecutionContext.CorrelationId = value));
        engine.SetValue("setVariable", (Action<string, object>)((name, value) => context.SetVariable(name, value)));
        engine.SetValue("getVariable", (Func<string, object?>)(name => context.GetVariable(name)));

        // Create variable & input setters and getters for each variable.
        CreateMemoryBlockAccessors(engine, context);
        
        // Create input getters.
        await CreateInputAccessorsAsync(engine, context);

        engine.SetValue("isNullOrWhiteSpace", (Func<string, bool>)(value => string.IsNullOrWhiteSpace(value)));
        engine.SetValue("isNullOrEmpty", (Func<string, bool>)(value => string.IsNullOrEmpty(value)));
        engine.SetValue("parseGuid", (Func<string, Guid>)(value => Guid.Parse(value)));
        engine.SetValue("toJson", (Func<object, string>)(value => Serialize(value)));

        // Add common .NET types.
        engine.RegisterType<DateTime>();
        engine.RegisterType<DateTimeOffset>();
        engine.RegisterType<TimeSpan>();
        engine.RegisterType<Guid>();

        // Allow listeners invoked by the mediator to configure the engine.
        await _mediator.PublishAsync(new EvaluatingJavaScript(engine, context), cancellationToken);

        return engine;
    }
    
    private async Task CreateInputAccessorsAsync(Engine engine, ExpressionExecutionContext context)
    {
        var workflowDefinitionActivity = GetFirstWorkflowDefinitionActivity(context);

        if (workflowDefinitionActivity == null)
            return;
        
        var descriptor = _activityRegistry.Find(workflowDefinitionActivity.Type, workflowDefinitionActivity.Version)!;
        var inputDefinitions = descriptor.Inputs;

        foreach (var inputDefinition in inputDefinitions)
        {
            var inputPascalName = inputDefinition.Name.Pascalize();
            var input = workflowDefinitionActivity.SyntheticProperties.TryGetValue(inputDefinition.Name, out var inputValue) ? (Input?)inputValue : default;
            var evaluatedExpression = input != null ? await _expressionEvaluator.EvaluateAsync(input, context) : input;

            engine.SetValue($"get{inputPascalName}", (Func<object?>)(() => evaluatedExpression));
        }
    }

    private static WorkflowDefinitionActivity? GetFirstWorkflowDefinitionActivity(ExpressionExecutionContext context) => 
        context.GetActivityExecutionContext().GetFirstWorkflowDefinitionActivity();

    private static void CreateMemoryBlockAccessors(Engine engine, ExpressionExecutionContext context)
    {
        var variablesDictionary = context.ReadAndFlattenMemoryBlocks();

        foreach (var variable in variablesDictionary)
        {
            var pascalName = variable.Key.Pascalize();
            engine.SetValue($"get{pascalName}", (Func<object?>)(() => context.GetVariable(variable.Key)));
            engine.SetValue($"set{pascalName}", (Action<object?>)(value => context.SetVariable(variable.Key, value)));
        }
    }
    
    private static object ExecuteExpressionAndGetResult(Engine engine, string expression)
    {
        var result = engine.Evaluate(expression);
        return result.ToObject();
    }

    private static string Serialize(object value)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        return JsonSerializer.Serialize(value, options);
    }
}