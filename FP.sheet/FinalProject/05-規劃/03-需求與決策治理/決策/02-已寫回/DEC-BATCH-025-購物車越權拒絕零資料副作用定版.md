---
type: decision-record
batch_id: DEC-BATCH-025
title: 購物車越權拒絕零資料副作用定版
status: applied
created_at: 2026-08-25
applied_at: 2026-08-25
source: alex 確認修正 Actor B 越權修改或刪除時建立空購物車的副作用
decision_ids:
  - DEC-P308
---

# DEC-BATCH-025｜購物車越權拒絕零資料副作用定版

## 背景

QA-08 Review 發現，購物車修改與刪除會先呼叫 `ResolveOrCreateCartAsync`。全新訪客 Key 或尚無 Cart 的會員以 Actor A 的 Item PublicId 呼叫時，系統雖回傳 404 且未修改 Actor A 資料，仍會先新增 Actor B 空購物車。這不符合 SEC-ACC-02 對拒絕請求之資料庫前後快照無副作用要求。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P308 | `PATCH /api/v1/cart/items/{id}` 與 `DELETE /api/v1/cart/items/{id}` 只查詢呼叫者既有、Active 且未逾期的購物車；不存在或已逾期時使用既有 `404 resource_not_found`，不得建立 Cart、更新逾期狀態或產生其他資料與外部副作用。讀取、新增商品、重新驗證及 Merge 的既有建立行為不變。Guest 與 Member Actor B 測試均須證明 Actor A 資料／RowVersion 不變且 Cart 資料列總數不增加。 |

## 最低成本分析

1. 接受目前行為：拒絕請求仍新增資料，不符合 SEC-ACC-02，排除。
2. 只收窄測試或文件的「無副作用」用語：無法消除實際部分寫入，排除。
3. 以設定或資料清理補救：請求當下仍先產生不必要資料，且清理不能證明原子拒絕，排除。
4. 沿用既有 EF Core 身分條件，為修改／刪除增加只查既有 Cart 的最小路徑：不改 API、Schema 或依賴即可滿足驗收，採用。

## 商業影響

- 受影響者：訪客、會員、購物車維運者與安全 Reviewer。
- 目前風險：每次全新身分越權或誤用 Item PublicId 都可能新增一筆無商品 Cart，造成不必要資料與不實的拒絕零副作用證據。
- 觸及頻率：只有修改／刪除購物車商品且呼叫者沒有可用 Cart 的拒絕路徑。
- 預期可量測成果：四個 Guest／Member Actor B 寫入與刪除案例均維持 404，且請求前後 Cart 筆數、Actor A Item／Cart RowVersion、數量與資料列完全相同；呼叫者既有 Cart 已逾期時也不得更新 Cart／Item 或新增 Cart。
- 建置與持續成本：一個既有 Cart 查詢 helper、兩個 call site、四個 Actor B 增強斷言與一個過期 Cart provider-backed 案例；無新套件、Schema、Migration、服務或持續費用。
- 主要風險成本：修改／刪除在沒有 Cart 時不再自動建立空 Cart；既有前端本就應先取得或建立 Cart，且錯誤契約仍為 404。
- 信心：高；修正前四案穩定重現 N→N+1，修正後同一組案例維持 N→N；過期 Cart provider-backed 案例 1／1 通過。
- 成功指標：本機 SQL Server focused 測試、完整 Required CI 與 Actor 覆蓋矩陣皆通過。
- 停止／回退條件：若正式需求日後明確要求任一購物車命令都建立 Cart，必須另行定版並重新設計 SEC-ACC-02 的原子邊界；不得直接恢復拒絕前寫入。

## 影響文件與程式

- `FP.dev/src/backend/DoSelect.Infrastructure/Shopping/EfCartService.cs`
- `FP.dev/tests/DoSelect.Api.IntegrationTests/Shopping/CartApiTests.cs`
- `FP.dev/tests/DoSelect.Infrastructure.Tests/Shopping/CartServiceTests.cs`
- [[03-架構/08-測試與驗收/QA-08私人資源授權覆蓋矩陣]]
- [[03-架構/08-測試與驗收/測試策略]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
