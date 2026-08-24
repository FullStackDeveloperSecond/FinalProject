namespace DoSelect.Application.Members;

/// <summary>會員清單查詢：正規化搜尋字串與分頁參數，其餘交給 <see cref="IAdminMemberQueryReader"/>。</summary>
public sealed class ListAdminMembersQuery
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IAdminMemberQueryReader _reader;

    public ListAdminMembersQuery(IAdminMemberQueryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public Task<AdminMemberListResult> ExecuteAsync(
        AdminMemberQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalized = query with
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            PageNumber = Math.Max(query.PageNumber, 1),
            PageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize),
        };

        return _reader.ListAsync(normalized, cancellationToken);
    }
}

public sealed class GetAdminMemberDetailQuery
{
    private readonly IAdminMemberQueryReader _reader;

    public GetAdminMemberDetailQuery(IAdminMemberQueryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public Task<AdminMemberDetailSnapshot?> ExecuteAsync(
        Guid publicId, CancellationToken cancellationToken = default) =>
        _reader.FindDetailAsync(publicId, cancellationToken);
}

public sealed class UpdateAdminMemberProfileCommand
{
    private readonly IAdminMemberWriter _writer;

    public UpdateAdminMemberProfileCommand(IAdminMemberWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    public Task<AdminMemberWriteResult> ExecuteAsync(
        Guid publicId,
        string displayName,
        DateOnly? birthDate,
        byte[] rowVersion,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        }

        return _writer.UpdateProfileAsync(
            publicId, displayName.Trim(), birthDate, rowVersion, updatedAtUtc, cancellationToken);
    }
}

/// <summary>
/// ⚠ PENDING ALEX POLICY REVIEW：對應 Member.ManageSensitive Policy（新提案，尚未核准）。
/// </summary>
public sealed class SetMemberAccountStatusCommand
{
    private readonly IAdminMemberWriter _writer;

    public SetMemberAccountStatusCommand(IAdminMemberWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    public Task<AdminMemberWriteResult> ExecuteAsync(
        Guid publicId,
        bool suspend,
        byte[] rowVersion,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default) =>
        _writer.SetAccountStatusAsync(publicId, suspend, rowVersion, occurredAtUtc, cancellationToken);
}

/// <summary>
/// ⚠ PENDING ALEX POLICY REVIEW：對應 Member.ManageSensitive Policy（新提案，尚未核准）。
/// 一律寄送重設密碼 Email，不由管理員直接指定新密碼。
/// </summary>
public sealed class ResetMemberPasswordCommand
{
    private readonly IAdminMemberPasswordResetInitiator _initiator;

    public ResetMemberPasswordCommand(IAdminMemberPasswordResetInitiator initiator)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        _initiator = initiator;
    }

    public Task<bool> ExecuteAsync(Guid publicId, CancellationToken cancellationToken = default) =>
        _initiator.SendResetPasswordEmailAsync(publicId, cancellationToken);
}
