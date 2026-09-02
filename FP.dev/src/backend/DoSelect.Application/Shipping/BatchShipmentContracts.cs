namespace DoSelect.Application.Shipping;

/// <summary>批次裡的一筆訂單。RowVersion 讓「別人剛動過這張訂單」在該筆失敗，而不是靜靜覆蓋。</summary>
public sealed record BatchShipmentOrderInput(Guid OrderPublicId, byte[] RowVersion);

/// <summary>
/// `shippingAction` 白名單（API DTO與Schema契約 `BatchShipmentRequest`）。
///
/// `createLabel` 只建立物流單並把訂單推到準備出貨；`markShipped` 直接標記為已出貨，也就是把庫存
/// 保留轉為 Consumed、扣掉實體庫存的那一步。兩者的差別完全在「庫存有沒有真的離開倉庫」。
/// </summary>
public static class BatchShipmentActions
{
    public const string CreateLabel = "createLabel";
    public const string MarkShipped = "markShipped";

    public static readonly IReadOnlyList<string> All = [CreateLabel, MarkShipped];
}

/// <summary>
/// `BatchShipmentRequest`（API DTO與Schema契約）：`orders:{orderPublicId,rowVersion}[1..100]`、
/// `shippingAction:createLabel/markShipped`、`idempotencyKey`。
/// </summary>
public sealed record BatchShipmentRequest(
    IReadOnlyList<BatchShipmentOrderInput> Orders,
    string ShippingAction,
    string IdempotencyKey);

/// <summary>
/// 一筆訂單的結果。<paramref name="SourceRowNumber"/> 是輸入清單裡的位置（1 起算）——結果 CSV
/// 契約要求「至少包含輸入列號」，管理員才對得回自己送出去的那份清單。
/// </summary>
public sealed record BatchShipmentItemResultDto(
    int SourceRowNumber,
    Guid OrderPublicId,
    string? OrderNumber,
    string Status,
    string? TrackingNumber,
    string? ErrorCode,
    string? Message);

/// <summary>
/// `BatchShipmentResultDto`：BatchPublicId、Total／Succeeded／Failed、逐筆結果、建立時間。
///
/// BatchPublicId 目前只是這次回應的識別碼，沒有對應的資料表——
/// `GET /api/v1/admin/shipments/batches/{id}/result.csv` 需要以它取回結果，而最終 Schema
/// （Terry-商品庫存物流組裝與報表最終Schema.md，M-11 六張表）刻意沒有 ShipmentBatch。畫面要的
/// 「逐筆結果與 CSV」由這份同步回應就地產生，不必等那支端點；是否要為重新下載新增一張表，留給
/// 組長裁定，不自行發明 schema。
/// </summary>
public sealed record BatchShipmentResultDto(
    Guid BatchPublicId,
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<BatchShipmentItemResultDto> Items,
    DateTime CreatedAtUtc);

/// <summary>
/// UC-ADM-SHIP-02 批次出貨（購物車、訂單、付款與物流.md §批次出貨）。
///
/// 每筆訂單獨立驗證、獨立交易、獨立回傳結果，**一筆失敗不回滾其他已成功出貨的訂單**。這一點
/// 與這個專案其他「整批單一交易」的流程（匯入、批次調價）正好相反，而且是刻意的：出貨是不可逆
/// 的實體動作，已經送出倉庫的貨不會因為清單裡另一張訂單有問題就回來。
///
/// 單批上限 100 筆；超過時整個 Request 回 `shipping_batch_limit_exceeded`，一筆都不開始處理。
/// </summary>
public interface IBatchShipmentService
{
    Task<BatchShipmentResultDto> ShipBatchAsync(
        BatchShipmentRequest request,
        string adminUserId,
        string correlationId,
        DateTime now,
        CancellationToken cancellationToken);
}
