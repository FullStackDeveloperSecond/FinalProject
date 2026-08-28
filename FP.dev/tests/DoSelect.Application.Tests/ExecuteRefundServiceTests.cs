using DoSelect.Application.Common;
using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Tests;

/// <summary>
/// 退款執行的純決策（`POST /api/v1/admin/refunds/{id}/actions/execute`）。
/// </summary>
/// <remarks>
/// 這一層負責：RowVersion 閘門、可執行狀態、餘額上限，以及 E1 的可信快照拒絕。
/// **冪等不在這一層** —— 重播與 Payload 衝突由共用 <c>IIdempotencyExecutor</c> 判斷，
/// 走到 <see cref="RefundExecutionDecision.Evaluate"/> 的一定是首次執行。
/// </remarks>
public sealed class ExecuteRefundServiceTests
{
    private static readonly Guid RefundPublicId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OrderItemPublicId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static readonly byte[] CurrentRowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] StaleRowVersion = [1, 2, 3, 4, 5, 6, 7, 9];

    [Fact]
    public async Task PreviewAsync_ReturnsNotFoundForAnUnknownRefund()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(snapshot: null));

        var result = await service.PreviewAsync(Request());

        Assert.Equal(RefundErrorCodes.ResourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task AStaleRowVersionIsRejected()
    {
        // rowversion 只能擋「伺服器讀取之後」的競爭；管理員拿著舊畫面按下執行時，
        // 版本在送進來的當下就已經過期，只有前置比對擋得住。
        var result = await EvaluateAsync(Request(rowVersion: StaleRowVersion));

        Assert.Equal(RefundErrorCodes.ConcurrencyConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task TheRowVersionIsCheckedBeforeAnythingElse()
    {
        // 版本過期時，呼叫端看到的狀態與金額都不可信，不該再依它判斷其他條件。
        var result = await EvaluateAsync(
            Request(rowVersion: StaleRowVersion),
            Snapshot(status: RefundStatus.Cancelled, approvedAmount: null));

        Assert.Equal(RefundErrorCodes.ConcurrencyConflict, result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task ARowVersionOfTheWrongLengthIsAValidationError(int length)
    {
        // SQL Server 的 rowversion 固定 8 bytes。長度不對的值不可能是任何一列的版本，
        // 應該回 400，而不是走到比對失敗後變成語意不對的 409。
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        await Assert.ThrowsAsync<DomainProblemException>(
            () => service.PreviewAsync(Request(rowVersion: new byte[length])));
    }

    [Fact]
    public async Task AnApprovedRefundWithinTheBalanceIsAccepted()
    {
        var result = await EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.Plan!.Amount);
        Assert.Equal("finance-1", result.Plan.ExecutedByAdminUserId);
        Assert.Equal(CurrentRowVersion, result.Plan.ExpectedRefundRowVersion);
    }

    [Fact]
    public async Task TheSignedAllocationTotalEqualsTheApprovedAmount()
    {
        // 這條才是分攤的真正保證。先前只斷言「非空且每筆為正」，那在
        // 分攤合計與核准金額不同時**照樣通過** —— 而那正是會寫出一筆自我矛盾
        // 財務紀錄的情況：SucceededAmount 記核准金額，分攤合計卻是另一個數。
        var result = await EvaluateAsync(Request(), Snapshot(approvedAmount: 500m));

        Assert.True(result.IsSuccess);

        var signedTotal = result.Plan!.Allocations.Sum(allocation =>
            RefundPolicy.DirectionOf(allocation.Type) == RefundAllocationDirection.Credit
                ? allocation.Amount
                : -allocation.Amount);

        Assert.Equal(500m, signedTotal);
        Assert.Equal(result.Plan.Amount, signedTotal);
        Assert.All(result.Plan.Allocations, allocation => Assert.True(allocation.Amount > 0m));
    }

    [Fact]
    public async Task AnApprovedAmountThatDisagreesWithTheCalculationIsRefused()
    {
        // 可信快照算出 500（1 件 × 500，無運費、無折扣），但退款只核准 400。
        // 執行下去會讓 SucceededAmount 與稽核寫 400、分攤合計卻是 500，
        // 退款交易、分攤與後續發票折讓永久對不起來，而且分攤寫入後不可變。
        var result = await EvaluateAsync(Request(), Snapshot(approvedAmount: 400m));

        Assert.Equal(RefundErrorCodes.RefundCalculationMismatch, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task AnApprovedAmountAboveTheCalculationIsAlsoRefused()
    {
        // 兩個方向都要擋：核准金額大於可信快照算出的淨額同樣是矛盾。
        var result = await EvaluateAsync(
            Request(), Snapshot(approvedAmount: 600m, refundableBalance: 5000m));

        Assert.Equal(RefundErrorCodes.RefundCalculationMismatch, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task AnAmountExactlyEqualToTheBalanceIsAllowed()
    {
        var result = await EvaluateAsync(
            Request(), Snapshot(approvedAmount: 500m, refundableBalance: 500m));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AnAmountAboveTheRefundableBalanceIsRejected()
    {
        var result = await EvaluateAsync(
            Request(), Snapshot(approvedAmount: 1001m, refundableBalance: 1000m));

        Assert.Equal(RefundErrorCodes.RefundAmountExceeded, result.ErrorCode);
    }

    [Fact]
    public async Task AFailedRefundCanBeRetried()
    {
        // Refund.AllowedTransitions 本來就允許 Failed → Processing，ApprovedAmount 也
        // 保留著。只認 Approved 會讓一次暫時性失敗變成永久卡死。
        var result = await EvaluateAsync(Request(), Snapshot(status: RefundStatus.Failed));

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.Plan!.Amount);
    }

    [Fact]
    public async Task ASucceededRefundIsAStateConflictNotAReplay()
    {
        // 重播由外層 Executor 判定並回放，根本不會走到這裡。走到這裡代表換了一把
        // 新金鑰再送一次已完成的退款 —— 那是狀態衝突，不得產生第二次副作用。
        var result = await EvaluateAsync(
            Request(), Snapshot(status: RefundStatus.Succeeded, succeededAmount: 500m));

        Assert.Equal(RefundErrorCodes.RefundStateConflict, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Theory]
    [InlineData(RefundStatus.PendingReview)]
    [InlineData(RefundStatus.Rejected)]
    [InlineData(RefundStatus.Processing)]
    [InlineData(RefundStatus.Cancelled)]
    public async Task AnyOtherStatusIsRejected(RefundStatus status)
    {
        var result = await EvaluateAsync(Request(), Snapshot(status: status));

        Assert.Equal(RefundErrorCodes.RefundStateConflict, result.ErrorCode);
    }

    [Fact]
    public async Task AnApprovedRefundWithoutAnApprovedAmountIsRejected()
    {
        var result = await EvaluateAsync(Request(), Snapshot(approvedAmount: null));

        Assert.Equal(RefundErrorCodes.RefundStateConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ExecutionIsRefusedWhileTheTrustedSnapshotIsIncomplete()
    {
        // E1：上游可信資料未齊全時必須拒絕，不得以估算值或管理端傳入的分攤補齊。
        // 分攤寫入即不可變，估算值會讓對帳與發票折讓永久失真。
        //
        // 專屬碼而非 refund_state_conflict：後者會讓管理員去查退款狀態，
        // 但實際原因是退貨核准端的資料還沒齊（alex 於 PR #16 裁定）。
        var result = await EvaluateAsync(Request(), Snapshot(withTrustedInputs: false));

        Assert.Equal(RefundErrorCodes.RefundSnapshotUnavailable, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task TheBalanceIsCheckedBeforeTheTrustedSnapshot()
    {
        // 超額退款是呼叫端看得懂也修得掉的問題；快照缺漏不是。先回前者比較有用。
        var result = await EvaluateAsync(
            Request(),
            Snapshot(approvedAmount: 5000m, refundableBalance: 1000m, withTrustedInputs: false));

        Assert.Equal(RefundErrorCodes.RefundAmountExceeded, result.ErrorCode);
    }

    [Fact]
    public async Task ACalculatorFailureKeepsItsOwnErrorCode()
    {
        // 對帳檢查加上去之後差點吃掉這個：計算失敗時 NetRefundAmount 是 0，
        // 若不先看 IsSuccess，每一種失敗都會被改寫成同一個對帳不一致的碼，
        // 呼叫端收到的原因與實際發生的事無關。
        //
        // 退貨數量超過可退數量：品項只有 2 件、已退 0 件，卻要求退 3 件。
        var result = await EvaluateAsync(
            Request(),
            Snapshot(approvedAmount: 1500m, refundableBalance: 5000m, requestedQuantity: 3));

        Assert.Equal(RefundErrorCodes.ReturnQuantityExceeded, result.ErrorCode);
        Assert.NotEqual(RefundErrorCodes.RefundCalculationMismatch, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Theory]
    [InlineData("contact me@example.com")]
    [InlineData("see <b>here</b>")]
    public async Task AnUnsafeNoteIsAValidationErrorNotAServerError(string note)
    {
        // 中央 Audit 會拒收這些輸入並丟 ArgumentException。不在邊界擋，
        // 那個例外會落到 GlobalExceptionHandler 變成 500，但呼叫端只是
        // 送了格式不合的理由。
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.PreviewAsync(Request(note: note)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(DomainErrorCodes.ValidationFailed, exception.Code);
    }

    [Fact]
    public async Task AnUnsafeReasonCodeIsAlsoAValidationError()
    {
        // reason 只接受 safe-code（ASCII 英數與 ._-:）。
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        var exception = await Assert.ThrowsAsync<DomainProblemException>(
            () => service.PreviewAsync(Request(reasonCode: "客戶要求")));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task RequireWellFormedRejectsAMissingIdempotencyKey()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        await Assert.ThrowsAsync<DomainProblemException>(
            () => service.PreviewAsync(Request(idempotencyKey: "   ")));
    }

    [Fact]
    public async Task RequireWellFormedRejectsAMissingAdministrator()
    {
        var service = new ExecuteRefundService(new FakeRefundExecutionReader(Snapshot()));

        await Assert.ThrowsAsync<DomainProblemException>(
            () => service.PreviewAsync(Request(executedByAdminUserId: "  ")));
    }

    [Fact]
    public void TheRequestCarriesNoAllocationsOrAmounts()
    {
        // 契約層級的保證（DEC-P287）：管理端連指定會計拆分的欄位都沒有。
        var properties = typeof(ExecuteRefundRequest).GetProperties();

        Assert.DoesNotContain(properties, property =>
            property.PropertyType == typeof(decimal) ||
            property.PropertyType == typeof(decimal?));
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Allocation", StringComparison.Ordinal));
    }

    [Fact]
    public void TheExecutionReasonIsNotStoredOnTheRefund()
    {
        // reasonCode 與 note 只寫中央 AuditLog，不在 Refund 重複建欄位（DEC-P289）。
        var properties = typeof(Refund).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Note", properties);
        Assert.DoesNotContain("ExecutionReasonCode", properties);
    }

    [Fact]
    public void TheDecisionLayerDoesNotOwnIdempotency()
    {
        // 這一層曾拿「建立退款時保存的金鑰」去比對執行請求 —— 那是兩個不同操作的
        // 金鑰，正常使用新金鑰的呼叫端會直接被判 idempotency_payload_conflict。
        // 重播改由共用 IIdempotencyExecutor 負責，這條守住比對不會被加回來。
        Assert.DoesNotContain(
            typeof(RefundExecutionSnapshot).GetProperties().Select(property => property.Name),
            name => name.Contains("IdempotencyKey", StringComparison.Ordinal));

        Assert.DoesNotContain(
            typeof(ExecuteRefundResult).GetMembers().Select(member => member.Name),
            name => name.Equals("IsReplay", StringComparison.Ordinal));
    }

    private static async Task<ExecuteRefundResult> EvaluateAsync(
        ExecuteRefundRequest request,
        RefundExecutionSnapshot? snapshot = null) =>
        await new ExecuteRefundService(new FakeRefundExecutionReader(snapshot ?? Snapshot()))
            .PreviewAsync(request);

    private static ExecuteRefundRequest Request(
        byte[]? rowVersion = null,
        string idempotencyKey = "refund-execute-1",
        string executedByAdminUserId = "  finance-1  ",
        string reasonCode = "customer_request",
        string? note = null) =>
        new(
            RefundPublicId,
            rowVersion ?? CurrentRowVersion,
            idempotencyKey,
            executedByAdminUserId,
            reasonCode,
            note,
            CorrelationId: "corr-1",
            TraceId: new string('a', 32),
            RemoteIpAddress: null);

    private static RefundExecutionSnapshot Snapshot(
        RefundStatus status = RefundStatus.Approved,
        decimal? approvedAmount = 500m,
        decimal? succeededAmount = null,
        decimal refundableBalance = 1000m,
        bool withTrustedInputs = true,
        int requestedQuantity = 1) =>
        new(
            11L,
            status,
            approvedAmount,
            succeededAmount,
            refundableBalance,
            CurrentRowVersion,
            withTrustedInputs ? TrustedInputsFor(requestedQuantity) : null);

    /// <summary>
    /// 一份齊全的可信快照，用來驗證「資料到位時決策確實會放行」。
    /// </summary>
    /// <remarks>
    /// 讀取端的上游欄位已經落地，這裡組出的是齊全的快照。依 E1，任一項缺漏時
    /// 讀取端回 <c>null</c>、整筆拒絕；那條路徑由本檔的其他測試涵蓋。
    /// </remarks>
    private static RefundTrustedInputs TrustedInputsFor(int requestedQuantity) => new(
        new RefundOrderSnapshot(
            Lines:
            [
                new RefundOrderLine(
                    OrderItemPublicId,
                    Quantity: 2,
                    AlreadyReturnedQuantity: 0,
                    FinalUnitPrice: 500m,
                    DiscountAllocation: 0m,
                    IsCouponEligible: false),
            ],
            ShippingFeePaid: 0m,
            ShippingMethodBaseFee: 0m,
            FreeShippingThreshold: null,
            AssemblyFee: 0m,
            CouponDiscountTotal: 0m,
            CouponEligibleSubtotal: 0m,
            CouponMinimumSpend: null),
        Lines: [new RefundLineRequest(OrderItemPublicId, requestedQuantity)],
        Reason: ReturnReason.Defective,
        AssemblyDisposition: AssemblyFeeDisposition.NotApplicable,
        ReturnShippingCost: 0m);

    private sealed class FakeRefundExecutionReader : IRefundExecutionReader
    {
        private readonly RefundExecutionSnapshot? _snapshot;

        public FakeRefundExecutionReader(RefundExecutionSnapshot? snapshot) => _snapshot = snapshot;

        public Task<RefundExecutionSnapshot?> FindAsync(
            Guid refundPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);
    }
}
