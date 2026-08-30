---
type: decision-record
batch_id: AUTO-DEC-015
title: 購物車組裝群組整組移除API定版
status: applied
created_at: 2026-08-28
applied_at: 2026-08-28
source: 組長 PR #29 round 7 review P1（「組裝群組不可拆分後，缺少真正可執行的整組移除／修復路徑」），要求先完成最小 API 設計裁定（原文引用 alex）再實作；terry／Claude 依 [[05-規劃/03-需求與決策治理/決策/決策分級與自動定案原則|決策分級與自動定案原則]] M 級標準（「若已有需求與安全慣例可推得，Codex 可定案並明列假設」）自行定案 — 本項不新增或變更任何商業規則，只是把既有「組裝群組不可拆分」規則（PR #29 round-6 已定案、CartAssemblyItemImmutable 已存在）落成一個可執行的操作，且完全沿用本檔案既有的 RemoveItemAsync／CartRowVersion 慣例，屬於「Endpoint 拆分」等級的 M 級決策，不需升級為互動表單。
---

# AUTO-DEC-015｜購物車組裝群組整組移除API定版

## 背景

組長 PR #29 round 7 review 指出：組裝群組（`AssemblyGroupKey` 非 null 的一組 `CartItem`）已經被禁止逐項調整數量／移除（`CartAssemblyItemImmutable`，PR #29 round-6 review 定案），但目前完全沒有任何「整組」操作。若群組內某個 SKU 之後變成缺貨、下架或不可用：

1. Revalidate 會回報 issue 並阻擋 Checkout；
2. 前端沒有整組移除的按鈕；
3. 後端會拒絕任何逐項調整／移除的嘗試；
4. `CartIssueDto.availableActions` 對組裝品項仍回傳 `reduce-quantity`／`remove`，這兩個動作實際上都會被 `CartAssemblyItemImmutable` guard 拒絕——UI 在說謊。

這個缺口沒有任何合法的復原路徑，購物車會永久卡住。

## 正式決策

1. **新增服務方法** `ICartService.RemoveAssemblyGroupAsync(CartIdentity identity, Guid assemblyGroupKey, byte[] cartRowVersion, CancellationToken)`，回傳 `CartDto`（與 `RemoveItemAsync` 同一組回傳慣例）。
2. **新增 Endpoint** `DELETE /api/v1/cart/assembly-groups/{assemblyGroupKey:guid}`，Request Body 為 `RemoveAssemblyGroupRequest(byte[] CartRowVersion)`——沿用 `RemoveCartItemRequest` 「RowVersion 放在 DELETE Body」的既有慣例（`CartController.cs` remarks 已明文承認這是本專案自訂慣例，非外部標準）。用 Cart 層級的 RowVersion（不是單一 Item 的），因為群組本質是多列，沒有單一 Item RowVersion 可以代表整組。
3. **原子性**：後端在同一個 `DbContext.SaveChangesAsync()` 呼叫內刪除該 `AssemblyGroupKey` 的全部 `CartItem` 列——EF Core 的 `SaveChangesAsync` 本身就是單一交易，同一批次的多筆刪除天生具備「全部成功或全部不成功」的原子性，不需要額外的顯式 `BeginTransactionAsync`（沿用 `AddAssemblyGroupsAsync`／`RemoveItemAsync` 現有的隱式單交易模式，不是新模式）。
4. **樂觀併發**：沿用既有 `_dbContext.Entry(cart).Property(c => c.RowVersion).OriginalValue = cartRowVersion` 模式（`AddItemAsync`／`UpdateItemQuantityAsync` 已經這樣做），對 Cart 本身的 RowVersion 做併發檢查；衝突時丟出既有的 `ShoppingWriteException.ErrorCodes.ConcurrencyConflict`（`SaveWithConcurrencyCheckAsync` 既有邏輯，不新增錯誤碼）。
5. **Revalidate 誠實回報**：`BuildItemsAsync` 組裝 `CartIssueDto.AvailableActions` 時，若該品項的 `AssemblyGroupKey` 非 null，一律回傳 `["remove-group"]`（新的動作代碼），取代原本會被後端拒絕的 `reduce-quantity`／`remove`。前端用 `issue.itemPublicId` 對照 `cart.items` 找出該品項的 `assemblyGroupKey`，不需要在 `CartIssueDto` 新增欄位。
6. **前端**：新增 `useRemoveAssemblyGroup()`（`useCart.ts`，沿用既有 mutation identity-snapshot／cache-write 模式），`CartPage.vue` 對整組品項顯示「整組移除」按鈕，直接呼叫這一個新 Endpoint——明確禁止用前端連續呼叫多個單品 `DELETE /items/{id}` 模擬整組移除（中途失敗會拆散群組，違反第 3 點的原子性保證）。

## 最低成本與商業影響

- 只用文件或前端按鈕文字調整無法解決問題：後端目前完全沒有任何合法路徑可以移除一個卡住的組裝群組，必須新增一個真正的寫入 Endpoint。
- 最低充分方案：一個新 Service 方法＋一個新 Controller Action＋一個新 Request DTO＋`BuildItemsAsync` 的一處條件分支，不需要新表、不需要 Migration、不需要新的錯誤碼族。
- 受影響者：任何把組裝清單加入購物車、之後才發現零件下架／缺貨的會員或訪客——現況他們的購物車會永久卡在無法結帳、也無法清空的狀態。
- 成功指標：群組 SKU 變成不可用時，Revalidate 回報 `remove-group`（不是會被拒絕的 `reduce-quantity`／`remove`）；呼叫新 Endpoint 後同一 `AssemblyGroupKey` 的所有列一次性消失；購物車恢復可重新驗證與結帳。

## 風險與回復條件

- 本決策不修改任何既有 Schema、不新增資料表、不變更「組裝群組不可拆分」的既有商業規則——只是新增一個刪除整組的合法路徑，方向與既有規則一致，不是推翻。
- 若之後 alex／組長對這個 Endpoint 的實際簽章有不同意見（例如想要用 Cart 層級的 batch action 而非 REST 資源路徑），本 AUTO-DEC 可被新的 AUTO-DEC／DEC-BATCH 取代並保留歷史，不靜默覆寫；`RemoveAssemblyGroupAsync`／新 Endpoint 都是新增程式碼，回復方式是直接還原這幾個檔案的變更，不影響任何既有資料或既有 Endpoint 行為。
