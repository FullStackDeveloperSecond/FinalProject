---
type: decision-record
batch_id: AUTO-DEC-014
title: 共用冪等與購物車合併衝突持久化定版
status: applied
created_at: 2026-08-22
applied_at: 2026-08-22
source: alex 選擇 B，先由 alex 完成共用前置並合併至 dev，再請 terry rebase 修正 PR #28
---

# AUTO-DEC-014｜共用冪等與購物車合併衝突持久化定版

## 正式決策

1. `IdempotencyRecord`、交易執行器與資料庫約束是 alex 維護的共用能力；Owner 功能不得各自建立平行的 Idempotency Entity、Configuration 或 reservation 流程。
2. Actor Scope 只接受後端已驗證資源的 PublicId。資料庫保存以伺服器 Pepper 執行 HMAC-SHA-256 後的 `binary(32)`，不得保存內部 User Id、Cookie、Token 或原始 Scope。
3. reservation、業務資料異動、可重播結果與完成狀態必須由同一 `DoSelectDbContext` 及同一 SQL Server 交易提交；Handler 失敗時整體 rollback，不得留下已提交的 `Processing`。
4. `ActorScopeHash + Operation + Key` 建立唯一索引；同 Key／同 Request Hash 重播原結果，同 Key／不同 Payload 回 `409 idempotency_payload_conflict`，同時抵達但尚在處理的請求回 `409 idempotency_request_in_progress` 與 `Retry-After: 3`。
5. 可重播摘要採版本化 JSON、上限 32 KiB，超過時只保存結果資源 PublicId 並由 Reader 重建。紀錄保存 24 小時並使用 `rowversion`；仍為 `Processing` 的紀錄不得由到期重用流程刪除。
6. 購物車合併超過數量上限或庫存時，必須保存 `CartMergeConflict`；未明確 Resolve 前持續阻擋 Checkout。衝突紀錄保留 Member／Guest Cart、Guest Item 與 SKU PublicId、合併前數量、接受數量、原因、解決碼、時間與 `rowversion`。
7. 共用前置合併 `dev` 後，Terry 的 PR #28 必須 rebase `dev`，移除私人 Idempotency 實作並改用共用 Executor／Entity；再把購物車衝突寫入、查詢、解決與 Checkout Gate 接上。

## 最低成本與商業影響

- 只用 Comment 或文件無法消除重複 Entity、缺 Migration 與跨交易提交；設定也無法提供資料庫唯一性及 rollback，因此最低充分方案是沿用單一 DbContext，新增一條共用 Application／Infrastructure 執行路徑與兩張必要資料表。
- 受影響者為會員購物車使用者，以及後續訂單、退款、發票等高風險命令的開發者。現況風險是重試造成重複副作用、失敗後永久卡在 Processing，或合併衝突消失後錯誤允許結帳。
- 建置成本限於共用 Domain／Application／Infrastructure、單一 additive Migration 與 SQL Server 整合測試；不新增外部套件、服務或 recurring spend。
- 成功指標為同鍵重播只執行一次、不同 Payload 穩定衝突、同時請求只有一個 winner、Handler 失敗全交易 rollback，以及 unresolved cart conflict 可持久查得並阻擋 Checkout。

## 風險與回復條件

- Migration 只允許新增 `IdempotencyRecords`、`CartMergeConflicts` 及其索引／約束，不得修改或刪除既有資料；本 PR 不套用到 `DoSelectDb`。
- Pepper 只能由 User Secrets、環境變數或部署 Secret 提供，至少 32 UTF-8 bytes；Repository、回應、Log 與資料庫不得出現明文值。
- 若 migration script 出現既有欄位改型、Drop／Rename、預期外資料回填或非 additive SQL，停止合併並重新 scaffold。
- 若共用 Executor 無法維持同一交易或 provider-backed 併發測試失敗，回復此獨立前置 PR；不得以 Terry 分支的私人實作作長期雙軌替代。
