using DoSelect.Infrastructure.Imports;
using DoSelect.Infrastructure.Outbox;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests.Outbox;

/// <summary>
/// 組長 PR #89 round 2 item 1：Hangfire 1.8 的 recurring job 沒指定 QueueName 就排到 default queue，
/// 而這個專案的 Server 只監聽 critical／notifications／maintenance／ai——排進 default 的工作永遠
/// 不會被執行。這支測試盯的就是「排程設定」這一層：不跑 Hangfire，只驗登記進去的 queue 與 cron。
/// </summary>
public sealed class MaintenanceScheduleTests
{
    [Fact]
    public void ImportRetention_IsRegisteredOnTheMaintenanceQueue()
    {
        var registration = Registration("maintenance:import-retention");

        Assert.Equal("maintenance", registration.Job.Queue);
        Assert.Equal(typeof(ImportRetentionJob), registration.Job.Type);
        Assert.Equal(nameof(ImportRetentionJob.RunAsync), registration.Job.Method.Name);
    }

    /// <summary>
    /// 「列最多保存 24 小時、摘要 90 天」靠每日一次的排程達不到——03:01 進終態的批次要等到再隔天
    /// 才會被清，接近 48 小時。每小時跑一次，允許的清理延遲就是「界線之後最多再一小時」。
    /// </summary>
    [Fact]
    public void ImportRetention_RunsEveryHourInTaipeiTime()
    {
        var registration = Registration("maintenance:import-retention");

        Assert.Equal("0 * * * *", registration.Cron);
        Assert.Equal("Asia/Taipei", registration.Options.TimeZone?.Id);
    }

    private static CapturedRegistration Registration(string recurringJobId)
    {
        var manager = new CapturingRecurringJobManager();
        var services = new ServiceCollection();
        services.AddSingleton<IRecurringJobManager>(manager);
        using var provider = services.BuildServiceProvider();

        provider.ScheduleDoSelectMaintenanceJobs();

        return Assert.Single(manager.Registrations, candidate => candidate.Id == recurringJobId);
    }

    private sealed record CapturedRegistration(string Id, Job Job, string Cron, RecurringJobOptions Options);

    /// <summary>只記錄登記內容；RecurringJobManagerExtensions.AddOrUpdate&lt;T&gt; 最後都會落到這一個多載。</summary>
    private sealed class CapturingRecurringJobManager : IRecurringJobManager
    {
        public List<CapturedRegistration> Registrations { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) =>
            Registrations.Add(new CapturedRegistration(recurringJobId, job, cronExpression, options));

        public void Trigger(string recurringJobId)
        {
        }

        public void RemoveIfExists(string recurringJobId)
        {
        }
    }
}
