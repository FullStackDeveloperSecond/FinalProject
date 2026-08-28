using DoSelect.Application.Files;
using DoSelect.Domain.Idempotency;
using DoSelect.Domain.Catalog;
using DoSelect.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageOptions = DoSelect.Application.Storage.StorageOptions;

namespace DoSelect.Infrastructure.Outbox;

public sealed class IdempotencyRetentionJob(
    DoSelectDbContext context,
    TimeProvider timeProvider)
{
    public const int BatchSize = 500;

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var records = await context.IdempotencyRecords
            .Where(record =>
                record.Status == IdempotencyStatus.Succeeded &&
                record.ExpiresAtUtc <= nowUtc)
            .OrderBy(record => record.ExpiresAtUtc)
            .ThenBy(record => record.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return 0;
        }

        context.IdempotencyRecords.RemoveRange(records);
        await context.SaveChangesAsync(cancellationToken);
        return records.Count;
    }
}

public sealed class OutboxRetentionJob(
    DoSelectDbContext context,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan ProcessedRetention = TimeSpan.FromDays(30);
    public const int BatchSize = 500;

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime - ProcessedRetention;
        var messages = await context.OutboxMessages
            .Where(message =>
                message.Status == Domain.Outbox.OutboxMessageStatus.Processed &&
                message.ProcessedAtUtc < cutoffUtc)
            .OrderBy(message => message.ProcessedAtUtc)
            .ThenBy(message => message.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        context.OutboxMessages.RemoveRange(messages);
        await context.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }
}

public sealed class AuditRetentionJob(
    DoSelectDbContext context,
    TimeProvider timeProvider)
{
    public const int BatchSize = 500;

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var logs = await context.AuditLogs
            .Where(log => !log.IsLegalHold && log.RetentionUntilUtc <= nowUtc)
            .OrderBy(log => log.RetentionUntilUtc)
            .ThenBy(log => log.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (logs.Count == 0)
        {
            return 0;
        }

        context.AuditLogs.RemoveRange(logs);
        await context.SaveChangesAsync(cancellationToken);
        return logs.Count;
    }
}

public sealed class StorageMaintenanceJob(
    DoSelectDbContext context,
    IPrivateFileStorage privateFileStorage,
    IImageStorage imageStorage,
    IOptions<StorageOptions> storageOptions,
    TimeProvider timeProvider,
    ILogger<StorageMaintenanceJob> logger)
{
    public static readonly TimeSpan TemporaryRetention = TimeSpan.FromHours(24);
    public static readonly TimeSpan DeletedProductImageRetention = TimeSpan.FromDays(30);
    public const int BatchSize = 200;

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> CleanupPrivateAttachmentsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var cleaned = 0;

        var supportAttachments = await context.SupportAttachments
            .Where(attachment =>
                !attachment.LegalHold &&
                attachment.DeletedAtUtc == null &&
                attachment.RetentionUntilUtc <= nowUtc)
            .OrderBy(attachment => attachment.RetentionUntilUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var attachment in supportAttachments)
        {
            if (await privateFileStorage.DeleteAsync(attachment.StorageKey, cancellationToken))
            {
                attachment.SoftDelete(nowUtc);
                await context.SaveChangesAsync(cancellationToken);
                cleaned++;
            }
        }

        if (cleaned >= BatchSize)
        {
            return cleaned;
        }

        var returnAttachments = await context.ReturnAttachments
            .Where(attachment =>
                !attachment.LegalHold &&
                attachment.DeletedAtUtc == null &&
                attachment.RetentionUntilUtc <= nowUtc)
            .OrderBy(attachment => attachment.RetentionUntilUtc)
            .Take(BatchSize - cleaned)
            .ToListAsync(cancellationToken);
        foreach (var attachment in returnAttachments)
        {
            if (await privateFileStorage.DeleteAsync(attachment.StorageKey, cancellationToken))
            {
                attachment.SoftDelete(nowUtc);
                await context.SaveChangesAsync(cancellationToken);
                cleaned++;
            }
        }

        if (cleaned >= BatchSize)
        {
            return cleaned;
        }

        var reportAttachments = await context.ReportAttachments
            .Where(attachment =>
                !attachment.LegalHold &&
                attachment.DeletedAtUtc == null &&
                attachment.RetentionUntilUtc <= nowUtc)
            .OrderBy(attachment => attachment.RetentionUntilUtc)
            .Take(BatchSize - cleaned)
            .ToListAsync(cancellationToken);
        foreach (var attachment in reportAttachments)
        {
            if (await privateFileStorage.DeleteAsync(attachment.StorageKey, cancellationToken))
            {
                attachment.SoftDelete(nowUtc);
                await context.SaveChangesAsync(cancellationToken);
                cleaned++;
            }
        }

        return cleaned;
    }

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> CleanupProductImagesAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var cutoffUtc = nowUtc - DeletedProductImageRetention;
        var cleaned = 0;
        var images = await context.ProductImages
            .Where(image =>
                image.Status == ProductImageStatus.Deleted &&
                image.DeletedAtUtc < cutoffUtc)
            .OrderBy(image => image.DeletedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var image in images)
        {
            if (!await imageStorage.DeleteAsync(image.StorageKey, cancellationToken))
            {
                logger.LogWarning(
                    "A deleted product image could not be removed from storage. ProductImagePublicId={ProductImagePublicId}",
                    image.PublicId);
                continue;
            }

            context.ProductImages.Remove(image);
            await context.SaveChangesAsync(cancellationToken);
            cleaned++;
        }

        cleaned += await CleanupOrphanProductImageDirectoriesAsync(
            cutoffUtc,
            BatchSize - cleaned,
            cancellationToken);
        cleaned += CleanupOldTemporaryEntries(nowUtc, BatchSize - cleaned);
        return cleaned;
    }

    private async Task<int> CleanupOrphanProductImageDirectoriesAsync(
        DateTime cutoffUtc,
        int remaining,
        CancellationToken cancellationToken)
    {
        if (remaining <= 0)
        {
            return 0;
        }

        var referencedKeys = new HashSet<string>(
            await context.ProductImages
                .AsNoTracking()
                .Select(image => image.StorageKey)
                .ToListAsync(cancellationToken),
            StringComparer.Ordinal);
        var dataRoot = Path.GetFullPath(storageOptions.Value.DataRoot);
        var imageRoot = Path.Combine(dataRoot, "product-images");
        if (!Directory.Exists(imageRoot))
        {
            return 0;
        }

        var rootPrefix = Path.TrimEndingDirectorySeparator(imageRoot) + Path.DirectorySeparatorChar;
        var cleaned = 0;
        foreach (var prefixDirectory in Directory.EnumerateDirectories(imageRoot))
        {
            foreach (var imageDirectory in Directory.EnumerateDirectories(prefixDirectory))
            {
                if (cleaned >= remaining)
                {
                    return cleaned;
                }

                var resolvedDirectory = Path.GetFullPath(imageDirectory);
                var attributes = File.GetAttributes(resolvedDirectory);
                if (!resolvedDirectory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                    attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    Directory.GetLastWriteTimeUtc(resolvedDirectory) > cutoffUtc)
                {
                    continue;
                }

                var prefix = Path.GetFileName(prefixDirectory);
                var imageId = Path.GetFileName(resolvedDirectory);
                var storageKey = $"product-images/{prefix}/{imageId}";
                if (referencedKeys.Contains(storageKey))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(resolvedDirectory, recursive: true);
                    cleaned++;
                }
                catch (IOException exception)
                {
                    logger.LogWarning(exception, "Orphan product image cleanup failed.");
                }
                catch (UnauthorizedAccessException exception)
                {
                    logger.LogWarning(exception, "Orphan product image cleanup was denied.");
                }
            }
        }

        return cleaned;
    }

    private int CleanupOldTemporaryEntries(DateTime nowUtc, int remaining)
    {
        if (remaining <= 0)
        {
            return 0;
        }

        var cutoffUtc = nowUtc - TemporaryRetention;
        var dataRoot = Path.GetFullPath(storageOptions.Value.DataRoot);
        var roots = new[]
        {
            Path.Combine(dataRoot, "quarantine"),
            Path.Combine(dataRoot, "image-staging"),
        };
        var cleaned = 0;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                if (cleaned >= remaining)
                {
                    return cleaned;
                }

                try
                {
                    if (File.Exists(entry) && File.GetLastWriteTimeUtc(entry) <= cutoffUtc)
                    {
                        File.Delete(entry);
                        cleaned++;
                    }
                    else if (Directory.Exists(entry) && Directory.GetLastWriteTimeUtc(entry) <= cutoffUtc)
                    {
                        Directory.Delete(entry, recursive: true);
                        cleaned++;
                    }
                }
                catch (IOException exception)
                {
                    logger.LogWarning(exception, "Temporary storage entry cleanup failed.");
                }
                catch (UnauthorizedAccessException exception)
                {
                    logger.LogWarning(exception, "Temporary storage entry cleanup was denied.");
                }
            }
        }

        return cleaned;
    }
}
