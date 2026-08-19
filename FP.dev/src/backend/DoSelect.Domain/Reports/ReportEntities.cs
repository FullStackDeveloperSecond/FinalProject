using System.Security.Cryptography;
using System.Text;
using DoSelect.Domain.Common;
using DoSelect.Domain.Support;

namespace DoSelect.Domain.Reports;

public enum ReportTargetType { Product, ProductReview, SupportMessage, OtherPublicContent }
public enum ReportCaseStatus { Open, Assigned, InReview, Actioned, Rejected, Closed }

public sealed class ReportCase : MutablePublicEntity
{
    private static readonly IReadOnlyDictionary<ReportCaseStatus, ReportCaseStatus[]> Allowed = new Dictionary<ReportCaseStatus, ReportCaseStatus[]>
    {
        [ReportCaseStatus.Open] = [ReportCaseStatus.Assigned],
        [ReportCaseStatus.Assigned] = [ReportCaseStatus.InReview],
        [ReportCaseStatus.InReview] = [ReportCaseStatus.Actioned, ReportCaseStatus.Rejected],
        [ReportCaseStatus.Actioned] = [ReportCaseStatus.Closed],
        [ReportCaseStatus.Rejected] = [ReportCaseStatus.Closed],
        [ReportCaseStatus.Closed] = [ReportCaseStatus.Open],
    };
    private ReportCase() { }
    public ReportCase(Guid publicId, string reportNumber, string reporterUserId, ReportTargetType targetType, Guid targetPublicId, string reasonCode, string description, CasePriority priority, DateTime createdAtUtc) : base(publicId, createdAtUtc)
    { if (targetPublicId == Guid.Empty) throw new ArgumentException("TargetPublicId is required.", nameof(targetPublicId)); ReportNumber = RequireText(reportNumber, nameof(reportNumber)); ReporterUserId = RequireText(reporterUserId, nameof(reporterUserId)); TargetType = targetType; TargetPublicId = targetPublicId; ReasonCode = RequireText(reasonCode, nameof(reasonCode)); Description = RequireText(description, nameof(description)); Status = ReportCaseStatus.Open; Priority = priority; LastActivityAtUtc = createdAtUtc; OpenCaseKeyHash = ComputeOpenKey(ReporterUserId, targetType, targetPublicId, ReasonCode); }
    public string ReportNumber { get; private set; } = string.Empty; public string ReporterUserId { get; private set; } = string.Empty; public ReportTargetType TargetType { get; private set; }
    public Guid TargetPublicId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty; public string Description { get; private set; } = string.Empty; public ReportCaseStatus Status { get; private set; }
    public CasePriority Priority { get; private set; }
    public string? AssigneeAdminUserId { get; private set; }
    public string? ResolutionCode { get; private set; }
    public string? DecisionNote { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public byte[]? OpenCaseKeyHash { get; private set; }
    public void Assign(string adminUserId, DateTime occurredAtUtc) { AssigneeAdminUserId = RequireText(adminUserId, nameof(adminUserId)); Transition(ReportCaseStatus.Assigned, occurredAtUtc); }
    public void Decide(bool actioned, string resolutionCode, string decisionNote, DateTime occurredAtUtc)
    { if (Status != ReportCaseStatus.InReview) throw new InvalidOperationException("The report is not in review."); ResolutionCode = RequireText(resolutionCode, nameof(resolutionCode)); DecisionNote = RequireText(decisionNote, nameof(decisionNote)); Transition(actioned ? ReportCaseStatus.Actioned : ReportCaseStatus.Rejected, occurredAtUtc); ResolvedAtUtc = occurredAtUtc; }
    public void Transition(ReportCaseStatus next, DateTime occurredAtUtc)
    { if (!Allowed[Status].Contains(next)) throw new InvalidOperationException($"Report cannot move from {Status} to {next}."); occurredAtUtc = RequireUtc(occurredAtUtc, nameof(occurredAtUtc)); Status = next; ClosedAtUtc = next == ReportCaseStatus.Closed ? occurredAtUtc : ClosedAtUtc; OpenCaseKeyHash = next == ReportCaseStatus.Closed ? null : ComputeOpenKey(ReporterUserId, TargetType, TargetPublicId, ReasonCode); LastActivityAtUtc = occurredAtUtc; MarkUpdated(occurredAtUtc); }
    private static byte[] ComputeOpenKey(string reporter, ReportTargetType targetType, Guid targetId, string reason) => SHA256.HashData(Encoding.UTF8.GetBytes($"{reporter.Normalize().ToUpperInvariant()}|{targetType.ToString().ToUpperInvariant()}|{targetId:D}|{reason.Normalize().ToUpperInvariant()}"));
}

public sealed class ReportAttachment : MutablePublicEntity
{
    private ReportAttachment() { }
    public ReportAttachment(Guid publicId, long reportCaseId, string uploadedByUserId, string originalFileName, string storageKey, string extension, string mimeType, long fileSizeBytes, byte[] sha256, DateTime createdAtUtc) : base(publicId, createdAtUtc)
    { AttachmentRules.Validate(fileSizeBytes, sha256); if (reportCaseId <= 0) throw new ArgumentOutOfRangeException(nameof(reportCaseId)); ReportCaseId = reportCaseId; UploadedByUserId = RequireText(uploadedByUserId, nameof(uploadedByUserId)); OriginalFileName = RequireText(originalFileName, nameof(originalFileName)); StorageKey = RequireText(storageKey, nameof(storageKey)); Extension = RequireText(extension, nameof(extension)); MimeType = RequireText(mimeType, nameof(mimeType)); FileSizeBytes = fileSizeBytes; Sha256 = sha256.ToArray(); ScanStatus = PrivateAttachmentScanStatus.Pending; }
    public long ReportCaseId { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty; public string OriginalFileName { get; private set; } = string.Empty; public string StorageKey { get; private set; } = string.Empty;
    public string Extension { get; private set; } = string.Empty; public string MimeType { get; private set; } = string.Empty; public long FileSizeBytes { get; private set; }
    public byte[] Sha256 { get; private set; } = [];
    public PrivateAttachmentScanStatus ScanStatus { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public DateTime? RetentionUntilUtc { get; private set; }
    public bool LegalHold { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public void RecordScan(PrivateAttachmentScanStatus result, DateTime scannedAtUtc)
    { if (result == PrivateAttachmentScanStatus.Pending) throw new ArgumentException("A completed scan result is required.", nameof(result)); ScanStatus = result; ScannedAtUtc = RequireUtc(scannedAtUtc, nameof(scannedAtUtc)); MarkUpdated(scannedAtUtc); }
    public void ScheduleRetention(DateTime retentionUntilUtc, DateTime occurredAtUtc)
    { RetentionUntilUtc = RequireUtc(retentionUntilUtc, nameof(retentionUntilUtc)); MarkUpdated(occurredAtUtc); }
    public void SetLegalHold(bool enabled, DateTime occurredAtUtc)
    { LegalHold = enabled; MarkUpdated(occurredAtUtc); }
    public void SoftDelete(DateTime deletedAtUtc)
    { if (LegalHold) throw new InvalidOperationException("An attachment under legal hold cannot be deleted."); DeletedAtUtc = RequireUtc(deletedAtUtc, nameof(deletedAtUtc)); MarkUpdated(deletedAtUtc); }
}

public sealed class ReportAssignmentHistory
{
    private ReportAssignmentHistory() { }
    public ReportAssignmentHistory(long reportCaseId, string? fromAdminUserId, string? toAdminUserId, AssignmentAction action, string? reason, string? actorUserId, DateTime occurredAtUtc)
    { if (reportCaseId <= 0 || occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid report assignment history."); ReportCaseId = reportCaseId; FromAdminUserId = Optional(fromAdminUserId); ToAdminUserId = Optional(toAdminUserId); Action = action; Reason = Optional(reason); ActorUserId = Optional(actorUserId); OccurredAtUtc = occurredAtUtc; }
    public long Id { get; private set; }
    public long ReportCaseId { get; private set; }
    public string? FromAdminUserId { get; private set; }
    public string? ToAdminUserId { get; private set; }
    public AssignmentAction Action { get; private set; }
    public string? Reason { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReportStatusHistory
{
    private ReportStatusHistory() { }
    public ReportStatusHistory(long reportCaseId, ReportCaseStatus? fromStatus, ReportCaseStatus toStatus, string actionCode, string? reason, string? actorUserId, DateTime occurredAtUtc)
    { if (reportCaseId <= 0 || occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid report status history."); ReportCaseId = reportCaseId; FromStatus = fromStatus; ToStatus = toStatus; ActionCode = Required(actionCode); Reason = Optional(reason); ActorUserId = Optional(actorUserId); OccurredAtUtc = occurredAtUtc; }
    public long Id { get; private set; }
    public long ReportCaseId { get; private set; }
    public ReportCaseStatus? FromStatus { get; private set; }
    public ReportCaseStatus ToStatus { get; private set; }
    public string ActionCode { get; private set; } = string.Empty; public string? Reason { get; private set; }
    public string? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim(); private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class CaseWorkbenchRow
{
    private CaseWorkbenchRow() { }
    public Guid CasePublicId { get; private set; }
    public string CaseType { get; private set; } = string.Empty; public string CaseNumber { get; private set; } = string.Empty; public string Title { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty; public CasePriority Priority { get; private set; }
    public string RequesterDisplay { get; private set; } = string.Empty; public Guid? AssigneePublicId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public DateTime? SlaDueAtUtc { get; private set; }
    public bool IsOverdue { get; private set; }
}
