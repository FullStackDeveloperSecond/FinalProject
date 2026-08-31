using System.Security.Claims;
using DoSelect.Api.Common;
using DoSelect.Api.Contracts.Members;
using DoSelect.Api.Security;
using DoSelect.Application.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Members;

/// <summary>M 會員資料／收件地址支撐（API Endpoint目錄.md）。</summary>
[ApiController]
[Authorize(Policy = DoSelectPolicies.Member)]
[Route("api/v1/members/me")]
public sealed class MembersController(IMemberProfileGateway gateway) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MemberProfileResponse>> GetProfile(CancellationToken cancellationToken)
    {
        var profile = await gateway.GetProfileAsync(RequireMemberUserId(), cancellationToken);
        return profile is null
            ? ResourceNotFoundProblem()
            : Ok(MemberProfileResponse.From(profile));
    }

    [HttpPut]
    public async Task<ActionResult<MemberProfileResponse>> UpdateProfile(
        [FromBody] UpdateMemberProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await gateway.UpdateProfileAsync(
            RequireMemberUserId(), request.ToCommand(), cancellationToken);

        return result switch
        {
            UpdateMemberProfileOutcome.Success success => Ok(MemberProfileResponse.From(success.Dto)),
            UpdateMemberProfileOutcome.ConcurrencyConflict => ConcurrencyConflictProblem(),
            _ => Problem(),
        };
    }

    [HttpGet("addresses")]
    public async Task<ActionResult<IReadOnlyList<MemberAddressResponse>>> ListAddresses(
        CancellationToken cancellationToken)
    {
        var addresses = await gateway.ListAddressesAsync(RequireMemberUserId(), cancellationToken);
        return Ok(addresses.Select(MemberAddressResponse.From).ToList());
    }

    [HttpPost("addresses")]
    public async Task<ActionResult<MemberAddressResponse>> CreateAddress(
        [FromBody] CreateMemberAddressRequest request,
        CancellationToken cancellationToken)
    {
        var result = await gateway.CreateAddressAsync(
            RequireMemberUserId(), request.ToInput(), cancellationToken);

        return result switch
        {
            MemberAddressWriteOutcome.Success success => CreatedAtAction(
                nameof(ListAddresses), null, MemberAddressResponse.From(success.Dto)),
            MemberAddressWriteOutcome.ConcurrencyConflict => ConcurrencyConflictProblem(),
            _ => Problem(),
        };
    }

    [HttpPut("addresses/{id:guid}")]
    public async Task<ActionResult<MemberAddressResponse>> UpdateAddress(
        Guid id,
        [FromBody] UpdateMemberAddressRequest request,
        CancellationToken cancellationToken)
    {
        var result = await gateway.UpdateAddressAsync(
            RequireMemberUserId(), id, request.ToCommand(), cancellationToken);

        return result switch
        {
            MemberAddressWriteOutcome.Success success => Ok(MemberAddressResponse.From(success.Dto)),
            MemberAddressWriteOutcome.NotFound => ResourceNotFoundProblem(),
            MemberAddressWriteOutcome.ConcurrencyConflict => ConcurrencyConflictProblem(),
            _ => Problem(),
        };
    }

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(
        Guid id,
        [FromBody] DeleteMemberAddressRequest request,
        CancellationToken cancellationToken)
    {
        var result = await gateway.DeleteAddressAsync(
            RequireMemberUserId(), id, request.RowVersion, cancellationToken);

        return result switch
        {
            MemberAddressWriteOutcome.Success => NoContent(),
            MemberAddressWriteOutcome.NotFound => ResourceNotFoundProblem(),
            MemberAddressWriteOutcome.ConcurrencyConflict => ConcurrencyConflictProblem(),
            _ => Problem(),
        };
    }

    private string RequireMemberUserId()
    {
        var memberUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            throw new InvalidOperationException("Authenticated member request is missing its identifier claim.");
        }

        return memberUserId;
    }

    private ObjectResult ResourceNotFoundProblem() => ProblemResult(
        StatusCodes.Status404NotFound,
        ApiErrorCodes.ResourceNotFound,
        "The referenced resource was not found.");

    private ObjectResult ConcurrencyConflictProblem() => ProblemResult(
        StatusCodes.Status409Conflict,
        ApiErrorCodes.ConcurrencyConflict,
        "The resource was updated by someone else. Reload and try again.");

    private ObjectResult ProblemResult(int statusCode, string code, string detail)
    {
        var problemDetails = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code, detail: detail);
        var result = new ObjectResult(problemDetails) { StatusCode = statusCode };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
