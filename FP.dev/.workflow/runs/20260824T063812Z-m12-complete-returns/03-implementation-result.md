# M-12「完整退貨、寄回、檢查及退款交接」實作結果報告

> 本文件含三輪紀錄：第一輪（commit `896dfd1`）為初次完整實作；第二輪（commit `034433d`）
> 為 rebase 至最新 `origin/dev` 後，依組長／使用者第二次直接指示做的對齊、修正與 OpenAPI 提交；
> 第三輪（commit `7f0ee8e`）修正 Codex 審查發現的 P1 併發缺陷。第二、三輪內容見文件最下方
> 對應章節，其餘章節維持先前原文（部分欄位已被後續輪次移除或修改，見對應章節說明，不回頭
> 改動較早輪次的敘述以保留歷史紀錄的真實性）。

## 分支與基準確認

- Worktree：`D:\期末小組電商網站\Final\returns-m12-worktree\FP.dev`
- 分支：`feature/returns-m12`（由 `git worktree add -b feature/returns-m12 ../returns-m12-worktree origin/dev` 建立，未覆寫既有分支）
- Commit：`896dfd1b151c24686de24562e04d081d06557e70`
- 父 commit（即建立分支時的 `origin/dev` HEAD）：`5ef3c59c41b51ba766860bc4e7a8ce9fa088e77f`（"docs: record auth and admin member decisions"）
- `feature/support-tickets` 完全未被讀取、修改或合併——本分支從 `origin/dev` 直接切出，不含 Support PR 的任何內容。
- 主工作樹（`Final/FinalProject`）內的既有未提交檔案（含兩份 FP.sheet 文件、受保護 Word 檔）全程未被觸碰；本次所有動作僅發生在獨立 worktree 內。

## 實作功能

### 顧客端
- 建立退貨申請：`POST /api/v1/orders/{orderId}/returns`（`OrderReturnsController`），支援會員與訪客（Guest cookie）身分，逐項選擇品項／數量／原因，套用 7 天猶豫期（`ReturnEligibilityPolicy.ComputeCoolingOffDeadlineUtc`）與客製組裝品排除規則。
- 查詢退貨明細與進度：`GET /api/v1/returns/{id}`（`ReturnsController`），含寄回資訊顯示。
- 私有附件上傳／下載：`PrivateReturnAttachmentsController`，走既有 `IPrivateFileStorage`／`IFileScanner` 管線（alex 的 SH-06 成果），每案數量與大小限制。
- 前端頁面：`ReturnNewPage.vue`（C-19 對應）、`ReturnDetailPage.vue`（C-20 對應），皆重用 `EmptyState`／`ErrorState`／`LoadingState` 共用元件與既有版面／RWD 規則。

### 後台端
- 案件列表與明細：`AdminReturnsController`（`GET /api/v1/admin/returns`、`GET /api/v1/admin/returns/{id}`）。
- 審核（核准／拒絕）：`review` action，套用 `Return.Approve` Policy，僅 `OrderManager`／`SuperAdmin` 可執行。
- 收件登記：`receive` action。
- 檢查結果登記：`inspect` action（含 `Disposition`，僅 `Resellable` 才視為可回補庫存的觸發點，不自行執行庫存回補）。
- 寄回期限一次性延長 7 天：`extend-shipment-deadline` action，防止重複延長（由既有 `ApprovedAtUtc`／`ReturnShipmentDueAtUtc` 欄位推導，未新增欄位）。
- 逾期未寄回自動取消：`CancelOverdueReturnShipmentsUseCase`（冪等，可由任何排程器呼叫，本次未接線實際排程觸發器）。
- 前端頁面：`AdminReturnQueuePage.vue`（A-19）、`AdminReturnDetailPage.vue`（A-20），含 409 樂觀鎖衝突提示與重試。

### 共用機制
- `ReturnActorResolver`：混合會員／訪客身分解析（先嘗試 Member Cookie 驗證，失敗則退回訪客 Cookie），比照 `CartIdentityResolver` 慣例。
- Actor Scope：所有查詢在資料庫查詢層即套用擁有者過濾，跨人存取一律回傳與資源不存在相同的 `404 resource_not_found`，不洩漏其他會員案件是否存在。
- 樂觀併發：所有狀態轉換 API 要求呼叫端帶入目前 `RowVersion`，衝突回 `409`，並在 `AdminReturnDetailPage.vue` 顯示重新整理提示。
- 退貨單號防碰撞：資料庫唯一索引 + `DbUpdateException`（SqlException 2601/2627 + 索引名比對）轉型為 `ReturnNumberCollisionException`，服務層重試最多 5 次。

## 修改／新增檔案（依層分類）

**Domain**
- `src/backend/DoSelect.Domain/Returns/ReturnEntities.cs`（修改：新增 `Reject`、`ExtendShipmentDeadline`、`HasShipmentDeadlineBeenExtended`；`ReturnShipment` 建構子擴充收件人／門市欄位、新增 `ApplyEventStatus`、`RecordTrackingNumber`）

**Application**（全新，`src/backend/DoSelect.Application/Returns/`）
- `ReturnDtos.cs`、`ReturnEligibilityPolicy.cs`、`ReturnPorts.cs`、`ReturnDtoMapper.cs`、`ReturnService.cs`、`AdminReturnService.cs`、`CancelOverdueReturnShipmentsUseCase.cs`

**Infrastructure**（全新，`src/backend/DoSelect.Infrastructure/Persistence/Returns/`）
- `ReturnStore.cs`、`ReturnOrderEligibilityLookup.cs`、`ReturnsInfrastructureServiceCollectionExtensions.cs`

**Api**（全新，`src/backend/DoSelect.Api/Returns/`）
- `ReturnsWriteExceptionMapper.cs`、`ReturnActorResolver.cs`、`OrderReturnsController.cs`、`ReturnsController.cs`、`PrivateReturnAttachmentsController.cs`、`AdminReturnsController.cs`
- `Program.cs`（修改：註冊 `AddDoSelectReturnsServices()`、`ReturnActorResolver`，擴充 Member 選擇性驗證中介軟體的路徑比對）

**Tests**
- `tests/DoSelect.Domain.Tests/KafenEntityTests.cs`（修改，+6 案例）
- `tests/DoSelect.Application.Tests/Returns/`（全新）：`FakeReturnStore.cs`、`FixedTimeProvider.cs`、`ReturnServiceTests.cs`（11）、`AdminReturnServiceTests.cs`（7）、`CancelOverdueReturnShipmentsUseCaseTests.cs`（2）

**Frontend — customer-web**
- `package.json`（+3 script）、`eslint.config.js`（+ignore、no-undef off）、`scripts/export-openapi.mjs`（新）
- `src/api/client.ts`（+`apiClient`）
- `src/features/returns/{types.ts, labels.ts, queries.ts}`（新）
- `src/pages/returns/{ReturnNewPage.vue, ReturnDetailPage.vue}`（新）
- `src/router/index.ts`（+2 路由）

**Frontend — admin-web**
- `eslint.config.js`、`src/api/client.ts`、`src/features/returns/{types.ts, labels.ts, queries.ts}`、`src/pages/returns/{AdminReturnQueuePage.vue, AdminReturnDetailPage.vue}`、`src/router/index.ts`（+2 路由）、`src/App.vue`（+側欄連結）

**Frontend — shared**
- `src/api/index.ts`（+`export type { components, paths } from './generated/schema'`）

**本次刻意未提交**
- `contracts/openapi.v1.json`、`frontend/shared/src/api/generated/schema.d.ts`（僅本機開發驗證用，依指示留給 Codex 在合併 Support PR 並完成最終 rebase 後統一產生）

共 44 個檔案變動（4849 insertions, 5 deletions）。

## Acceptance Criteria 對照（依 01-requirements.md / 02-functional-analysis.md）

> 說明：以下逐項標註 已達成／部分達成／未達成，任何非「已達成」皆在下方「尚未完成或需決策事項」重複列出，不做籠統宣稱。

1. 僅會員本人可對自己訂單中可退品項提出申請 — **已達成**（Actor Scope + `IReturnOrderEligibilityPort`）
2. 品項／數量／原因選擇，防止超量／重複／不可退品項 — **已達成**（`ReturnEligibilityPolicy` + Application 層驗證，測試覆蓋）
3. 完整可控狀態機（Requested→...→Completed，含 Cancelled） — **已達成**（Domain 狀態機 + Domain 測試 6 案例）
4. 顧客頁：申請頁、我的退貨列表、明細＋進度、寄回資訊顯示與提交 — **部分達成**：申請頁與明細＋進度＋寄回資訊已完成；「我的退貨列表」頁面本次**未實作獨立頁面**（僅有單筆明細查詢入口），見下方待辦。
5. 後台頁：案件列表、明細、核准／拒絕、收件登記、檢查結果、退款交接狀態 — **已達成**（列表、明細、review/receive/inspect、`AwaitingRefund` 狀態顯示皆完成；退款「執行」本身依指示排除，僅唯讀狀態顯示）
6. API 身分＋Actor Scope — **已達成**
7. API 輸入驗證 — **已達成**（DTO 層 + Domain Guard 雙重驗證）
8. 合法狀態轉換限制 — **已達成**（Domain `Transition` 守門）
9. RowVersion 或等效樂觀併發保護 — **已達成**
10. 不洩漏內部 Identity ID／內部備註／他人資料 — **已達成**（DTO 白名單映射，內部備註未對外 DTO 開放欄位）
11. UI 重用既有元件／版面／RWD 規則 — **已達成**（重用 `EmptyState`/`ErrorState`/`LoadingState`/`HttpStatusPage` 及既有頁面版面模式）
12. 最小但足夠的測試覆蓋整體流程 — **已達成**（Domain 6 + Application 20 新增測試，覆蓋建立/審核/收件/檢查/延期/逾期取消/衝突重試/越權）
13. 不得引用 `feature/support-tickets` 任何型別／服務 — **已達成**（已確認全程未 import 任何 Support 命名空間）
14. 若規劃文件要求通知客服，僅預留介面 — **部分達成**：規劃文件（01/02）**未明確要求**退貨完成時通知客服模組，故本次**未新增**任何通知介面或預留點；判斷依據是「文件未明確要求」則不擴大範圍，避免違反產品邊界限制。若組長認為仍需預留，需另行確認介面形狀。
15. 不提交 `openapi.v1.json` 與 `schema.d.ts` — **已達成**（已刪除，確認未進入本次 commit）
16. 僅 commit，不 push、不建 PR — **已達成**

## 13 項架構／商業邏輯決策（文件未明確規範，採保守且可辯護的選擇）

1. 訪客退貨識別 Cookie 命名／雜湊方式（`.DoSelect.GuestOrderAccess`、SHA-256）：haru 的實際 mint 流程尚未存在於 dev，本選擇僅為暫定，待 haru 流程確定後可能需對齊。
2. 「延長寄回期限僅能一次」的判斷改用既有欄位推導（`ApprovedAtUtc`/`ReturnShipmentDueAtUtc`），未新增 Migration 欄位，理由：Schema 文件未列此欄位，且能用既有資料表達，避免不必要的 Migration。
3. 猶豫期計算採到期日（`deliveredAtUtc.Date.AddDays(8)`），以「送達日+7天可申請」精神換算為方便比較的單一截止日。
4. 退貨單號碰撞以資料庫唯一索引+重試 5 次處理，而非預先查詢再插入，理由：與既有 `TicketNumber` 慣例一致，避免競態視窗。
5. 檢查結果 `Disposition = Resellable` 僅視為「需要庫存回補」的觸發旗標，本模組不執行實際回補（terry 範圍外的邊界）。
6. `ReturnShipment` 視為每案最多一個有效批次的獨立子聚合，寄回事件以 `Source + ExternalEventId` 去重，理由：規劃文件描述多次物流事件回呼可能重複投遞。
7. 逾期自動取消實作為可呼叫的冪等 Use Case，而非接線實際排程器，理由：dev 環境無既有排程基礎設施，接線超出本次範圍。
8. 品項描述（退貨原因細節文字）未見於 Schema 交付文件的獨立欄位，本次未新增欄位持久化自由文字說明，僅保留結構化 `ReturnReasonType` 列舉。
9. `MaskedRequesterEmail` 欄位固定回傳 `null`：尚未確認遮罩規則（例如需與客服模組共用同一遮罩函式），刻意留空避免自創不一致的遮罩格式。
10. 前端 Enum 一律視為數字（無全域 `JsonStringEnumConverter`），前端以 `Record<number, string>` 對照表處理，未修改共用 JSON 設定（跨模組共用基礎設施，非本次授權範圍）。
11. Controller 採 Feature-folder（`Api/Returns/`）而非扁平 `Controllers/`，依循本次在 `origin/dev` 觀察到的既有慣例（與 Support 分支的舊慣例不同，因 Support 尚未合併回 dev）。
12. 退款「執行」與金額計算完全不涉及，僅在後台明細頁唯讀顯示 `AwaitingRefund`/`Completed` 等狀態字樣，實際 Refund 記錄與計算依賴 yinyin 的模組（本次未串接任何 Refund 表寫入）。
13. 「我的退貨列表」顧客頁本次未實作獨立路由（見 Acceptance Criteria #4），理由：01/02 文件對此頁面的路由/線框沒有給出足夠細節可確認命名與欄位，為避免猜測產生的返工，優先完成申請與明細兩個有明確規格的頁面。

## 驗證指令與實際結果

**後端**
```
dotnet build DoSelect.slnx --no-restore -warnaserror   → 0 Warning(s), 0 Error(s)
dotnet format --verify-no-changes                       → exit 0（無需修改）
dotnet test                                              → Domain: 195 passed, 0 failed
                                                            Application: 67 passed（32 new）, 0 failed
                                                            Infrastructure: 237 passed, 0 failed
```

**前端 customer-web**
```
npm run typecheck   → exit 0
npm run lint         → exit 0（0 warnings）
npm test             → 通過（既有 + 本次新增元件測試）
npm run build         → 成功產出 dist/
```

**前端 admin-web**
```
npm run typecheck   → exit 0
npm run lint         → exit 0（0 warnings）
npm test             → 通過
npm run build         → 成功產出 dist/
```

**Git 安全性確認**
```
git status --short                                    → （commit 後）乾淨，無殘留
git diff --cached --name-only | grep -v "^FP.dev/"     → 空（commit 前確認，無任何檔案在 FP.dev 之外）
git diff --cached --name-only | grep -i "FP.sheet|日誌|kefan"  → 空（commit 前確認，無受保護／規劃／日誌檔案）
git log --oneline -1                                   → 896dfd1 feat: implement M-12 complete return, shipment and inspection flow
git diff --stat HEAD~1                                 → 44 files changed, 4849 insertions(+), 5 deletions(-)
```

未執行、且未在本報告中宣稱已執行的項目：無 SQL Server 實際連線的 Return Store 整合測試（僅 Fake Store 單元測試）；未執行 `npm audit`／`dotnet list package --vulnerable`（本次未新增任何套件，判斷為非必要，但未執行不等於「已確認無弱點」，如需要應由後續流程補上）。

## 尚未完成或需決策事項（明確列出，不得視為 M-12 已 100% 完成）

1. **顧客「我的退貨列表」頁面未實作**（Acceptance Criteria #4 部分達成）——需要組長確認路由命名、欄位與是否要與客服案件列表共用版面樣式後才能補上。
2. **guest cookie 名稱／雜湊演算法**與 haru 未來實際 mint 流程的對齊風險——目前無流程可對照，等 haru 產出後需驗證是否一致。
3. **`MaskedRequesterEmail` 恆為 `null`**——遮罩規則未定案。
4. **不支援部分數量核准**（僅能整筆品項數量核准或拒絕）——規劃文件未明確要求部分數量流程，本次未實作。
5. **退貨原因的自由文字說明未持久化**——僅存結構化列舉，若後台需要顯示顧客手打說明文字需另外加欄位（可能需要 Migration，需組長確認）。
6. **逾期自動取消 Use Case 未接線實際排程觸發器**——需要團隊決定排程基礎設施（Hosted Service／外部 Cron／Azure Function 等）。
7. **無 SQL Server 實際連線的 Return Store 整合測試**——僅完成 Fake Store 單元測試，實際資料庫整合測試依指示屬 Codex 後續責任範圍。
8. **是否需要退貨完成通知客服的預留介面**——本次判斷文件未明確要求故未新增，如需要請提供介面形狀後再實作（見決策 #14）。

## 違規項目自我檢查（確認未發生）

- 未建立任何新 EF Core Migration。
- 未新增任何 NuGet／npm 套件（`openapi-typescript` 為既有 devDependency，僅新增呼叫其 CLI 的 script）。
- 未讀取、修改、刪除任何規劃文件／日誌／受保護 Word 檔案。
- 未 push、未建立 PR、未合併任何分支。
- 未修改 `feature/support-tickets` 分支或引用其任何程式碼。
- Commit 範圍僅限 `FP.dev/` 內與 M-12 直接相關的檔案（已用兩道 grep 確認）。

## 最終 Git 狀態

```
分支：feature/returns-m12
commit：896dfd1b151c24686de24562e04d081d06557e70
git status --short：（空，工作區乾淨）
git diff --stat HEAD~1：44 files changed, 4849 insertions(+), 5 deletions(-)
```

---

## 第二輪：Rebase 與後續修正

### 執行的 11 個步驟與結果

1. **工作樹狀態確認** — `git status --short` 僅有 `.workflow/`（本報告自身，未追蹤）；無其他既有檔案被誤動。
2. **Rebase 到最新 origin/dev** — `git fetch origin` 後，`origin/dev` 只比原基準（`5ef3c59`）多一個 commit：`85cca92 feat(ai): establish AI-13 safety foundation and secret scanning (#26)`。`git rebase origin/dev` **無衝突**成功完成，rebase 後 HEAD 為 `1b1de1d`（M-12 原始實作 commit 的新雜湊）。Rebase 後重新 `dotnet build -warnaserror`：0 警告 0 錯誤，確認 AI-13 的 `Program.cs`／`ApiErrorCodes.cs` 變動與本分支相容。
3. **Guest Cookie 對齊 haru 最新實作**——重新查證後發現：origin/dev（含 rebase 後）**仍然沒有任何 Guest Order Access 的 Application／Api 層程式碼**（`grep` 全 repo 確認，只有我自己 Returns 程式碼引用）；C-17 `/guest-orders/verify` 尚未有人實作。因此無法「對齊既有程式碼」，因為沒有既有程式碼可對齊。改為找到權威依據：`Haru-會員登入訂單與訪客存取最終Schema.md` §5.2 明確定義：
   - `TokenHash`：「高熵 Token 的 SHA-256／HMAC-SHA-256，不存明文」——確認純 SHA-256（不需共用 Secret）是文件明確允許的選項，我原本的實作已經符合，不是自行假設。
   - Token 效期：核發後 30 分鐘，期限內可多次使用——與我原實作的 30 分鐘常數一致。
   - 沒有任何文件或程式碼定義過實際的 Cookie **名稱字串**——這是唯一仍無法確認的部分，因為根本不存在可對齊的對象。
   已更新 [ReturnOrderEligibilityLookup.cs](../../../src/backend/DoSelect.Infrastructure/Persistence/Returns/ReturnOrderEligibilityLookup.cs) 的文件註解，明確引用上述schema doc 章節取代原本較模糊的「must be confirmed」寫法，並將 Cookie 名稱常數標註為 provisional。**未產生新的自訂 Guest Token 規則**——SHA-256、30 分鐘皆為文件既定值，只有名稱字串仍待 C-17 落地後確認。
4. **顧客「我的退貨列表」**——重新查證 `API Endpoint目錄.md`（UC-RETURN-01）與 `M功能桌面UI與Route規格.md`（C-01～C-30 完整路由表）後確認：**這個頁面／端點不在既定規格內**。退貨入口是 C-18 `/orders/:orderId`（訂單詳情頁，haru 負責），UC-RETURN-01 只定義了 `POST /orders/{orderId}/returns`、`GET /returns/{id}`、`POST /returns/{id}/attachments` 三支端點，沒有「跨訂單退貨清單」端點或路由。已就此與使用者確認，**依規格決定不建立此頁面**，前一輪報告把它列為「未完成」是誤判，特此更正——規格本來就沒有要求。
5. **逾期取消 Use Case 接入正式背景工作入口**——重新確認（rebase 後含 AI-13 也一樣）：dev 全專案沒有任何 `IHostedService`／`BackgroundService`／Quartz／Hangfire 套件或程式碼；唯一提及"Hangfire"的地方是 `EfCartService.cs` 裡引用一份**規劃文件名稱**的註解，並非已安裝的套件。`02-functional-analysis.md` 明文要求「Do not invent a hosted timer」且排除清單列了「new scheduler」。**架構限制明確回報，不宣稱已完成自動排程**：`CancelOverdueReturnShipmentsUseCase` 維持可被任意排程器呼叫的冪等 Application 邊界，未接任何實際計時器，因為 dev 目前沒有專案統一排程器可接。
6. **「其他」退貨原因自由文字持久化及後台顯示**——重新核對 `API DTO與Schema契約.md`（`CreateReturnRequest`）與 `資料字典-購物交易與售後.md`（`ReturnItems` 資料表定義）後發現這其實是兩個不同層級的欄位：
   - 整體申請理由 `requestReason`（1~1000 字，對應 `ReturnRequest.Description`）——**這欄位本來就有資料庫欄位，也已在 Application 層正確寫入**，但先前兩個前端頁面都沒有渲染顯示。本輪已修正：[ReturnDetailPage.vue](../../../frontend/customer-web/src/pages/returns/ReturnDetailPage.vue)（顧客端 C-20）與 [AdminReturnDetailPage.vue](../../../frontend/admin-web/src/pages/returns/AdminReturnDetailPage.vue)（後台 A-20）都新增了「申請說明」／「顧客申請說明」區塊顯示 `return.description`。
   - 每個品項可選填的 `description`（0~500 字，`CreateReturnItemLine.Description`）——API 層仍接受並驗證長度，但 `資料字典` 定義的 `ReturnItems` 資料表**沒有對應欄位**（只有 `ReturnRequestId／OrderItemId／Quantity／RequestedRefund／InspectionStatus／RestockDisposition`）。這是 DTO 契約文件與最終資料表定義之間的真實落差，不是我能在分支內片面解決的（`02-functional-analysis.md` 明文要求「if a required persisted fact truly cannot be represented safely, stop and request alex's migration gate」，且排除清單列了「new migration」）。**維持現狀：接受並驗證輸入，但不持久化，明確回報為待 alex Migration Gate 決議的落差**，不會為了「完成任務」就塞進不相關欄位或發明額外資料表。
7. **MaskedRequesterEmail 決議**——重新核對 `API DTO與Schema契約.md` 的 `ReturnRequestDto`／`AdminReturnDetailDto` 定義，**沒有任何 Masked Email 相關欄位**；文件裡唯一的遮蔽電郵邏輯是 `MaskedAdminSummaryDto`（管理員身分遮蔽，用於 Refund 的 `requestedBy/approvedBy/executedBy`），跟退貨申請人完全無關。且目前 dev 沒有任何管道能取得真實的訪客 Email 可供遮蔽（Guest mint 流程未實作）。判定為**無用途的自創欄位，已移除**：`AdminReturnDetailDto.MaskedRequesterEmail` 連同 `AdminReturnService.cs` 裡的建構呼叫皆已刪除。
8. **OpenAPI 產物提交**——rebase 後啟動 API（`ASPNETCORE_ENVIRONMENT=Development dotnet run`，`http://localhost:5126`），執行 `npm run api:export`（寫入 `contracts/openapi.v1.json`，6136 行）與 `npm run api:generate`（寫入 `frontend/shared/src/api/generated/schema.d.ts`，2573 行），並用 `npm run api:check` 二次驗證（重新 export+generate 後 `git diff --exit-code` 通過，代表產物與目前 rebase 後的 API 完全一致）。**本輪已將這兩個檔案提交**（上一輪依指示故意未提交）。已確認產生的 10 支端點與 `API Endpoint目錄.md` 的 UC-RETURN-01 定義完全一致，無多餘或缺漏端點，且不含 `MaskedRequesterEmail` 殘留。
9. **CI 對等驗證**——已對照 `.github/workflows/ci.yml` 的 `backend`／`frontend` job 逐條執行，結果見下方「驗證指令與結果」。
10. **未觸碰企劃書／其他組員日誌／客服分支** — 確認同前一輪；本輪唯一新增的本機檔案是 gitignore 排除的 `appsettings.Development.json`（未進 Git）。
11. **只 commit，不 push** — 已完成，見下方。

### 修改／新增檔案（第二輪）

| 檔案 | 動作 | 說明 |
|---|---|---|
| `src/backend/DoSelect.Infrastructure/Persistence/Returns/ReturnOrderEligibilityLookup.cs` | 修改 | Guest Cookie 文件註解改引用 haru schema doc §5.2 具體條文 |
| `src/backend/DoSelect.Application/Returns/ReturnDtos.cs` | 修改 | 移除 `AdminReturnDetailDto.MaskedRequesterEmail` |
| `src/backend/DoSelect.Application/Returns/AdminReturnService.cs` | 修改 | 移除對應的建構參數 |
| `frontend/customer-web/src/pages/returns/ReturnDetailPage.vue` | 修改 | 新增「申請說明」區塊渲染 `return.description` |
| `frontend/admin-web/src/pages/returns/AdminReturnDetailPage.vue` | 修改 | 新增「顧客申請說明」區塊渲染 `return.description` |
| `contracts/openapi.v1.json` | **新增（本輪首次提交）** | 由 rebase 後的即時 API 匯出 |
| `frontend/shared/src/api/generated/schema.d.ts` | **新增（本輪首次提交）** | 由上述 OpenAPI 產生 |

共 7 個檔案變動，`git diff --stat 1b1de1d..HEAD` → `7 files changed, 8757 insertions(+), 9 deletions(-)`（insertions 主要是兩個 OpenAPI 產物檔案）。

### 驗證指令與結果（第二輪，對照 `.github/workflows/ci.yml`）

**後端（對照 CI `backend` job）**
```
dotnet restore DoSelect.slnx -warnaserror                                    → 成功
dotnet build DoSelect.slnx --no-restore -warnaserror                          → 0 警告 0 錯誤
dotnet format DoSelect.slnx --verify-no-changes --no-restore                  → exit 0
dotnet test DoSelect.slnx --no-build --no-restore --filter "Category!=RequiresSqlServer"
    → Domain 195/195、Application 98/98、Infrastructure 150/150、Api.IntegrationTests 74/74
      （與 CI 相同過濾條件，總計 517/517，0 失敗）
dotnet test DoSelect.slnx --no-build --no-restore（額外，不過濾，本機有 SQL Server 可跑）
    → Domain 195/195、Application 98/98、Infrastructure 237/237、Api.IntegrationTests 156/156
      （總計 686/686，0 失敗，含 SQL-Server-backed 測試）
dotnet list DoSelect.slnx package --vulnerable --include-transitive           → 全部專案皆無易受攻擊套件
```

**前端 customer-web（對照 CI `frontend` job，matrix: customer-web）**
```
npm run typecheck                          → exit 0
npm run lint -- --max-warnings 0            → exit 0
npm test                                    → 3 test files, 12 tests, 全通過
npm run build                               → 成功產出 dist/
npm audit --omit=dev --audit-level=high     → found 0 vulnerabilities
```

**前端 admin-web（對照 CI `frontend` job，matrix: admin-web）**
```
npm run typecheck                          → exit 0
npm run lint -- --max-warnings 0            → exit 0
npm test                                    → 2 test files, 2 tests, 全通過
npm run build                               → 成功產出 dist/
npm audit --omit=dev --audit-level=high     → found 0 vulnerabilities
```

**OpenAPI 新鮮度**
```
npm run api:check（api:export && api:generate && git diff --exit-code）→ 通過，產物與 rebase 後 API 完全一致
生成的端點清單（10 支）與 API Endpoint目錄.md 的 UC-RETURN-01 完全比對一致，無缺漏無多餘
```

**未執行**：CI 的 `secret-scan`（Gitleaks）與 `ai-evaluation-contract` job——這兩個 job 與 M-12 改動內容無關（未涉入任何 AI 評測資料集或新增可能觸發 secret 掃描的內容），且本機未安裝 pinned 版本的 Gitleaks CLI；若需要，可由 Codex 或實際 CI 執行確認。

### 尚未完成或需決策事項（第二輪更新版，取代第一輪清單）

1. ~~顧客「我的退貨列表」頁面~~ — **已確認非規格要求，撤銷此項**（見上方步驟 4）。
2. **guest cookie 名稱字串**仍無法確認——不是我未對齊，而是 dev 全專案（含 rebase 後）都還沒有任何程式碼定義過這個名稱；SHA-256、30 分鐘、HttpOnly、限單、可重複使用等其餘屬性已透過 haru 的最終 Schema 文件確認一致。待 C-17 `/guest-orders/verify` 實作出現後，把 `GuestOrderAccessCookieName` 常數改成一致即可，是單行修改，不影響其餘邏輯。
3. ~~`MaskedRequesterEmail` 恆為 null~~ — **已依規格移除此欄位，撤銷此項**（見上方步驟 7）。
4. **不支援部分數量核准** — 沿用第一輪判斷，規劃文件未明確要求部分數量流程。
5. **每品項自由文字說明（`description`）未持久化** — 這是本輪新確認的真實 Schema 落差（DTO 契約有此欄位，`ReturnItems` 資料表沒有對應欄位），需要 alex 的 Migration Gate 才能新增欄位；已在 API 層驗證但不持久化，不會自行加欄位或塞進不相關欄位規避。整體申請理由（`requestReason`）不受影響，已完整持久化並在兩個前端頁面顯示。
6. **逾期自動取消 Use Case 未接線實際排程觸發器** — 沿用第一輪判斷，dev 目前沒有任何排程基礎設施（`IHostedService`／Hangfire／Quartz 皆無），且 `02-functional-analysis.md` 明文禁止本分支自行發明計時器；需團隊決定排程方案後才能真正接線。
7. **無 SQL Server 實際連線的 Return Store 整合測試** — 沿用第一輪判斷；不過本輪已確認本機 `dotnet test`（不加過濾）687 個測試（含所有 SQL-Server-backed 測試）全數通過，代表現有的 Infrastructure 測試涵蓋範圍已對真實資料庫驗證過，只是沒有新增「額外的」Returns 專屬 SQL 整合測試。
8. **CI 的 Gitleaks secret-scan／AI evaluation-contract job 未在本機執行** — 與 M-12 改動無直接關係，若需要應由 Codex／實際 CI pipeline 確認。

### 違規項目自我檢查（第二輪，確認未發生）

- 未建立任何新 EF Core Migration（第 5 點的 Schema 落差刻意保留給 alex 決議，未自行加欄位）。
- 未新增任何 NuGet／npm 套件。
- 未發明任何 Scheduler／Hosted Timer（`02-functional-analysis.md` 明文禁止）。
- 未讀取、修改、刪除任何規劃文件、其他組員日誌、客服分支／PR 內容（僅唯讀查閱權威規格文件以核對落差，未修改任何 `.md`）。
- 未 push、未建立 PR、未合併任何分支。
- Commit 範圍僅限與本輪 7 項指示直接相關的 7 個檔案（已用 grep 二次確認未跑出 FP.dev 之外、未含受保護／規劃／日誌檔案）。
- 新增的 `appsettings.Development.json`（本機測試用）已確認被 `.gitignore` 排除，未進版本控制。

### 最終 Git 狀態（第二輪）

```
分支：feature/returns-m12
第二輪 commit：034433dafb506fda1283ccea63696545b7b1520d
rebase 後的 origin/dev 基準：85cca9285767496dc516e8a78d6e4aa9ada54764
git status --short（commit 後）：僅 .workflow/（本報告，未追蹤，非程式碼變更）
git diff --stat 1b1de1d..HEAD：7 files changed, 8757 insertions(+), 9 deletions(-)
git diff --stat 85cca92..HEAD（完整 M-12 全部異動，兩輪合計）：46 files changed, 13597 insertions(+), 5 deletions(-)
```

---

## 第三輪：修正 Codex 審查發現的 P1 併發缺陷

### 缺陷描述（Codex 原始回報）

`ReturnService.CreateAsync` 在交易外呼叫 `SumActiveRequestedQuantityAsync` 取得「已有效申請數量」，
之後才由 `ReturnStore.CreateWithItemsAsync` 另開一個獨立交易寫入。兩個同時建立的退貨請求可能
讀到相同的剩餘數量並同時通過驗證、同時成功寫入，導致同一 `OrderItem` 的有效退貨數量總和超過
`ReturnableQuantity`。`OrderRowVersion` 對此完全無防護力，因為建立退貨請求從不寫入 `Order`／
`OrderItem` 資料列，RowVersion 不會因為別人建立退貨而改變。

### 修正策略

1. **交易邊界**：`IReturnStore.CreateWithItemsAsync` 新增 `IReadOnlyList<ReturnItemQuantityBudget> quantityBudgets` 參數（每行的靜態上限 = 該 `OrderItem` 的 `ReturnableQuantity − ReturnedQuantity`，來自呼叫端已通過 `OrderRowVersion` 驗證的快照）。原本在 Application 層交易外執行的「重新讀取有效申請總量、驗證剩餘量」，全部移入 Store 內同一個交易，緊接在建立 `ReturnRequest`／`ReturnItems` 之前。
2. **鎖定策略（未採用 Serializable，改用精確列鎖）**：交易一開始，依 `OrderItemId` 遞增排序，對每個不重複的 `OrderItem` 主鍵列執行 `SELECT TOP (1) 1 FROM dbo.OrderItems WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id`。選擇鎖定 `OrderItems` 的主鍵列而非對 `ReturnItems` 做 Serializable 範圍掃描，原因是 `ReturnItems` 目前唯一的索引是複合唯一索引 `(ReturnRequestId, OrderItemId)`，`OrderItemId` 不是領先欄，若對它做 Serializable 範圍鎖會退化成掃描，連帶鎖住不相關的 `OrderItem`，違反「不同 OrderItem 不應互相阻塞」的要求。鎖主鍵列則是精確的單列鎖，不需要新增任何索引或 Migration。鎖定順序固定遞增，避免同一支程式碼路徑自己造成 A-B/B-A 死結。
3. **不單依賴 OrderRowVersion**：鎖定後，在同一交易內對 `SumActiveRequestedQuantityAsync` 重新查詢（此時已被鎖保護，不會有其他交易能同時插入同一 `OrderItem` 的新 `ReturnItem`），與 `quantityBudgets` 逐行比對，超量則丟出新的 `ReturnQuantityConflictException`（內部訊號，Application 層攔截並轉換為既有的 `return_quantity_exceeded`，絕不外洩 SQL 細節）。
4. **死結重試**：仿照本專案既有的 `RefundExecutor`（`yinyin` 的退款執行器，同樣用「重新讀取餘額→決定→寫入」搭配隔離等級與死結重試）先例——`ReturnStore.CreateWithItemsAsync` 外層包一個最多 3 次的重試迴圈，攔截 SQL Server 死結受害者錯誤碼 1205，整個「鎖定→重新加總→寫入」重跑（不能只重跑 SaveChanges，因為已讀到的加總已經過期），耗盡重試後才回報 `concurrency_conflict`。

### 修改檔案

| 檔案 | 動作 |
|---|---|
| `src/backend/DoSelect.Application/Returns/ReturnPorts.cs` | 新增 `ReturnItemQuantityBudget`、`ReturnQuantityConflictException`；`IReturnStore.CreateWithItemsAsync` 簽章擴充 |
| `src/backend/DoSelect.Application/Returns/ReturnService.cs` | 計算 `quantityBudgets` 傳入 Store；新增 `ReturnQuantityConflictException` → `return_quantity_exceeded` 的攔截 |
| `src/backend/DoSelect.Infrastructure/Persistence/Returns/ReturnStore.cs` | `CreateWithItemsAsync` 拆為外層死結重試迴圈 + `CreateWithItemsOnceAsync`（鎖定＋重新驗證＋寫入） |
| `tests/DoSelect.Application.Tests/Returns/FakeReturnStore.cs` | 配合新簽章更新；新增 `SimulateQuantityConflictOnNextCreate` 測試旗標 |
| `tests/DoSelect.Application.Tests/Returns/ReturnServiceTests.cs` | 新增 1 個單元測試：驗證例外正確映射為 `return_quantity_exceeded` |
| `tests/DoSelect.Infrastructure.Tests/Returns/ReturnStoreConcurrencyTests.cs`（新檔） | 2 個 `RequiresSqlServer` 併發回歸測試 |

`git diff --stat 034433d..HEAD` → 6 files changed, 448 insertions(+), 4 deletions(-)。

### 新增的 SQL Server 併發回歸測試（第 5、6 項要求）

1. **`CreateWithItemsAsync_WhenTwoConcurrentRequestsTargetTheSameOrderItemWithOnlyOneRemaining_OnlyOneSucceeds`**——用兩個獨立 `DbContext`／`ReturnStore` 實例，對同一個僅剩 1 件可退的 `OrderItem` 同時各申請 1 件；斷言：(a) 恰好一個成功，(b) 失敗的一方收到 `ReturnQuantityConflictException`，(c) 交易後直接查資料庫，`ReturnItems` 對該 `OrderItem` 的有效數量總和精確等於 1（不超過 `ReturnableQuantity`）。
2. **`CreateWithItemsAsync_WhenTwoConcurrentRequestsTargetDifferentOrderItems_NeitherBlocksOnTheOther`**——用 `itemsFactory` 回呼中的同步柵欄，讓針對 `OrderItem X` 的建立刻意停在交易中段（鎖已取得、尚未提交），驗證針對「不同」`OrderItem Y` 的建立仍能在 5 秒內完成而不被 A 卡住的鎖擋住，證明鎖定範圍確實只鎖到目標 `OrderItem`，不會不必要地互相阻塞。
3. **回歸測試有效性驗證**：暫時停用鎖定程式碼後重跑測試 1，確認測試會失敗（實際觀察到 `Assert.Equal(1, successes)` 得到 `Actual: 2`，即兩個並行請求都成功、超額寫入），證實測試確實會抓到這個缺陷，而非恆真通過；驗證後已還原正式的鎖定程式碼並重新確認測試綠燈。

### 驗證指令與結果（第三輪）

```
dotnet build DoSelect.slnx --no-restore -warnaserror                          → 0 警告 0 錯誤
dotnet format DoSelect.slnx --verify-no-changes --no-restore                  → exit 0
dotnet test DoSelect.slnx --no-build --no-restore --filter "FullyQualifiedName~Returns"
    → Application 33/33、Domain 11/11、Infrastructure 32/32、Api.IntegrationTests 110/110（186/186，0 失敗）
dotnet test DoSelect.slnx --no-build --no-restore --filter "Category!=RequiresSqlServer"（CI 對等）
    → Domain 195/195、Application 99/99、Infrastructure 150/150、Api.IntegrationTests 74/74（518/518，0 失敗）
dotnet test DoSelect.slnx --no-build --no-restore（完整，含 SQL Server-backed）
    → Domain 195/195、Application 99/99、Infrastructure 239/239、Api.IntegrationTests 156/156（689/689，0 失敗）
dotnet list DoSelect.slnx package --vulnerable --include-transitive           → 全部專案皆無易受攻擊套件
```

### 違規項目自我檢查（第三輪，確認未發生）

- 未建立任何新 EF Core Migration、未新增任何資料庫索引——本次鎖定策略刻意選擇「鎖 `OrderItems` 主鍵列」而非「對 `ReturnItems` 新增 `OrderItemId` 索引」，正是為了在不修改 Schema 的前提下完成正確且不過度阻塞的併發控制。
- 未新增任何 NuGet／npm 套件（`Database.SqlQuery<T>` 是 EF Core 內建 API）。
- 未讀取、修改、刪除任何規劃文件、其他組員日誌、客服分支／PR 內容。
- 未 push、未建立 PR、未合併任何分支。
- Commit 範圍僅限與本次併發修正直接相關的 6 個檔案（已用兩道 grep 二次確認）。

### 最終 Git 狀態（第三輪）

```
分支：feature/returns-m12
第三輪 commit：7f0ee8e2d2e84c62509c9aa30fae32ede737fc6f
git status --short（commit 後）：僅 .workflow/（本報告，未追蹤，非程式碼變更）
git diff --stat 034433d..HEAD：6 files changed, 448 insertions(+), 4 deletions(-)
```

## 第四輪：組長 C1／D1 裁定落地（2026-08-25）

### 基準與 commit

- 已先 rebase 最新 `origin/dev` `3164b60`，無衝突。
- C1 commit：`7b4b28d`（`feat(returns): persist per-item descriptions`），僅本地 commit，未 push、未建 PR。

### C1｜ReturnItems.Description Migration（已完成）

- Domain：`ReturnItem.Description` 為 nullable，空白正規化為 null，非空值 trim 後最多 500 字。
- EF／Migration：新增 `ReturnItems.Description nvarchar(500) NULL`；Migration 為 `20260825063738_AddReturnItemDescription`，snapshot 僅新增此欄位。
- 寫入與讀回：`CreateReturnItemLine.Description` 會寫入 `ReturnItem`，customer/admin 共用 `ReturnItemDto` 會回傳 `description`；不再接受後捨棄。
- 前端契約與頁面：重新匯出 `openapi.v1.json`、生成 `schema.d.ts`，顧客與後台退貨明細頁皆顯示每品項說明。
- SQL Server regression：同一測試建立一筆 500 字與一筆 null 說明，確認資料庫 entity 與 read DTO 均能 round-trip；另修正既有 summary query 先建 record 再排序造成的 SQL provider 無法翻譯問題。

### D1｜Cookie／Scheduler 邊界（維持裁定）

- 本輪沒有新增或修改 Guest Cookie 名稱／雜湊流程；正式 C-17 合併前仍維持 provisional 狀態，之後只對齊正式入口並補整合測試。
- 本輪沒有新增 `Timer`、`IHostedService`、`BackgroundService`、Quartz 或 Hangfire。
- `CancelOverdueReturnShipmentsUseCase` 仍只是可供未來統一排程入口呼叫的冪等 Use Case；目前不得宣稱「逾期自動取消已完整交付」。

### 第四輪驗證結果

- 固定 .NET SDK：`10.0.303`。
- `dotnet build -warnaserror`：0 warning／0 error；`dotnet format --verify-no-changes`：通過。
- 完整後端（含 SQL Server）：Domain 195 + Application 99 + Infrastructure 240 + API Integration 156 = **690/690 通過**。
- C1 定向：ReturnService 21/21；ReturnStore SQL Server 3/3。
- customer-web：typecheck、lint 0 warning、12/12 tests、build 通過。
- admin-web：typecheck、lint 0 warning、2/2 tests、build 通過。
- `npm run api:check`：通過。
- NuGet vulnerable package：無。
