using Elsa.Modules.Quartz.Jobs;
using Elsa.Modules.Scheduling.Jobs;
using Quartz;
using IJob = Elsa.Jobs.Services.IJob;

namespace Elsa.Modules.Quartz.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollectionQuartzConfigurator AddElsaJobs(this IServiceCollectionQuartzConfigurator quartz)
    {
        quartz.AddJob<RunWorkflowJob>();
        quartz.AddJob<ResumeWorkflowJob>();

        return quartz;
    }

    private static IServiceCollectionQuartzConfigurator AddJob<TJob>(this IServiceCollectionQuartzConfigurator quartz) where TJob : IJob =>
        quartz.AddJob<QuartzJob<TJob>>(job => job.StoreDurably().WithIdentity(typeof(TJob).Name));
}