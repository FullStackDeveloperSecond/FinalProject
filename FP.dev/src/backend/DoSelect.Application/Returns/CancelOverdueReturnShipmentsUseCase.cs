namespace DoSelect.Application.Returns;

/// <summary>
/// The callable Application boundary for the overdue-shipment auto-cancel rule ("到期仍未交寄
/// 時，系統自動將退貨申請轉為 Cancelled"). No Hangfire/Outbox/scheduler exists anywhere in
/// origin/dev yet (checked: no job infrastructure under src/backend at all), so per the M-12
/// functional analysis this stops at a callable, idempotent Application handler rather than
/// inventing a hosted timer. Wiring this to an actual recurring trigger — Hangfire, a Windows
/// Service, a minimal-API cron endpoint guarded by an internal-only policy, whatever the team
/// eventually adopts — is an integration dependency for whoever owns that decision (alex/SH),
/// not implemented here.
/// </summary>
public interface ICancelOverdueReturnShipmentsUseCase
{
    Task<CancelOverdueReturnShipmentsResult> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed record CancelOverdueReturnShipmentsResult(IReadOnlyList<Guid> CancelledReturnPublicIds)
{
    public int Count => CancelledReturnPublicIds.Count;
}

public sealed class CancelOverdueReturnShipmentsUseCase : ICancelOverdueReturnShipmentsUseCase
{
    private readonly IReturnStore _store;
    private readonly TimeProvider _timeProvider;

    public CancelOverdueReturnShipmentsUseCase(IReturnStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<CancelOverdueReturnShipmentsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var cancelled = await _store.CancelOverdueAwaitingShipmentAsync(nowUtc, cancellationToken);
        return new CancelOverdueReturnShipmentsResult(cancelled);
    }
}
