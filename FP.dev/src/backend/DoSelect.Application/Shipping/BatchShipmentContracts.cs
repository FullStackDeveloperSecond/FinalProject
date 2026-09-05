using DoSelect.Application.Auditing;

namespace DoSelect.Application.Shipping;

/// <summary>批次裡的一筆訂單。RowVersion 讓「別人剛動過這張訂單」在該筆失敗，而不是靜靜覆蓋。</summary>
public sealed record BatchShipmentOrderInput(Guid OrderPublicId, byte[] RowVersion);

/// <summary>
/// `shippingAction` 白名單（API DTO與Schema契約 `BatchShipmentRequest`）。
///
/// `createLabel` 只建立物流單並把訂單推到準備出貨；`markShipped` 直接標記為已出貨，也就是把庫存
/// 保留轉為 Consumed、扣掉實體庫存的那一步。兩者的差別完全在「庫存有沒有真的離開倉庫」。
///
/// 兩者可以接續：先 `createLabel` 印單，之後再 `markShipped` 把同一張物流單推到已出貨——後者會載入
/// 那張既有的 Preparing 物流單，不會建立第二筆（一張訂單只有一張主要物流單）。
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
///
/// `idempotencyKey` 是真的生效的：同一位管理員以同一把鍵重送同一份 payload，會重播第一次的
/// 逐筆結果（同一個 BatchPublicId），不會再出一次貨；同一把鍵搭配不同 payload 回
/// `idempotency_payload_conflict`。
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
/// BatchPublicId 是這一次批次作業的識別碼，由回應本身帶出；它保存在冪等記錄裡，所以用同一把
/// 冪等鍵重送會拿回同一個 BatchPublicId 與同一份逐筆結果。系統沒有 ShipmentBatch 資料表
/// （組長 PR #93 裁定 A1：不新增該表），畫面要的「逐筆結果與 CSV」由這份同步回應在前端就地產生。
///
/// <paramref name="IsReplay"/> 為 true 代表這份結果來自先前那一次送出——沒有任何訂單被重複出貨。
/// </summary>
public sealed record BatchShipmentResultDto(
    Guid BatchPublicId,
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<BatchShipmentItemResultDto> Items,
    DateTime CreatedAtUtc,
    bool IsReplay);

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
        AuditRequestContext auditContext,
        DateTime now,
        CancellationToken cancellationToken);
}
