using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Admin.Members;

/// <summary>
/// ⚠ 新範圍：後台會員管理（會員列表 + 會員中心）沒有既有 API 規格，這是依 AdminOrder
/// 系列清單+詳細頁模式新設計的 Route/DTO/Policy，PR／日誌中標註待 alex 覆核。
/// </summary>
[ApiController]
[Route("api/v1/admin/members")]
public sealed class AdminMembersController(
    ListAdminMembersQuery listQuery,
    GetAdminMemberDetailQuery detailQuery,
    UpdateAdminMemberProfileCommand updateProfileCommand,
    SetMemberAccountStatusCommand setAccountStatusCommand,
    ResetMemberPasswordCommand resetPasswordCommand,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = DoSelectPolicies.MemberManage)]
    [ProducesResponseType(typeof(AdminMemberListResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminMemberListResponseDto>> List(
        [FromQuery] AdminMemberListRequestDto request, CancellationToken cancellationToken)
    {
        if (!TryParseStatus(request.Status, out var status))
        {
            return BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationFailed));
        }

        var result = await listQuery.ExecuteAsync(
            new AdminMemberQuery(
                request.Search, status, request.RegisteredFrom, request.RegisteredTo,
                request.PageNumber, request.PageSize),
            cancellationToken);

        var members = new PageResult<AdminMemberSummaryDto>(
            result.Members.Items.Select(ToSummaryDto).ToArray(),
            result.Members.PageNumber,
            result.Members.PageSize,
            result.Members.TotalCount);

        return Ok(new AdminMemberListResponseDto(
            members,
            new AdminMemberListStatsDto(
                result.Stats.TotalMembers, result.Stats.NewTodayCount, result.Stats.ActiveCount)));
    }

    [HttpGet("{publicId:guid}")]
    [Authorize(Policy = DoSelectPolicies.MemberManage)]
    [ProducesResponseType(typeof(AdminMemberDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminMemberDetailDto>> GetDetail(
        Guid publicId, CancellationToken cancellationToken)
    {
        var detail = await detailQuery.ExecuteAsync(publicId, cancellationToken);
        return detail is null
            ? NotFound(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status404NotFound, AdminMemberErrorCodes.MemberNotFound))
            : Ok(ToDetailDto(detail));
    }

    [HttpPut("{publicId:guid}")]
    [Authorize(Policy = DoSelectPolicies.MemberManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProfile(
        Guid publicId, [FromBody] UpdateAdminMemberProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await updateProfileCommand.ExecuteAsync(
            publicId,
            request.DisplayName,
            request.BirthDate,
            request.RowVersion,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>⚠ PENDING ALEX POLICY REVIEW：Member.ManageSensitive，新提案尚未核准。</summary>
    [HttpPost("{publicId:guid}/reset-password")]
    [Authorize(Policy = DoSelectPolicies.MemberManageSensitive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid publicId, CancellationToken cancellationToken)
    {
        var sent = await resetPasswordCommand.ExecuteAsync(publicId, cancellationToken);
        return sent
            ? NoContent()
            : NotFound(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status404NotFound, AdminMemberErrorCodes.MemberNotFound));
    }

    /// <summary>⚠ PENDING ALEX POLICY REVIEW：Member.ManageSensitive，新提案尚未核准。</summary>
    [HttpPost("{publicId:guid}/status")]
    [Authorize(Policy = DoSelectPolicies.MemberManageSensitive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetStatus(
        Guid publicId, [FromBody] SetMemberAccountStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await setAccountStatusCommand.ExecuteAsync(
            publicId, request.Suspend, request.RowVersion, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return ToActionResult(result);
    }

    private IActionResult ToActionResult(AdminMemberWriteResult result)
    {
        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.ErrorCode switch
        {
            AdminMemberErrorCodes.MemberNotFound => NotFound(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status404NotFound, result.ErrorCode)),
            AdminMemberErrorCodes.ConcurrencyConflict => Conflict(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status409Conflict, result.ErrorCode)),
            _ => BadRequest(ApiProblemDetailsFactory.Create(
                HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode ?? ApiErrorCodes.ValidationFailed)),
        };
    }

    private static bool TryParseStatus(string? raw, out AccountStatus? status)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            status = null;
            return true;
        }

        if (Enum.TryParse<AccountStatus>(raw, ignoreCase: true, out var parsed))
        {
            status = parsed;
            return true;
        }

        status = null;
        return false;
    }

    private static AdminMemberSummaryDto ToSummaryDto(AdminMemberRow row) =>
        new(row.PublicId, row.DisplayName, row.Email, row.RegisteredAtUtc, row.AccountStatus.ToString());

    private static AdminMemberDetailDto ToDetailDto(AdminMemberDetailSnapshot snapshot) =>
        new(
            snapshot.PublicId,
            snapshot.DisplayName,
            snapshot.Email,
            snapshot.Phone,
            snapshot.BirthDate,
            snapshot.RegisteredAtUtc,
            snapshot.AccountStatus.ToString(),
            snapshot.RowVersion,
            new AdminMemberStatsDto(
                snapshot.Stats.TotalSpend, snapshot.Stats.TotalOrderCount, snapshot.Stats.ReturnRatePercent),
            snapshot.RecentOrders
                .Select(o => new AdminMemberOrderSummaryDto(
                    o.OrderPublicId, o.OrderNumber, o.PlacedAtUtc, o.OrderStatus, o.GrandTotal))
                .ToArray(),
            snapshot.ActivityLog
                .Select(e => new AdminMemberActivityEventDto(e.OccurredAtUtc, e.EventType, e.Description))
                .ToArray());
}
