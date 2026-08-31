using System.ComponentModel.DataAnnotations;
using DoSelect.Api.Common;
using DoSelect.Api.Security;
using DoSelect.Application.Common;
using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoSelect.Api.Invoicing;

/// <summary>
/// 後台模擬發票查詢（`API Endpoint目錄` 第 118 行）。
/// </summary>
/// <remarks>
/// <c>Invoice.Manage</c> 只負責授權。<b>完整個資仍需 <c>PersonalData.ViewFull</c></b>，
/// 不因為呼叫者是 FinanceManager 就回傳（`API DTO與Schema契約` 第 151 行）——
/// 所以買受人資料在這裡一樣是遮蔽後的。
/// </remarks>
[ApiController]
[Authorize(Policy = DoSelectPolicies.InvoiceManage)]
[Route("api/v1/admin/invoices")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminInvoicesController : ControllerBase
{
    private readonly InvoiceQueryService _invoices;

    public AdminInvoicesController(InvoiceQueryService invoices)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        _invoices = invoices;
    }

    [HttpGet]
    [ProducesResponseType<PageResult<AdminInvoiceSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PageResult<AdminInvoiceSummaryDto>>> List(
        [FromQuery] AdminInvoiceListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _invoices.ListAsync(
            new AdminInvoiceQuery(
                request.Statuses,
                request.FromUtc,
                request.ToUtc,
                request.Q,
                request.PageNumber,
                request.PageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminInvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminInvoiceDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var invoice = await _invoices.FindAsync(id, cancellationToken);
        if (invoice is null)
        {
            return NotFound(ApiProblemDetailsFactory.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                InvoiceErrorCodes.ResourceNotFound,
                detail: "The referenced invoice was not found."));
        }

        return Ok(invoice);
    }
}

/// <summary>
/// 後台發票清單的查詢字串（`API DTO與Schema契約` 第 149 行）。
/// </summary>
public sealed class AdminInvoiceListRequest
{
    /// <remarks>
    /// 可重複的 <c>statuses=</c>。不送、或送空集合，都代表不篩狀態 ——
    /// 與其他後台清單端點（優惠券、退貨、客服工單）的語意相同。
    /// </remarks>
    public IReadOnlyList<SimulatedInvoiceStatus>? Statuses { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    /// <remarks>只比對發票號碼；訂單編號在 Orders，發票 Reader 不碰它。</remarks>
    [StringLength(64)]
    public string? Q { get; init; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
