using DoSelect.Application.Payments;
using DoSelect.Domain.Payments;

namespace DoSelect.Application.Tests;

/// <summary>
/// 最新一筆付款嘗試的擁有者比對與終態語意。
/// </summary>
/// <remarks>
/// 排序與資料庫行為由 <c>LatestPaymentAttemptReaderSqlServerTests</c> 負責 ——
/// 假的 Reader 直接回傳指定的那一筆，證明不了 SQL 真的排對了。
/// </remarks>
public sealed class LatestPaymentAttemptServiceTests
{
    private static readonly Guid OrderPublicId = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime NowUtc = new(2026, 9, 1, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheOwnerGetsTheirLatestAttempt()
    {
        var service = CreateService();

        var result = await service.FindLatestAsync(
            new PaymentAttemptViewer.Member("member-1"), OrderPublicId);

        var found = Assert.IsType<LatestPaymentAttemptResult.Found>(result);
        Assert.Equal(1000m, found.Attempt.Amount);
    }

    [Fact]
    public async Task AnotherMemberIsToldToTryTheGuestCookieRatherThanBeingRefused()
    {
        // MemberAccessDenied 不是對外的答案 —— 同一台裝置可以同時有會員 cookie
        // 與某張訪客訂單的有效 token（Issue #86 C1）。
        var service = CreateService();

        var result = await service.FindLatestAsync(
            new PaymentAttemptViewer.Member("someone-else"), OrderPublicId);

        Assert.IsType<LatestPaymentAttemptResult.MemberAccessDenied>(result);
    }

    [Fact]
    public async Task AVerifiedGuestGetsTheAttemptWithoutASecondOwnershipCheck()
    {
        // 訪客的 Scope 已由 GuestOrderAccessScopeAuthorizer 對「這一張訂單」驗過。
        var service = CreateService(memberUserId: null);

        var result = await service.FindLatestAsync(new PaymentAttemptViewer.Guest(), OrderPublicId);

        Assert.IsType<LatestPaymentAttemptResult.Found>(result);
    }

    [Fact]
    public async Task AnUnknownOrderIsNotFound()
    {
        var service = new LatestPaymentAttemptService(
            new FakeReader(order: null, attempt: Attempt()));

        Assert.IsType<LatestPaymentAttemptResult.NotFound>(
            await service.FindLatestAsync(
                new PaymentAttemptViewer.Member("member-1"), OrderPublicId));
    }

    [Fact]
    public async Task AnOrderWithNoAttemptIsNotFound()
    {
        // 付款頁靠這個回到「建立付款方式」流程。
        var service = CreateService(withoutAttempt: true);

        Assert.IsType<LatestPaymentAttemptResult.NotFound>(
            await service.FindLatestAsync(
                new PaymentAttemptViewer.Member("member-1"), OrderPublicId));
    }

    [Theory]
    [InlineData(PaymentAttemptStatus.Failed)]
    [InlineData(PaymentAttemptStatus.Expired)]
    [InlineData(PaymentAttemptStatus.Cancelled)]
    [InlineData(PaymentAttemptStatus.Paid)]
    public async Task ATerminalAttemptIsStillReturned(PaymentAttemptStatus status)
    {
        // Issue #86 A1：終態不視同「沒有付款嘗試」。把終態濾掉的話，付款失敗後
        // 重新整理就再也看不到失敗原因。
        var service = CreateService(attempt: Attempt(status));

        var found = Assert.IsType<LatestPaymentAttemptResult.Found>(
            await service.FindLatestAsync(
                new PaymentAttemptViewer.Member("member-1"), OrderPublicId));
        Assert.Equal(status, found.Attempt.Status);
    }

    [Fact]
    public async Task ADeferredAttemptKeepsTheInstructionTheShopperNeeds()
    {
        // ATM／超商代碼是使用者要拿去繳費的東西，正是重新整理後最不能掉的欄位。
        var service = CreateService(attempt: Attempt(method: PaymentMethod.ATM));

        var found = Assert.IsType<LatestPaymentAttemptResult.Found>(
            await service.FindLatestAsync(
                new PaymentAttemptViewer.Member("member-1"), OrderPublicId));

        Assert.NotNull(found.Attempt.Instruction);
        Assert.Equal("SIM-REFERENCE", found.Attempt.Instruction!.Code);
    }

    [Fact]
    public async Task TheReaderIsNeverAskedForAttemptsWhenTheMemberIsNotTheOwner()
    {
        // 不是擁有者就不該再去查那張訂單的付款嘗試 —— 那是沒有必要的讀取。
        var reader = new FakeReader(
            new PaymentAttemptOrderReference(7L, "member-1"), Attempt());
        var service = new LatestPaymentAttemptService(reader);

        await service.FindLatestAsync(new PaymentAttemptViewer.Member("someone-else"), OrderPublicId);

        Assert.Equal(0, reader.FindLatestCalls);
    }

    private static LatestPaymentAttemptService CreateService(
        string? memberUserId = "member-1",
        PaymentAttempt? attempt = null,
        bool withoutAttempt = false) =>
        new(new FakeReader(
            new PaymentAttemptOrderReference(7L, memberUserId),
            withoutAttempt ? null : attempt ?? Attempt()));

    private static PaymentAttempt Attempt(
        PaymentAttemptStatus status = PaymentAttemptStatus.AwaitingPayment,
        PaymentMethod method = PaymentMethod.CreditCard)
    {
        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            7L,
            method,
            1000m,
            "SIM",
            $"key-{Guid.NewGuid():N}",
            NowUtc.AddHours(1),
            NowUtc);
        attempt.SetPaymentInstruction("SIM-REFERENCE", NowUtc);

        // 從 AwaitingPayment 走到指定狀態；狀態機不允許跳過中間步驟。
        switch (status)
        {
            case PaymentAttemptStatus.AwaitingPayment:
                break;
            case PaymentAttemptStatus.Expired:
            case PaymentAttemptStatus.Cancelled:
                attempt.Transition(status, NowUtc);
                break;
            case PaymentAttemptStatus.Paid:
                attempt.Transition(PaymentAttemptStatus.Processing, NowUtc);
                attempt.Transition(PaymentAttemptStatus.Paid, NowUtc);
                break;
            case PaymentAttemptStatus.Failed:
                attempt.Transition(PaymentAttemptStatus.Processing, NowUtc);
                attempt.Transition(PaymentAttemptStatus.Failed, NowUtc, "simulated_failure");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return attempt;
    }

    private sealed class FakeReader : ILatestPaymentAttemptReader
    {
        private readonly PaymentAttemptOrderReference? _order;
        private readonly PaymentAttempt? _attempt;

        public FakeReader(PaymentAttemptOrderReference? order, PaymentAttempt? attempt)
        {
            _order = order;
            _attempt = attempt;
        }

        public int FindLatestCalls { get; private set; }

        public Task<PaymentAttemptOrderReference?> FindOrderAsync(
            Guid orderPublicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_order);

        public Task<PaymentAttempt?> FindLatestAsync(
            long orderId, CancellationToken cancellationToken = default)
        {
            FindLatestCalls++;
            return Task.FromResult(_attempt);
        }
    }
}
