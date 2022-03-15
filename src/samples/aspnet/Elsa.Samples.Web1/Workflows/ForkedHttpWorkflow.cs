using System.Collections.Generic;
using System.Net;
using Elsa.Activities.Console;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Workflows;
using Elsa.Contracts;
using Elsa.Models;
using Elsa.Modules.Http;
using Elsa.Runtime.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Samples.Web1.Workflows;

public class ForkedHttpWorkflow : IWorkflow
{
    public void Build(IWorkflowDefinitionBuilder workflow)
    {
        // Setup workflow graph.
        workflow.WithRoot(new Sequence
        {
            Activities =
            {
                new HttpEndpoint
                {
                    Path = new Input<string>("/fork"),
                    SupportedMethods = new Input<ICollection<string>>(new[] { HttpMethods.Get })
                },
                new Fork
                {
                    JoinMode = JoinMode.WaitAll,
                    Branches =
                    {
                        new Sequence
                        {
                            Activities =
                            {
                                new HttpEndpoint { Path = new Input<string>("/fork/branch-1"), },
                                new WriteLine("Branch 1 continues!")
                            }
                        },
                        new Sequence
                        {
                            Activities =
                            {
                                new HttpEndpoint { Path = new Input<string>("/fork/branch-2"), },
                                new WriteLine("Branch 2 continues!")
                            }
                        }
                    },
                },
                new WriteHttpResponse
                {
                    StatusCode = new Input<HttpStatusCode>(HttpStatusCode.OK),
                    Content = new Input<string?>("Done!")
                }
            }
        });
    }
}