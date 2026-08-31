---
文件狀態: 實作完成（M-15、INT-04、S-02、CSV／XLSX 已完成；M-13 由 PR #16 獨立負責）
最後更新: 2026-08-30
實作人: alex
原負責人: terry
基準分支: dev@1cb0f0d9
覆核: kafen（退貨／庫存）、yinyin（退款／折讓金額）
---

# Alex 接手 Terry｜M-15、INT-04、S-02 實作計畫

## 1. 決定與目標

**DONE**：依 `M-15 營運報表 → INT-04 售後與報表核心 E2E → 既有 API Gate 修復 → S-02 評價與審核 → XLSX` 順序交付；XLSX 新 Package Gate 已由 alex 明確核准。M-13 退款執行不在本分支，由 PR #16 獨立負責。

目標是完成：

1. 七個白名單營運報表的 Query、API、權限、匯出、後台 UI 與 SQL Server 證據。
2. 從退貨、既有成功退款結果、發票折讓、庫存到報表的金額與數量一致性 E2E。
3. 核心項目完成後，延伸既有 Reviews Entity／EF 模型完成 S-02。

本計畫不包含 AI 報表解讀、自然語言查詢、新報表資料倉儲、每日彙總表、延伸 Report Key、部署或生產資料庫作業。

## 2. 最低成本分析

| 層級 | 結論 | 理由 |
|---|---|---|
| 不變更 | 不可行 | M-15 只有規格與部分資料模型，Query／API／UI／SQL 證據是空白。 |
| 流程／文件／人工作業 | 不可行 | 人工對帳無法滿足七報表、權限、匯出與可重跑 E2E 驗收。 |
| 設定／資料校正／既有開關 | 不可行 | 沒有現成的營運報表端點可開啟。 |
| 延伸既有路徑 | **採用** | 現有 Application／Controller／EF Query／OpenAPI／Vue 慣例及 Orders、Payments、Returns、Refunds、Invoicing 基礎可直接延伸。 |
| 新相依、服務、Schema 或基礎設施 | **僅採最小 Schema 擴充** | 七份報表仍沿用既有即時 SQL 路徑；庫存週轉的歷史估值缺少不可變成本來源，僅在 `InventoryMovements` 增加 nullable 成本快照，不新增服務、相依或彙總表。 |

## 3. Business Impact

| 項目 | 摘要 |
|---|---|
| 影響角色 | MarketingAnalyst、FinanceManager、CustomerServiceSupervisor、SuperAdmin，以及 Demo 驗收人員 |
| 目前損失／風險 | 無法從系統核對營收、退款、庫存、毛利與售後一致性；M-15 與 INT-04 無法驗收 |
| 觸及範圍／頻率 | 每次後台營運分析、每次 Demo 核心售後旅程；實際使用頻率尚無量測資料 |
| 預期可量測結果 | 7／7 Report Key 可查詢與匯出；權限與公式測試通過；INT-04 金額／數量核對通過 |
| 建置成本 | 多個全堆疊工作包；不虛構工時或金額 |
| 經常性成本 | 主要是即時 SQL 查詢成本與後續公式維護；不新增外部服務或授權費 |
| 預期風險成本 | 誤認列、跨月退款、成本快照誤用及權限外洩；以公式、SQL Server 與 Policy 測試降低 |
| 信心 | 中；規格已定，但 PR #16 的 M-13 契約、M-20 與完整付款垂直切片仍在整合 |
| 成功指標 | 見 Definition of Done |
| 停止／回滾條件 | Migration 出現破壞性操作、庫存寫入無法可靠取得成本快照、查詢優化後 P95 仍超過 3 秒，或上游退款／折讓契約仍會破壞已完成工作包 |

## 4. 已確認基準與假設

### 2026-08-29 WP-003 決策

- **庫存週轉**：採 `InventoryMovement.UnitCostSnapshot` nullable 不可變快照；新異動必須寫入當下 SKU 成本。既有資料不以目前 SKU 成本回填，查詢區間只要缺少邊界估值所需快照，就標示資料不足且不輸出虛構週轉率。Migration 只允許 additive nullable column，不套用資料庫。
- **商品關聯**：Confidence 採方向性規則，`A → B` 與 `B → A` 分別計算及套用門檻；Support 與 Lift 仍以同一完整訂單集合為母體。
- **預測／異常**：最近 30 個完整日、至少 14 個有效日建立線性迴歸，未來 7 日預測值下限為 0；Z-Score 採實際值減迴歸預測值的殘差，並以整個基準視窗的母體標準差計算。
- **較低成本選項拒絕理由**：用目前 SKU 成本回推會違反歷史快照原則；無方向關聯會丟失 Confidence 的條件機率語意；原始銷量 Z-Score 會把正常趨勢誤判為異常。
- **相容／復原**：舊程式可忽略 nullable 欄位；新程式搭配舊 schema 必須等 Migration 後才能啟用報表。應用可先停用庫存週轉讀取；若回退 schema，`Down()` 會丟棄部署後成本快照，因此正式環境應優先 roll forward，而非宣稱資料可逆。

### Repository 事實

- 實作基準為 `dev@1cb0f0d9`。
- .NET SDK 10.0.303；Vue 3.5.41；TypeScript 5.9.3；EF Core SQL Server；xUnit；Vitest；Playwright。
- M-15 沒有 `ReportQuery`、`ReportResultDto`、Controller、Infrastructure Query 或 A-27 頁面。
- `DoSelect.Domain/Reports` 現有內容是「內容檢舉案件」，不是 M-15 營運報表。M-15 新程式必須用 `OperationalReports` 或同等清楚命名避免混淆。
- S-02 的 `ProductReview`、`ReviewImage`、`ProductReviewRevision` 與 EF Configuration 已存在，但 Application／API／UI 未實作。
- M-12 已有完整退貨垂直切片；M-13 退款執行由 PR #16 獨立負責。本工程包的 INT-04 只消費既有成功退款與分攤結果，並復用既有 M-20 折讓 Writer／API 契約完成跨模組核對。

### 非阻擋假設

- 第一版報表使用現有交易與快照資料，不改 Schema。
- Report API 是新增契約，不移除或改名現有 Endpoint，因此舊前端仍相容。
- CSV 保持相容；XLSX 所需 `ClosedXML 0.105.1` 已於 2026-08-29 由 alex 明確核准，只用於營運報表輸出，不延伸匯入或其他 Office 功能。

## 5. 需求與驗收

| ID | 優先 | 需求 | 驗收證據 |
|---|---|---|---|
| REQ-001 | Must | 只接受七個已定 Report Key | 白名單單元測試；未知 Key 回 `400 report_key_invalid` |
| REQ-002 | Must | 使用 `ReportQuery`、`ReportResultDto` 與每個 Report Key 獨立 Row Schema | Application 契約測試；OpenAPI contract diff |
| REQ-003 | Must | 日期、時區、粒度、篩選與 Cursor 在伺服器端驗證 | 邊界單元／API 測試；錯誤為 `report_range_invalid` |
| REQ-004 | Must | 依報表類型強制管理員 Policy | 未登入 401、角色不符 403、允許角色 200 |
| REQ-005 | Must | 營收以收款日認列，成功退款於成功日沖減 | SQL Server Provider-backed 跨日／跨月測試 |
| REQ-006 | Must | 毛利、ABC、同期、庫存週轉、關聯、預測／異常符合定版公式 | 純計算單元測試＋SQL 投影整合測試 |
| REQ-007 | Must | 提供 `GET /api/v1/admin/reports/{reportKey}` 與 `/export` | API 契約與授權整合測試 |
| REQ-008 | Must | A-27 共用頁面支援載入、空白、錯誤、篩選、摘要、明細與匯出 | Vitest／typecheck／build；Playwright 核心旅程 |
| REQ-009 | Must | CSV／XLSX 匯出含 DEMO DATA／Metadata，外部文字不執行公式且不含個資 | 兩種匯出內容與權限測試 |
| REQ-010 | Must | INT-04 以 M-13 已成功退款／分攤結果證明退貨／折讓／庫存／報表一致 | 可重跑 SQL Server＋HTTP／UI E2E；不重複驗證退款執行 |
| REQ-011 | Should/Gated | 完成已購評價、圖片安全、審核、公開與 Revision | M／Demo Gate 通過後的全堆疊測試 |
| NFR-001 | Must | 報表查詢先即時彙總，優化後 P95 目標 3 秒 | 固定 Demo Seed 量測；超標時停止並重開資料設計決定 |
| NFR-002 | Must | 不暴露內部 Id、收件人、Email、電話、地址或 AI 對話全文 | DTO／OpenAPI／匯出負面斷言 |

## 6. 架構決定與變更地圖

### DEC-001｜使用 OperationalReports 垂直切片

新增 `DoSelect.Application/OperationalReports`、`DoSelect.Infrastructure/OperationalReports` 與 `DoSelect.Api/OperationalReports`，避免與既有「內容檢舉」`Domain/Reports` 混淆。計算規則放 Application 並保持純函數可測；EF 查詢只負責有界投影與彙總；Controller 只處理 HTTP、Policy 與 Problem Details。

### DEC-002｜不新增報表資料表

第一版依定版規格使用 SQL Server 即時彙總與必要現有索引。只有在固定 Demo Seed 查詢與索引優化後仍超過 P95 3 秒，才可停止本路徑並另立反正規化決定。

### 預計檔案地圖

| 層 | 路徑 | 責任 |
|---|---|---|
| Application | `src/backend/DoSelect.Application/OperationalReports/` | Report Key、Query／Result／Row DTO、驗證、公式、Query Port |
| Infrastructure | `src/backend/DoSelect.Infrastructure/OperationalReports/` | EF Core 即時彙總、Cursor、匯出資料投影、DI |
| API | `src/backend/DoSelect.Api/OperationalReports/` | Controller、Policy 分流、Problem Details、CSV／XLSX 回應 |
| Security | `DoSelectSecurityConstants.cs`、`SecurityServiceCollectionExtensions.cs` | 新增營運報表 View／Finance Policy；不復用檢舉審核 Policy |
| OpenAPI | `contracts/openapi.v1.json`、`frontend/shared/src/api/generated/schema.d.ts` | 依現有 generator 同步契約 |
| Vue | `frontend/admin-web/src/features/operationalReports/`、`pages/OperationalReportPage.vue`、router／sidebar | A-27 共用殼層與完整使用者狀態 |
| Tests | Application／Infrastructure／API／admin-web／Playwright 現有專案 | 公式、SQL 翻譯、Policy、契約、UI 與 INT-04 |

## 7. 工作包與依賴

| WP | 產出 | 覆蓋需求 | 依賴 | 驗證 | 大小／不確定性 |
|---|---|---|---|---|---|
| WP-001 | OperationalReports 契約、白名單、日期／Cursor 驗證、公式基礎、Policy 名稱 | REQ-001～004 | 無 | Application 測試＋Security 測試＋build | M／Low |
| WP-002 | 銷售總覽、ABC、同期、毛利 SQL 垂直切片 | REQ-005～006 | WP-001；Orders／Payments／Refunds 穩定投影 | 純計算＋SQL Server Provider-backed | L／High |
| WP-003 | 庫存週轉、關聯組合、預測／異常 SQL 垂直切片 | REQ-006 | WP-001；Inventory／Orders 投影 | 純計算＋SQL Server Provider-backed | L／Medium |
| WP-004 | 查詢 API、匯出、OpenAPI／Typed Client | REQ-001～007、09 | WP-002／003 | API Pipeline／Policy／契約／匯出測試 | M／Medium |
| WP-005 | A-27 共用後台頁面 | REQ-008 | WP-004 | Vitest／typecheck／lint／build | M／Low |
| WP-006 | INT-04 SQL Server＋HTTP／UI E2E | REQ-010 | WP-002～005；M-12／M-20 完成；PR #16 提供 M-13 成功退款結果契約 | 可重跑跨模組旅程與金額／數量斷言，不呼叫退款執行端點 | L／High |
| WP-007 | S-02 已購評價與審核垂直切片 | REQ-011 | 所有 M／Demo Gate；OrderItem Owner Query | 單元／API／SQL／Vue／E2E | L／Medium |

### 執行波次

```text
Wave 1: WP-001
Wave 2: WP-002 || WP-003
Wave 3: WP-004 -> WP-005
Wave 4: WP-006（消費 PR #16 的 M-13 成功退款結果，同時需 M-20 Gate）
Wave 5: WP-007（只在 M／Demo Gate 通過後）
```

## 8. 可追溯驗證

| 需求 | 工作包 | 計畫證據 | Gate |
|---|---|---|---|
| REQ-001～004 | WP-001／004 | Application 白名單／驗證測試；API 401／403／400／200 | Contract Gate |
| REQ-005～006 | WP-002／003 | 公式單元測試；固定 SQL Server Seed 輸出 | Data Gate |
| REQ-007／009 | WP-004 | OpenAPI diff；API／匯出安全測試 | API Gate |
| REQ-008 | WP-005 | Vue 狀態與請求參數測試；前端稽核 | UI Gate |
| REQ-010 | WP-006 | 跨模組 SQL／HTTP／瀏覽器旅程 | Integration Gate |
| REQ-011 | WP-007 | 評價所有權、審核、圖片掃描、Revision 證據 | S Gate |

已存在的廣範檢查命令：

```powershell
dotnet build DoSelect.slnx --no-restore -warnaserror
dotnet test DoSelect.slnx --no-build --no-restore
dotnet format DoSelect.slnx --verify-no-changes --no-restore
dotnet list DoSelect.slnx package --vulnerable --include-transitive

Set-Location frontend/admin-web
npm run typecheck
npm run lint -- --max-warnings 0
npm test
npm run build
```

各工作包先執行 focused tests，再依風險擴大；未執行的檢查不宣稱通過。

## 9. 風險與 Gate

| ID | 觸發／影響 | 降低方式 | 偵測／停止 |
|---|---|---|---|
| RISK-001 | `Reports` 命名與內容檢舉混淆 | 對營運報表固定使用 `OperationalReports` | final diff 不得將 M-15 放入既有檢舉 Aggregate |
| RISK-002 | 付款／退款狀態尚未完整整合，報表誤認列 | 使用定版事件時間與成功狀態；上游契約改動時重跑 | SQL 跨月回歸不通過時停止合併 |
| RISK-003 | 毛利或退款分攤誤用現值 | 只使用 OrderItem／Refund 快照與 yinyin 覆核 | 快照負面測試；金額不同時 No-Go |
| RISK-004 | 即時彙總超過 P95 3 秒 | 有界投影、穩定分頁、量測後才補現有索引 | 固定 Demo Seed 優化後仍超標則停止，不自行加彙總表 |
| RISK-005 | PR #16 的 M-13 契約或 M-20 未完成阻擋 INT-04 | 本分支不重複實作 M-13；以已成功退款／分攤前置資料驗證下游，並復用既有 M-20 寫入路徑 | PR #16 契約變更時重跑 SQL／HTTP／UI 跨模組旅程 |
| RISK-006 | S-02 擠壓 M 核心範圍 | 固定為 Wave 5 | M 實作矩陣未過 Gate 則不開工 |

### Definition of Ready

- Report Key、指標公式、時間歸屬、角色 Policy 與 API 契約已在權威文件定版。
- 現有 SQL Server 測試架與 OpenAPI／Typed Client 流程可復用。
- 本實作使用隔離 worktree，不重疊未提交的 M-18 變更。

### Definition of Done

- 7／7 Report Key 的 Query、API、獨立 Row Schema、Policy、匯出、UI 及所規劃驗證完成。
- 前端與 OpenAPI 契約同步；無新增個資外洩、無任意 SQL／元件名稱。
- INT-04 在 SQL Server／HTTP／UI 邊界可重跑，以既有成功退款結果核對折讓、庫存與報表；M-13 執行行為由 PR #16 驗證。
- 只有在 M／Demo Gate 後，S-02 才可列入完成範圍。
- 沒有套用 production migration、生產 SQL、部署、push 或 merge。

### No-Go

- 未定義財務口徑或同一指標在權威文件間矛盾。
- 需要破壞性 Schema／API 變更但尚未裁定相容性。
- 權限範圍無法從現有角色與定版 Policy 推導。
- Provider-backed 證據不可用，卻要宣稱 SQL 計算或交易一致性完成。

## 10. 執行進度

| 時間 | 工作包 | 狀態 | 證據／備註 |
|---|---|---|---|
| 2026-08-29 | 計畫基準 | 完成 | 依 `dev@1cb0f0d9` 重新核對規格、程式、測試與未完成矩陣 |
| 2026-08-29 | WP-001 | 完成 | 已建 OperationalReports 契約、7 Key 白名單、日期／篩選／Cursor 驗證、ABC／比率基礎與兩個獨立 Admin MFA Policy；Application 27 cases、Policy 4 cases 通過 |
| 2026-08-29 | WP-002 | 完成 | `sales-overview`、`product-abc`、`period-comparison`、`gross-margin` 已實作；涵蓋付款／完成／退款時間口徑、成本快照、退款數量、80%／95% ABC、同期零分母、分類過濾及不透明 Cursor。Solution build 0 warnings、Application 428／428、SQL Server Provider 5／5、format 通過 |
| 2026-08-29 | WP-003 | 完成（Migration Gate） | `inventory-turnover`、`product-associations`、`forecast-anomalies` 已實作；成本與數量事件分流重建歷史估值、舊資料缺快照標示不足、方向性 Confidence、30 日迴歸／7 日預測、殘差母體 Z-Score 與門檻均有測試。SQL Server 報表 9／9、成本變更寫入 1／1、統計 3／3、Infrastructure 593／593、Domain 475／475、Application 431／431 通過；Migration 僅新增 nullable `decimal(18,2)`，pending-model check 為否且未套用資料庫 |
| 2026-08-29 | WP-004 | 完成 | 新增查詢、CSV 與 XLSX Endpoint、一般／財務雙層 Policy；兩種匯出共用相同篩選、Cursor 展開與 100,000 列上限。CSV 保留 UTF-8 BOM／公式注入防護；XLSX 提供 README／Summary／Rows 三張表、固定欄位、typed 數值／日期／布林、外部文字非公式。OpenAPI 與 shared Typed Client 已重產。 |
| 2026-08-29 | WP-005 | 完成 | A-27 共用 Vue 頁面已接七個白名單 Route，包含角色導覽、日期／分類／品牌／狀態／粒度篩選、載入／空白／錯誤／重試、摘要、無外部套件趨勢圖、明細、Cursor 載入與 CSV 下載；過期請求不覆蓋目前報表，預設日期固定依 Asia/Taipei 切日。focused 9／9、admin-web 全套 165／165、typecheck、lint、production build 通過。 |
| 2026-08-30 | M-13 範圍收斂 | 完成 | 依最新裁定移除本分支的退款執行 Endpoint、Application／Infrastructure 擴充與專屬測試；M-13 由 PR #16 獨立負責，避免重複實作與合併衝突。 |
| 2026-08-29 | WP-006 | 完成 | 新增退貨可再販品的庫存 Port／Writer，檢驗完成時在同一 `SaveChanges` 寫入 ReturnToStock movement 與 balance；INT-04 以既有成功退款／分攤前置資料驗證下游，不執行 M-13；真實 SQL 跨模組、SQL-backed HTTP 與 A-27 Playwright 核對付款 NT$1,060、退款 NT$500、淨營收 NT$560。 |
| 2026-08-29 | 既有 API Gate 修復 | 完成 | 30 個 Support 測試的根因是共用 ambient dev DB 缺 `AuditLogs`；改為專屬、隔離、完整 Migration 的 ephemeral SQL fixture，並補齊 Returns fixture 的實際 SKU／庫存資料。完整 API Integration 565／565 通過。 |
| 2026-08-29 | WP-007 | 完成 | 復用既有 `ProductReview`／`ReviewImage`／`ProductReviewRevision`、檔案掃描、Audit、Identity 與 Vue，不新增 Schema／服務／Package。完成已完成訂單與所有權限制、一品項一評價、草稿／送審／核准／退回／隱藏／恢復、公開後編輯立即下架並保存 Revision、最多 3 張 JPG／PNG 5 MB、只公開 Approved 且不含 PII。SQL-backed HTTP 2／2、Domain 3／3、Vue focused 4／4、Playwright 2／2 通過。 |
| 2026-08-29 | 廣範回歸 Gate | 完成 | API 565／565、Domain 479／479、Infrastructure 617／617、customer-web 147／147、S-02 Playwright 2／2 已通過；加入 XLSX 後 Application 更新為 460／460、admin-web 更新為 169／169，Solution `-warnaserror` build 0 warning／0 error、兩站 typecheck／lint／production build、format 與 NuGet transitive vulnerability scan 全數通過。 |
| 2026-08-29 | XLSX Gate | 完成 | alex 核准新增 `ClosedXML 0.105.1`。新增 `GET /api/v1/admin/reports/{reportKey}/export/xlsx` 與 A-27 XLSX 按鈕；7／7 Row Schema、空資料固定 Header、opaque Cursor、多頁、100,000 列拒絕、ZIP/XLSX 簽章、MIME、下載檔名、公式安全與 Policy Pipeline 均有測試。Focused Application 11／11、Controller／HTTP 13／13、Vue 9／9 通過；官方 NuGet 弱點索引未回報任何 direct 或 transitive vulnerable package。 |
| 2026-08-29 | SQL Server Gate | 已解除 | 原加密錯誤確認為受限沙箱無法委派 Windows 認證；改在沙箱外執行同一隔離測試後可連線 `SQL2025`。校正合法 Created cohort 日期的測試預期後，真實 Provider 測試通過；未變更 SQL Server 或正式連線設定 |
