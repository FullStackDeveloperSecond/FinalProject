using DoSelect.Application.Common;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Tests;

/// <summary>
/// 退款核准的純決策（`POST /api/v1/admin/refunds/{id}/actions/approve`，alex 2026-09-04
/// #98 WP2 裁定）。
/// </summary>
/// <remarks>
/// 這一層負責：RowVersion 閘門、可核准狀態（只有 <c>PendingReview</c>）、E1 的可信快照
/// 拒絕，以及核准金額由後端重新計算——管理端不傳金額。**冪等不在這一層**，與
/// <see cref="RefundExecutionDecision"/> 同一個理由：走到 <see cref="RefundApprovalDecision.Evaluate"/>
/// 的一定是首次核准。
/// </remarks>
public sealed class RefundApprovalDecisionTests
{
    private static readonly Guid RefundPublicId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OrderItemPublicId = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static readonly byte[] CurrentRowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] StaleRowVersion = [1, 2, 3, 4, 5, 6, 7, 9];

    [Fact]
    public void AStaleRowVersionIsRejected()
    {
        // rowversion 只能擋「伺服器讀取之後」的競爭；管理員拿著舊畫面按下核准時，
        // 版本在送進來的當下就已經過期，只有前置比對擋得住。
        var result = Evaluate(Request(rowVersion: StaleRowVersion));

        Assert.Equal(RefundErrorCodes.ConcurrencyConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void TheRowVersionIsCheckedBeforeAnythingElse()
    {
        var result = Evaluate(
            Request(rowVersion: StaleRowVersion),
            Snapshot(status: RefundStatus.Cancelled));

        Assert.Equal(RefundErrorCodes.ConcurrencyConflict, result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(16)]
    public void ARowVersionOfTheWrongLengthIsAValidationError(int length)
    {
        var exception = Assert.Throws<DomainProblemException>(
            () => RefundApprovalDecision.RequireWellFormed(Request(rowVersion: new byte[length])));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void APendingReviewRefundWithACompleteSnapshotIsApproved()
    {
        var result = Evaluate(Request());

        Assert.True(result.IsSuccess);
        // 訂單快照：單價 500 × 退 1 件、無折扣、非完整退貨、退貨運費 0、組裝費不退 = 500。
        Assert.Equal(500m, result.Plan!.ApprovedAmount);
        Assert.Equal("finance-1", result.Plan.ApprovedByAdminUserId);
        Assert.Equal(CurrentRowVersion, result.Plan.ExpectedRefundRowVersion);
    }

    [Theory]
    [InlineData(RefundStatus.Approved)]
    [InlineData(RefundStatus.Rejected)]
    [InlineData(RefundStatus.Processing)]
    [InlineData(RefundStatus.Succeeded)]
    [InlineData(RefundStatus.Failed)]
    [InlineData(RefundStatus.Cancelled)]
    public void OnlyPendingReviewCanBeApproved(RefundStatus status)
    {
        // Refund.AllowedTransitions：只有 PendingReview 能到 Approved。任何其他狀態
        // 代表這筆退款已經被核准過、拒絕過，或還在執行／已結清。
        var result = Evaluate(Request(), Snapshot(status: status));

        Assert.Equal(RefundErrorCodes.RefundStateConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void ApprovalIsRefusedWhileTheTrustedSnapshotIsIncomplete()
    {
        // E1：上游可信資料未齊全時必須拒絕，與執行端同一個專屬碼。
        var result = Evaluate(Request(), Snapshot(withTrustedInputs: false));

        Assert.Equal(RefundErrorCodes.RefundSnapshotUnavailable, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void ACalculatorFailureKeepsItsOwnErrorCode()
    {
        // 退貨數量超過可退數量：品項只有 2 件、已退 0 件，卻要求退 3 件。這是真正的
        // 計算錯誤，不該被改寫成對帳不一致的碼。
        var result = Evaluate(Request(), Snapshot(requestedQuantity: 3));

        Assert.Equal(RefundErrorCodes.ReturnQuantityExceeded, result.ErrorCode);
        Assert.NotEqual(RefundErrorCodes.RefundCalculationMismatch, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void AZeroOrNegativeRecomputedAmountIsRefused()
    {
        // #99 A1 的同一個行為：RefundCalculator 對淨額 <= 0 回
        // Failure(RefundAmountExceeded)，不是 Success。在核准這一刻遇到代表建立
        // 這筆退款之後，同一張訂單又受理了其他退貨，可信快照重算出的淨額已經降到
        // 0 或負數——退款維持 PendingReview，需要管理員另外處理。
        var result = Evaluate(
            Request(),
            Snapshot(requestedAmount: 500m, clawbackExceedsRefund: true));

        Assert.Equal(RefundErrorCodes.RefundAmountExceeded, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void ARecomputedAmountAboveTheRequestedAmountIsRefused()
    {
        // 結構上這個方向理論上不會發生（AlreadyReturnedQuantity 只增不減，淨額只會
        // 持平或降低）；仍防禦，避免直接呼叫 Refund.Approve 時因為
        // approvedAmount > RequestedAmount 丟未分類的例外變成 500。
        var result = Evaluate(Request(), Snapshot(requestedAmount: 100m));

        Assert.Equal(RefundErrorCodes.RefundCalculationMismatch, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void ARecomputedAmountEqualToTheRequestedAmountIsAllowed()
    {
        var result = Evaluate(Request(), Snapshot(requestedAmount: 500m));

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.Plan!.ApprovedAmount);
    }

    [Theory]
    [InlineData("contact me@example.com")]
    [InlineData("see <b>here</b>")]
    public void AnUnsafeNoteIsAValidationErrorNotAServerError(string note)
    {
        var exception = Assert.Throws<DomainProblemException>(
            () => RefundApprovalDecision.RequireWellFormed(Request(note: note)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, exception.Code);
    }

    [Fact]
    public void AnUnsafeReasonCodeIsAlsoAValidationError()
    {
        var exception = Assert.Throws<DomainProblemException>(
            () => RefundApprovalDecision.RequireWellFormed(Request(reasonCode: "客戶要求")));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void RequireWellFormedRejectsAMissingIdempotencyKey() =>
        Assert.Throws<DomainProblemException>(
            () => RefundApprovalDecision.RequireWellFormed(Request(idempotencyKey: "   ")));

    [Fact]
    public void RequireWellFormedRejectsAnOversizedIdempotencyKey()
    {
        var exception = Assert.Throws<DomainProblemException>(
            () => RefundApprovalDecision.RequireWellFormed(Request(idempotencyKey: new string('k', 129))));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, exception.Code);
    }

    [Fact]
    public void RequireWellFormedRejectsAMissingAdministrator() =>
        Assert.Throws<DomainProblemException>(
            () => RefundApprovalDecision.RequireWellFormed(Request(approvedByAdminUserId: "  ")));

    [Fact]
    public void TheRequestCarriesNoAllocationsOrAmounts()
    {
        // 契約層級的保證（alex #98 WP2 裁定）：管理端連指定金額的欄位都沒有。
        var properties = typeof(ApproveRefundRequest).GetProperties();

        Assert.DoesNotContain(properties, property =>
            property.PropertyType == typeof(decimal) ||
            property.PropertyType == typeof(decimal?));
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Allocation", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDecisionLayerDoesNotOwnIdempotency()
    {
        Assert.DoesNotContain(
            typeof(RefundApprovalSnapshot).GetProperties().Select(property => property.Name),
            name => name.Contains("IdempotencyKey", StringComparison.Ordinal));

        Assert.DoesNotContain(
            typeof(ApproveRefundResult).GetMembers().Select(member => member.Name),
            name => name.Equals("IsReplay", StringComparison.Ordinal));
    }

    private static ApproveRefundResult Evaluate(
        ApproveRefundRequest request,
        RefundApprovalSnapshot? snapshot = null)
    {
        RefundApprovalDecision.RequireWellFormed(request);
        return RefundApprovalDecision.Evaluate(snapshot ?? Snapshot(), request);
    }

    private static ApproveRefundRequest Request(
        byte[]? rowVersion = null,
        string idempotencyKey = "refund-approve-1",
        string approvedByAdminUserId = "  finance-1  ",
        string reasonCode = "customer_request",
        string? note = null) =>
        new(
            RefundPublicId,
            rowVersion ?? CurrentRowVersion,
            idempotencyKey,
            approvedByAdminUserId,
            reasonCode,
            note,
            CorrelationId: "corr-1",
            TraceId: new string('a', 32),
            RemoteIpAddress: null);

    private static RefundApprovalSnapshot Snapshot(
        RefundStatus status = RefundStatus.PendingReview,
        decimal requestedAmount = 500m,
        bool withTrustedInputs = true,
        int requestedQuantity = 1,
        bool clawbackExceedsRefund = false) =>
        new(
            11L,
            status,
            requestedAmount,
            CurrentRowVersion,
            withTrustedInputs
                ? TrustedInputsFor(requestedQuantity, clawbackExceedsRefund)
                : null);

    /// <summary>
    /// 一份齊全的可信快照。<paramref name="clawbackExceedsRefund"/> 為 true 時複製
    /// <c>RefundCalculatorTests.WhenTheClawbackSwallowsTheWholeRefund_TheAmountIsRejected</c>
    /// 的同一組觸發資料：優惠券扣回 500，退貨後保留小計遠低於門檻 3000。
    /// </summary>
    private static RefundTrustedInputs TrustedInputsFor(
        int requestedQuantity, bool clawbackExceedsRefund) => new(
        new RefundOrderSnapshot(
            Lines:
            [
                new RefundOrderLine(
                    OrderItemPublicId,
                    Quantity: 2,
                    AlreadyReturnedQuantity: 0,
                    FinalUnitPrice: 500m,
                    DiscountAllocation: 0m,
                    IsCouponEligible: true),
            ],
            ShippingFeePaid: 0m,
            ShippingMethodBaseFee: 0m,
            FreeShippingThreshold: null,
            AssemblyFee: 0m,
            CouponDiscountTotal: clawbackExceedsRefund ? 500m : 0m,
            CouponEligibleSubtotal: 0m,
            CouponMinimumSpend: clawbackExceedsRefund ? 3000m : null),
        Lines: [new RefundLineRequest(OrderItemPublicId, requestedQuantity)],
        Reason: ReturnReason.Defective,
        AssemblyDisposition: AssemblyFeeDisposition.NotApplicable,
        ReturnShippingCost: 0m);
}
