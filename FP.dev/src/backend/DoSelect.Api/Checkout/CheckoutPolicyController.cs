using DoSelect.Application.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Checkout;

/// <summary>
/// 提供顧客送出 Checkout 前必須明確接受的目前政策版本。
/// </summary>
/// <remarks>
/// 只投影顧客輸入契約本來就要求的 Terms、Return 與 Privacy 版本；
/// ShippingConstraint 是伺服器端交易規則，不屬於顧客可接受或控制的欄位。
/// </remarks>
[ApiController]
[Route("api/v1/checkout")]
public sealed class CheckoutPolicyController(ICheckoutPolicyProvider policyProvider) : ControllerBase
{
    [HttpGet("policy-versions")]
    [AllowAnonymous]
    [ProducesResponseType<AcceptedPolicyVersions>(StatusCodes.Status200OK)]
    public ActionResult<AcceptedPolicyVersions> GetPolicyVersions()
    {
        var current = policyProvider.Current;
        return Ok(new AcceptedPolicyVersions(
            current.Terms,
            current.Return,
            current.Privacy));
    }
}
