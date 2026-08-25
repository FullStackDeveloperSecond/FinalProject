using System.Reflection;
using DoSelect.Application.Returns;
using DoSelect.Domain.Returns;

namespace DoSelect.Application.Tests.Returns;

public sealed class CancelOverdueReturnShipmentsUseCaseTests
{
    private static readonly DateTimeOffset NowOffset = new(2026, 8, 24, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = NowOffset.UtcDateTime;
    private static readonly FieldInfo RequestIdField =
        typeof(ReturnRequest).BaseType!.BaseType!.BaseType!
            .GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public async Task ExecuteAsync_CancelsOnlyRequestsPastTheirShipmentDeadline()
    {
        var store = new FakeReturnStore();
        var overdue = MakeAwaitingShipment("RT-1", dueAtUtc: NowUtc.AddDays(-1));
        var notYetDue = MakeAwaitingShipment("RT-2", dueAtUtc: NowUtc.AddDays(1));
        RequestIdField.SetValue(overdue, 1L);
        RequestIdField.SetValue(notYetDue, 2L);
        store.Requests.Add(overdue);
        store.Requests.Add(notYetDue);

        var useCase = new CancelOverdueReturnShipmentsUseCase(store, new FixedTimeProvider(NowOffset));
        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.Count);
        Assert.Equal(overdue.PublicId, result.CancelledReturnPublicIds[0]);
        Assert.Equal(ReturnRequestStatus.Cancelled, overdue.Status);
        Assert.Equal(ReturnRequestStatus.AwaitingShipment, notYetDue.Status);
    }

    [Fact]
    public async Task ExecuteAsync_SecondRun_IsIdempotentAndCancelsNothingMore()
    {
        var store = new FakeReturnStore();
        var overdue = MakeAwaitingShipment("RT-1", dueAtUtc: NowUtc.AddDays(-1));
        RequestIdField.SetValue(overdue, 1L);
        store.Requests.Add(overdue);
        var useCase = new CancelOverdueReturnShipmentsUseCase(store, new FixedTimeProvider(NowOffset));
        await useCase.ExecuteAsync(CancellationToken.None);

        var secondRun = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Empty(secondRun.CancelledReturnPublicIds);
        Assert.Equal(ReturnRequestStatus.Cancelled, overdue.Status);
    }

    private static ReturnRequest MakeAwaitingShipment(string returnNumber, DateTime dueAtUtc)
    {
        var request = new ReturnRequest(Guid.NewGuid(), returnNumber, 1, "member-a", "Defective", "面板有亮點", 1, NowUtc.AddDays(-20));
        request.Transition(ReturnRequestStatus.UnderReview, NowUtc.AddDays(-19));
        // Approve() sets ReturnShipmentDueAtUtc = occurredAtUtc + 7 days, so approving "7 days
        // before" the desired due date lands exactly on it.
        request.Approve("admin-1", requiresShipment: true, dueAtUtc.AddDays(-7));
        return request;
    }
}
