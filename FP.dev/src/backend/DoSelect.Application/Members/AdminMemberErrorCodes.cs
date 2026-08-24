namespace DoSelect.Application.Members;

public static class AdminMemberErrorCodes
{
    public const string MemberNotFound = "member_not_found";

    /// <summary>值與 DoSelect.Api.Common.ApiErrorCodes.ConcurrencyConflict 一致，保持跨層一致。</summary>
    public const string ConcurrencyConflict = "concurrency_conflict";
}
