using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Imports;
using DoSelect.Infrastructure.Notifications;
using DoSelect.Infrastructure.Persistence;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Outbox;

public static class BackgroundJobServiceCollectionExtensions
{
    public static bool BackgroundJobsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("Features:BackgroundJobsEnabled");

    public static IServiceCollection AddDoSelectBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<BackgroundJobSettings>()
            .Bind(configuration.GetSection(BackgroundJobSettings.SectionName))
            .Validate(settings => settings.SchemaName == "HangFire", "SchemaName must be 'HangFire'.")
            .Validate(settings => settings.WorkerCount == 4, "WorkerCount must remain 4 until measured.")
            .Validate(settings => settings.DispatcherPollSeconds == 5, "Dispatcher polling must be 5 seconds.")
            .Validate(settings => settings.DispatcherBatchSize == 20, "Dispatcher batch size must be 20.")
            .Validate(settings => settings.ClaimLeaseSeconds >= 30, "Claim lease must be at least 30 seconds.")
            .ValidateOnStart();

        services.AddScoped<IEmailNotificationContentResolver, EmailNotificationContentResolver>();
        services.AddSingleton<IInAppNotificationContentRenderer, InAppNotificationContentRenderer>();
        services.AddScoped<IOutboxConsumer, EmailNotificationOutboxConsumer>();
        services.AddScoped<IOutboxConsumer, InAppNotificationOutboxConsumer>();
        services.AddScoped<IOutboxConsumer, SimulatedInvoiceOutboxConsumer>();

        if (!BackgroundJobsEnabled(configuration))
        {
            return services;
        }

        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
        services.AddScoped<OutboxDispatchJob>();
        services.AddScoped<IdempotencyRetentionJob>();
        services.AddScoped<OutboxRetentionJob>();
        services.AddScoped<AuditRetentionJob>();
        services.AddScoped<StorageMaintenanceJob>();
        services.AddScoped<ImportRetentionJob>();

        var connectionString = configuration.GetConnectionString(
            PersistenceServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required when background jobs are enabled.");
        }

        var settings = configuration.GetSection(BackgroundJobSettings.SectionName)
            .Get<BackgroundJobSettings>() ?? new BackgroundJobSettings();
        services.AddHangfire(configurationBuilder => configurationBuilder
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = settings.SchemaName,
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(1),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            }));
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = settings.WorkerCount;
            options.Queues = ["critical", "notifications", "maintenance", "ai"];
        });
        services.AddHostedService<OutboxDispatcherBackgroundService>();

        return services;
    }

    public static void ScheduleDoSelectMaintenanceJobs(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var recurringJobs = services.GetRequiredService<IRecurringJobManager>();
        var taipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

        recurringJobs.AddOrUpdate<IdempotencyRetentionJob>(
            "maintenance:idempotency-retention",
            job => job.RunAsync(CancellationToken.None),
            "40 2 * * *",
            new RecurringJobOptions { TimeZone = taipeiTimeZone });
        recurringJobs.AddOrUpdate<StorageMaintenanceJob>(
            "maintenance:private-attachment-retention",
            job => job.CleanupPrivateAttachmentsAsync(CancellationToken.None),
            "20 3 * * *",
            new RecurringJobOptions { TimeZone = taipeiTimeZone });
        recurringJobs.AddOrUpdate<StorageMaintenanceJob>(
            "maintenance:product-image-retention",
            job => job.CleanupProductImagesAsync(CancellationToken.None),
            "40 3 * * *",
            new RecurringJobOptions { TimeZone = taipeiTimeZone });
        recurringJobs.AddOrUpdate<OutboxRetentionJob>(
            "maintenance:outbox-retention",
            job => job.RunAsync(CancellationToken.None),
            "0 4 * * *",
            new RecurringJobOptions { TimeZone = taipeiTimeZone });
        recurringJobs.AddOrUpdate<AuditRetentionJob>(
            "maintenance:audit-retention",
            job => job.RunAsync(CancellationToken.None),
            "20 4 * * *",
            new RecurringJobOptions { TimeZone = taipeiTimeZone });
        // 匯入暫存與庫存調整設計.md「清理由 maintenance Queue 執行」：列 24 小時、摘要 90 天。
        //
        // QueueName 必須明說（組長 PR #89 round 2 item 1）：Hangfire 1.8 的 recurring job 沒指定 queue
        // 就排到 default，而上面的 Server 只監聽 critical／notifications／maintenance／ai——排進 default
        // 的工作永遠不會被執行，看起來有排程、其實什麼都沒清。
        //
        // 每小時跑一次（round 2 item 3）：每日一次的話，03:01 進終態的批次要等到再隔天才被清，
        // 接近 48 小時。每小時一次，允許的清理延遲就是「24 小時／90 天的界線之後最多再一小時」。
        // 用 1.8 的非過時多載把 queue 當明確參數傳（RecurringJobOptions.QueueName 已標示過時）。
        recurringJobs.AddOrUpdate<ImportRetentionJob>(
            "maintenance:import-retention",
            "maintenance",
            job => job.RunAsync(CancellationToken.None),
            "0 * * * *",
            new RecurringJobOptions { TimeZone = taipeiTimeZone });
    }
}
