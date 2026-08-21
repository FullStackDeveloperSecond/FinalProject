# 給 Terry｜商品目錄分支審查與切分建議書

Terry 你好，本次檢查範圍是 GitHub 最新的 `feature/catalog-search`，Head 為 `1f20ee2`，比較基準為 `origin/dev`（merge-base：`1124b01`）。

先說結論：目前分支變更很大，主要原因是同時包含公開商品查詢、前台商品頁、五種後台資源 CRUD、後台頁面、整合測試與 OpenAPI 型別。核心查詢本身確實有合理複雜度，不需要全部重寫；但目前分支較像一個 Catalog Epic，不適合作為單一 PR 直接合併。

## 1. 目前變更規模

| 類型 | 新增行數 | 占比 |
|---|---:|---:|
| 後端正式程式與設定 | 3,658 | 33.6% |
| 前端正式程式與設定 | 2,865 | 26.3% |
| 測試 | 2,588 | 23.8% |
| OpenAPI 自動產生型別 | 1,584 | 14.6% |
| 開發日誌 | 180 | 1.7% |
| 合計 | 10,875 | 100% |

補充：

- 共變更 90 個檔案，其中 76 個是新增檔案。
- 只有 4 行刪除，並非大量格式化或搬移造成的假 Diff。
- 變更檔案合計約 380 KB，沒有大型二進位、`node_modules`、`bin` 或 `obj`。
- 測試與自動產生型別合計占 38.4%，這一部分的行數增加大致合理。

## 2. 目前分支實際涵蓋範圍

`feature/catalog-search` 不只有商品搜尋，實際包含：

- 公開商品搜尋、商品詳情及篩選選項 API。
- customer-web 商品列表、商品詳情及首頁入口。
- 品牌、分類及標籤管理。
- 商品及 SKU 管理。
- 約 20 個 HTTP Action，其中 17 個是後台管理 Action。
- customer-web 兩個商品 Route。
- admin-web 六個商品管理 Route。
- Application DTO／介面、EF Core 查詢及寫入服務。
- SQL Server Provider-backed 測試、API 整合測試及 Vue 測試。
- customer-web 與 admin-web 各一份 OpenAPI Schema。

因此問題主要是交付單位太大，而不是單一搜尋演算法寫了上萬行。

## 3. 合併前必須處理的阻擋事項

### P1｜後台商品管理 API 尚未套用正式授權

目前下列 Controller 只有授權 TODO，沒有正式 `[Authorize]`／Policy：

- `BrandsController`
- `CategoriesController`
- `TagsController`
- `AdminProductsController`
- `AdminSkusController`

目前 Controller 內也已註明要等待 alex 的 Cookie／Policy 基礎。若直接合併到目前遠端 `dev`，匿名使用者可能呼叫後台商品查詢與寫入 API。

處理要求：

1. 不要以臨時角色判斷、前端隱藏按鈕或環境判斷取代正式 Policy。
2. 等 alex 的 SH-05 身分驗證與授權基礎合併後，將分支更新到該基線。
3. 後台查詢、建立、修改及刪除都必須套用正式 Catalog Policy。
4. API 整合測試至少加入：未登入為 401、已登入但無權限為 403、`CatalogManager` 成功、`SuperAdmin` 依正式矩陣成功。
5. 授權完成前，後台 Controller 及後台 UI 不應進入可合併 PR。

## 4. 建議的最低成本切分方式

現有 Commit 已依功能分組，不需要整批重寫。建議利用現有 Commit 建立三個可依序審查的 PR。

### PR 1｜公開商品查詢與 customer-web

建議內容：

- `4f8ca0d`：公開商品搜尋、詳情、篩選 API 與查詢測試。
- `79fd3b9`：customer-web 商品列表及商品詳情。

驗收重點：

- 只回已上架且可公開資料。
- 搜尋、動態規格、特價、庫存、排序及分頁符合正式契約。
- 不回成本、內部 Id 或草稿資料。
- 前端涵蓋 loading、empty、error、404、分頁及篩選狀態。

### PR 2｜後台 Catalog API

建議內容：

- `8e6e13e`：品牌、分類、標籤、商品及 SKU 的 Application／Infrastructure。
- `4974551`：後台 Controller。
- `b8e4c27`：後台 HTTP 整合測試。

前置條件：

- 必須以含 SH-05 的最新 `dev` 為基線。
- Controller 必須套用正式 Catalog Policy。
- 授權正反例必須和功能測試放在同一個 PR。

如果這一個 PR 仍太大，可再拆成：

1. 品牌／分類／標籤 Lookup 管理。
2. 商品／SKU 管理。

### PR 3｜admin-web 商品管理

建議內容：

- `1f20ee2`：商品列表、商品編輯、SKU 編輯、品牌／分類／標籤頁面。

前置條件：

- PR 2 的 API、Policy、DTO 與錯誤碼已穩定。
- admin-web Route 必須接既有 Admin／2FA／Policy Guard。
- 401、403、409 RowVersion 衝突與重新載入流程必須有明確 UI 行為。

## 5. OpenAPI 型別的調整建議

目前存在兩份從相同 `/openapi/v1.json` 產生的型別：

- `customer-web/src/api/generated/schema.d.ts`：322 行。
- `admin-web/src/api/generated/schema.d.ts`：1,262 行。

customer-web 版本在後台 API 加入前產生，兩份內容已不同步。若兩邊各自重新執行 `generate:api`，同一份 API Schema 會被重複提交，後續也容易產生大型無關 Diff。

建議處理：

1. 先和 alex 的共用 Typed Client 工作確認正式產生位置與指令。
2. 優先只保留一份權威 OpenAPI Schema／Types，再由兩個 Web 專案取用。
3. 產生檔不可手動修改；PR 應記錄產生來源與對應 API Commit。
4. 在共用產生流程定版前，不要讓兩套 Schema 各自演進。

## 6. 可以保留的實作

下列方向整體合理，不需要因分支過大而全部推翻：

- 使用 PublicId 作為 API 資源識別。
- 公開 DTO 與管理 DTO 分離。
- 使用 RowVersion 處理管理端併發。
- SQL Server Provider-backed 查詢與整合測試。
- 商品搜尋處理動態規格、有效特價、庫存及穩定分頁。
- Controller、Application 契約與 Infrastructure 查詢分層。
- 以 OpenAPI 產生 TypeScript 型別，而不是手寫第二套 DTO。
- Commit 已依公開 API、管理服務、前台、Controller、測試及後台頁面分組。

## 7. 局部實作改善

### CatalogLookupTable 型別安全

目前共用元件使用 `Record<string, unknown>`，各頁再以 `as unknown as ...` 還原品牌、分類及標籤表單型別。這減少了一些重複 UI，但也讓 TypeScript 無法驗證欄位是否真正相容。

建議不要再繼續擴張這個弱型別介面。可選擇：

- 保留共用純顯示表格，但讓各資源自行持有具體表單；或
- 等確認 Vue 泛型元件方案後，再改成真正有型別的 Item／Edit Model 契約。

這不是目前分支龐大的主要原因，也不要求為此全面重寫；只需避免後續更多資源依賴雙重型別斷言。

### 重複的翻譯與 Lookup 查詢

商品搜尋、詳情與篩選服務各自包含名稱／翻譯 Lookup。現階段可保留清楚的顯式查詢，但新增相同邏輯前應先確認是否能共用既有批次投影，避免日後出現多套 fallback 規則或逐 SKU 查詢。

## 8. 建議交付順序

1. 先停止直接合併目前整支 `feature/catalog-search`。
2. 以現有 Commit 整理公開商品 PR。
3. 等 alex 的 SH-05 授權基礎進入 `dev`。
4. 建立後台 Catalog API PR並補齊 Policy 測試。
5. 對齊共用 OpenAPI Typed Client。
6. 最後建立 admin-web 商品管理 PR。
7. 每個 PR 各自更新開發日誌，明確列出提供的 DTO、Query、Policy、錯誤碼與未完成依賴。

## 9. 每個 PR 的完成檢查

- [ ] PR 標題與內容只描述本次實際交付的切片。
- [ ] 不包含下一切片的未授權 Controller 或提前建立的 UI。
- [ ] API Route、DTO、錯誤碼與正式文件一致。
- [ ] 管理 API 具有 401／403／成功角色測試。
- [ ] RowVersion 衝突固定回正式 409 錯誤碼。
- [ ] OpenAPI 型別由正式指令產生且沒有第二套手寫 DTO。
- [ ] `dotnet build`、`dotnet test`、`dotnet format --verify-no-changes` 通過。
- [ ] 對應 Web 的 typecheck、lint、test、build 通過。
- [ ] 日誌記錄跨模組契約、未完成依賴及實際驗證結果。

## 最終判斷

目前分支不是因為大量垃圾檔案或全面過度設計而變大；主要是第一個 Catalog 切片一次納入太多公開與後台能力。請保留已完成的核心實作，優先修正授權阻擋，再利用既有 Commit 拆成可獨立審查、測試及回退的 PR。
