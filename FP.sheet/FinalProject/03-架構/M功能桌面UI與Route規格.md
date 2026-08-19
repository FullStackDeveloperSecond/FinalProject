---
文件狀態: 已確認
最後更新: 2026-08-14
適用範圍: M 功能桌面版
追蹤項目:
  - DES-10
  - DES-16
  - SH-09
  - SH-10
---

# M 功能桌面 UI 與 Route 規格

## 文件目的

本文件是 `customer-web` 與 `admin-web` 的第一版功能頁面契約，固定頁面 Route、角色入口、主要 Server State、API 對照及共同失敗狀態。商業規則仍以領域需求及使用案例為準；API 欄位、Policy 與錯誤碼分別以 [[03-架構/API Endpoint目錄]]、[[03-架構/API DTO與Schema契約]]、[[01-需求/角色與權限]] 及 [[03-架構/API錯誤碼目錄]] 為準。

本頁只規範 M 桌面版。手機與平板 RWD 是 S-07；繁中以外語系是 S。Logo、色票與視覺風格由 PM-02 補充，不得改變本文件的 Route、授權及資料契約。

## 已確認的資訊架構

- 消費者前台是主要網站，不設 `/shop` 或 `/frontend` 共同前綴。
- 管理後台所有 Web Route 統一置於 `/admin/*`。
- `/admin/*`、Router Guard、側邊選單與隱藏按鈕只改善體驗；ASP.NET Core Policy 與資源所有權才是授權邊界。
- 公開分享、Email 驗證與密碼重設只在 URL 放不可避免的短效／不可預測 Token；完成後立即清除可清除的 Query，不寫入 Log 或前端持久儲存。
- 商品、訂單及其他可路由資源使用 PublicId；不得在 Web Route 暴露 SQL `bigint Id`。

## 消費者前台殼層

固定主導覽：首頁、商品、AI 懂選、自由組裝、購物車。右側依 Session 顯示登入／註冊，或會員選單、通知及登出。客服入口在主導覽次要區與頁尾保留；Demo／無品牌背書聲明固定顯示於頁尾。

| Page ID | Web Route | 角色／Guard | 頁面責任 | 主要 API／使用案例 |
|---|---|---|---|---|
| C-01 | `/` | Public | 商業價值、一般搜尋入口、AI 導購入口、熱門可售商品；不建立獨立首頁資料真實來源 | `GET /products`、UC-SEARCH-01 |
| C-02 | `/products` | Public | 關鍵字、分類、品牌、價格、庫存與白名單規格篩選；條件與頁碼寫入 URL Query | `GET /products`、`GET /catalog/filter-options`、UC-SEARCH-01 |
| C-03 | `/products/:productId` | Public | 商品、SKU、規格、圖片、庫存狀態、配送限制及加入購物車 | `GET /products/{id}`、`POST /cart/items`、API-M-01／04 |
| C-04 | `/ai-search` | Public＋額度 | 輸入自然語言、回答補問、顯示推薦理由／預算／限制；AI 失敗可轉一般搜尋 | `POST /ai/product-search/recommendations`、UC-AI-SEARCH-01～03 |
| C-05 | `/builds/new` | Public | 訪客本地草稿、選零件、確定性相容檢查、登入後保存 | `POST /compatibility-checks`、`POST /build-lists`、UC-BUILD-01、UC-COMPAT-01 |
| C-06 | `/builds/:buildId` | Owner Member | 編輯、重驗、分享／撤銷、刪除、加入購物車 | Build List CRUD、Share、Add-to-cart Action、API-M-06 |
| C-07 | `/builds/shared/:shareToken` | Public | 唯讀去識別化分享、重新驗證、複製或加入購物車；失效統一顯示不可使用 | `GET /build-shares/{token}`、UC-BUILD-01 |
| C-08 | `/register` | PublicOnly | 註冊並顯示待驗證狀態 | UC-AUTH-01 |
| C-09 | `/verify-email` | Public | 送出 Email 驗證 Token；成功後導向登入 | UC-AUTH-01 |
| C-10 | `/login` | PublicOnly | 會員登入；成功後處理訪客購物車合併結果 | UC-AUTH-02、UC-CART-02 |
| C-11 | `/forgot-password` | PublicOnly | 送出一致安全訊息，不揭露帳號是否存在 | UC-AUTH-03 |
| C-12 | `/reset-password` | Public | 驗證短效 Token 並設定新密碼 | UC-AUTH-03 |
| C-13 | `/cart` | Public Cart／Member | 購物車群組、增刪改、價格／庫存／相容性衝突處理、優惠券及結帳前重驗 | Cart、Coupon、Shipping Options、UC-CART-01／02、UC-COUPON-01 |
| C-14 | `/checkout` | Public Cart／Member | 收件資料、配送、示範門市、付款方式、模擬發票及政策確認；只送識別與使用者輸入，不送價格 | Shipping／Store、`POST /orders`、UC-CHECKOUT-01／COD-01 |
| C-15 | `/orders/:orderId/payment` | Order Owner／Guest Scope | 建立或重試付款嘗試、顯示 ATM／超商代碼或即時付款模擬結果 | UC-PAY-01 |
| C-16 | `/guest-orders/access` | Public | 輸入訂單編號與 Email，回應不揭露訂單是否存在 | UC-GUEST-ORDER-01 |
| C-17 | `/guest-orders/verify` | Public | 驗證 10 分鐘一次性碼並建立 30 分鐘限單 Cookie | UC-GUEST-ORDER-01 |
| C-18 | `/orders/:orderId` | Owner Member／Guest Scope | 訂單、付款、物流、退款、快照與合法取消／退貨入口 | `GET /orders/{id}`、Cancel Action、UC-GUEST-ORDER-01、UC-RETURN-01 |
| C-19 | `/orders/:orderId/returns/new` | Order Owner／Guest Scope | 選擇可退明細、數量、理由、說明與附件，送出單項退貨申請 | `POST /orders/{orderId}/returns`、UC-RETURN-01 |
| C-20 | `/returns/:returnId` | Order Owner／Guest Scope | 退貨申請狀態、寄回期限、附件、審核、退款進度 | `GET /returns/{id}`、附件、UC-RETURN-01 |
| C-21 | `/account` | Member | 基本資料與聯絡電話；Email 只顯示遮蔽值 | Session、`GET/PUT /members/me`、API-M-02 |
| C-22 | `/account/addresses` | Member | 收件地址列表、新增、修改、刪除及預設地址 | Member Address CRUD、API-M-02 |
| C-23 | `/account/orders` | Member | 自己的訂單列表、狀態與合法入口 | `GET /orders`、API-M-05 |
| C-24 | `/account/builds` | Member | 已保存組裝清單列表及新建入口 | `GET /build-lists`、API-M-06 |
| C-25 | `/notifications` | Member | 站內通知列表、單筆／全部已讀及白名單導向 | Notification Endpoints、API-M-03 |
| C-26 | `/support` | Member | 顯示 AI 外部處理告知、AI 客服及人工客服入口；未同意不得呼叫 OpenAI | UC-AI-SUPPORT-01、UC-SUPPORT-01 |
| C-27 | `/support/ai` | Member＋有效同意 | 非串流 AI 問答、引用、免責、本人訂單選取、剩餘額度及轉人工 | UC-AI-SUPPORT-01～04 |
| C-28 | `/support/tickets` | Member | 自己的客服案件列表及建立入口 | `GET /support-tickets`、API-M-11 |
| C-29 | `/support/tickets/new` | Member | 七類分類、主旨、訊息、可選本人訂單與最多三個附件 | `POST /support-tickets`、附件、UC-SUPPORT-01／02 |
| C-30 | `/support/tickets/:ticketId` | Owner Member | 對話、附件、狀態、SLA 摘要與合法取消；不顯示內部備註 | Ticket Detail／Messages／Cancel、UC-SUPPORT-01／02 |

`/orders/:orderId` 是會員與已驗證訪客共用的畫面 Route，但 API 分別以會員 Cookie 或限單 Cookie 授權；前端不得接受使用者輸入 Member PublicId。

## 管理後台殼層

管理端固定使用 `/admin/*`、桌面側邊導覽與頂部 Session 區。選單依完成 2FA 後取得的 Policy 摘要顯示；沒有任何可用模組時顯示 403，不以 SuperAdmin 畫面當作所有角色的預設。

| Page ID | Web Route | 角色／Policy | 頁面責任 | 主要 API／使用案例 |
|---|---|---|---|---|
| A-01 | `/admin/login` | PublicOnly Admin | 管理員密碼第一階段登入 | UC-ADMIN-AUTH-01 |
| A-02 | `/admin/totp` | TwoFactorChallenge | TOTP 或 Recovery Code 第二階段；完成前不可載入管理資料 | UC-ADMIN-AUTH-01 |
| A-03 | `/admin` | Admin＋2FA | 依 Policy 顯示可用模組捷徑與 DEMO DATA 標示；不另建重複統計 API | Admin Session |
| A-04 | `/admin/products` | Catalog View／Manage | 商品列表、篩選、批次上／下架、批次調價與匯出 | Admin Products、Bulk Action、UC-ADM-PROD-01／02 |
| A-05 | `/admin/products/new` | Catalog Manage | 建立 Product 與第一個預設／變體 SKU | Product／SKU Create、UC-ADM-PROD-01 |
| A-06 | `/admin/products/:productId` | Catalog View／Manage | 商品、SKU、規格、圖片、來源／授權、RowVersion；依 Policy 控制成本及發布 | Product／SKU／Image Endpoints、UC-ADM-PROD-01／02 |
| A-07 | `/admin/products/import` | Catalog Import | 模板下載、XLSX／三 CSV 預覽、逐列錯誤、錯誤 CSV 及原子確認 | Product Import Endpoints、UC-IMPORT-01 |
| A-08 | `/admin/catalog/lookups` | Catalog Manage | 品牌、分類及標籤分頁管理；已引用資料以停用處理 | Brand／Category／Tag Endpoints |
| A-09 | `/admin/catalog/specifications` | Catalog Manage | 分類規格範本、Option、排序與受保護 Semantic Key | Specification Definition Endpoints |
| A-10 | `/admin/catalog/compatibility` | Compatibility Policy | 規則查看、警告門檻、SuperAdmin 啟停及無寫入測試 | Compatibility Admin Endpoints、UC-COMPAT-01 |
| A-11 | `/admin/inventory` | Inventory View | SKU 庫存餘額、低庫存與異動明細 | Balance／Movement Endpoints、API-M-08 |
| A-12 | `/admin/inventory/reservations` | Inventory Manage | Cursor 保留佇列、二次確認、理由及人工釋放 | UC-ADM-INV-01 |
| A-13 | `/admin/inventory/imports` | Inventory Adjust | 庫存模板預覽、逐列錯誤及原子確認 | Inventory Import Endpoints、API-M-08 |
| A-14 | `/admin/orders` | Order View／Manage | Cursor 訂單列表、摘要狀態／徽章篩選、勾選批次出貨 | UC-ADM-ORDER-01、UC-ADM-SHIP-02 |
| A-15 | `/admin/orders/:orderId` | Order／用途特定 Policy | 訂單五狀態、歷程、合法 Action；完整收件資料另按需取得並稽核 | Admin Order／Recipient、UC-ADM-ORDER-01／02 |
| A-16 | `/admin/shipping/batches` | Order Manage | 建立最多 100 筆批次出貨、逐筆結果與 CSV | UC-ADM-SHIP-02 |
| A-17 | `/admin/shipping/stores` | Store View／Manage | 100 筆虛構門市、搜尋、新增、修改與停用 | UC-ADM-STORE-01 |
| A-18 | `/admin/shipping/package-limits` | Order Manage | 超商／宅配限制版本、草稿、排程發布及歷史 | UC-ADM-SHIP-01 |
| A-19 | `/admin/returns` | Return View | 退貨列表、期限、注意旗標與工作台回連 | Admin Return Query、UC-RETURN-01 |
| A-20 | `/admin/returns/:returnId` | Return Process／Approve | 收貨、檢查、一次延長、審核、退款分攤預覽；依 Policy 顯示 Action | Return Actions／Review、UC-RETURN-01／UC-REFUND-01 |
| A-21 | `/admin/refunds` | Refund View／Execute | 退款列表、狀態與金額摘要 | Admin Refund Query、UC-REFUND-01 |
| A-22 | `/admin/refunds/:refundId` | Finance／SuperAdmin | 分攤明細、金額上限、TOTP 二次確認、冪等執行及歷程 | Refund Detail／Execute、UC-REFUND-01 |
| A-23 | `/admin/coupons` | Marketing／Finance／SuperAdmin | 優惠券列表、建立、修改、啟用／暫停／停用與規則預覽 | Coupon Admin Endpoints、UC-COUPON-01 |
| A-24 | `/admin/cases` | 各案件領域 View | 三領域統一摘要、Cursor、授權後導向原領域詳情 | UC-WORKBENCH-01 |
| A-25 | `/admin/support/sla` | CustomerService／Supervisor | 首回／結案期限、80%／逾時、負責人及佇列排序 | UC-SLA-01 |
| A-26 | `/admin/support/tickets/:ticketId` | CustomerService／Supervisor | 公開回覆、內部備註、自領／指派／轉派、優先級與合法狀態 | Support Admin Endpoints、UC-SUPPORT-01／02 |
| A-27 | `/admin/reports/:reportKey` | Report-specific Policy | 七個 M 報表共用殼層、日期／篩選／摘要／圖表／明細／匯出 | UC-REPORT-01 |
| A-28 | `/admin/ai/usage` | 依成本可見 Policy | AI 次數、Token、成本彙總、保護狀態；成本明細只限 Finance／SuperAdmin | UC-AI-SUPPORT-04 |

## 七個報表 Route 白名單

| Report Key | 顯示名稱 |
|---|---|
| `sales-overview` | 銷售總覽 |
| `product-abc` | 商品排行與 ABC 分級 |
| `period-comparison` | 同期比較 |
| `inventory-turnover` | 庫存周轉分析 |
| `gross-margin` | 毛利分析 |
| `product-associations` | 關聯組合分析 |
| `forecast-anomalies` | 預測與異常偵測 |

未知 `reportKey` 固定由 API 回 `400 report_key_invalid`，前端顯示報表不存在並返回可用報表清單，不動態載入任意元件或 SQL。

## Route Guard 與導向

| Guard | 行為 |
|---|---|
| `PublicOnly` | 已登入會員進入登入／註冊頁時導向原安全 ReturnUrl 或 `/account`；ReturnUrl 只接受本站相對路徑 |
| `MemberRequired` | 未登入導向 `/login?returnUrl=...`；API 仍重新驗證 |
| `GuestOrderScope` | 沒有限單 Cookie 時導向 `/guest-orders/access`；不得在前端保存 Guest Token |
| `AdminRequired` | 未登入導向 `/admin/login`；未完成 2FA 導向 `/admin/totp` |
| `PolicyHint` | 選單與按鈕可依 Policy 摘要隱藏；進頁與每次 API 操作仍接受 403 並顯示無權限 |

禁止把完整個資、TOTP Secret、Recovery Code、驗證 Token、付款模擬密鑰、附件實體路徑或 OpenAI Context 放入 Route、Query、Pinia Persistence 或瀏覽器儲存。

## 狀態責任

| 狀態種類 | 正式擁有者 | 規則 |
|---|---|---|
| API Server State | TanStack Query | 商品、購物車、訂單、案件、報表、管理列表與明細；Query Key 必須包含 Filter、Page／Cursor 與資源 PublicId |
| Session／跨頁 UI 狀態 | Pinia | 只保存由 Session Endpoint 重建的登入摘要、側邊欄、非敏感 UI 偏好與暫存流程選擇；不得複製一般 API Cache |
| 可分享列表條件 | Vue Router Query | 關鍵字、篩選、排序、`pageNumber`；同一 Query 可重整與返回 |
| Cursor | TanStack Query 記憶體 | Cursor 綁定篩選／排序／授權，不寫成可任意重放的 URL 頁碼；條件改變即清除 |
| 表單輸入 | Page／Form composable | Server validation errors 對應欄位；送出中禁止重複命令；成功後失效相關 Query |
| 訪客組裝草稿 | customer-web 本地草稿 Adapter | 只存 SKU PublicId、數量與非敏感名稱；開啟時重新向 API 驗證，不作價格／相容性真實來源 |

## 每頁共同顯示狀態

所有查詢頁及明細頁必須明確處理：

1. 初次載入：Skeleton 或 Loading，避免把空陣列誤顯示成「沒有資料」。
2. 空白：說明目前沒有資料或篩選無結果，保留條件並提供合法下一步。
3. 驗證錯誤：依 `errors` 綁定欄位；未知欄位錯誤顯示頁面摘要。
4. `401`：前台導向正確登入入口並保留安全 ReturnUrl；管理端回 `/admin/login`。
5. `403`：顯示無權限，不以 404 頁或隱藏按鈕取代說明。
6. `404`：商品／分享／他人資源使用不洩漏的不存在畫面；提供安全返回入口。
7. `409 concurrency_conflict`：保留未送出的表單草稿，要求重新載入，不自動覆蓋新版本。
8. 其他 `409`：依穩定錯誤碼顯示可執行修正，例如改量、重選付款、回到售後流程。
9. `429`：顯示可安全揭露的重試時間；AI 頁顯示剩餘／重設資訊。
10. `503`：基本電商頁提供重試；AI 依契約降級一般搜尋或人工客服；檔案掃描失敗不得假裝上傳成功。
11. 未預期錯誤：顯示 Correlation／Trace ID 供查詢，不顯示 Stack Trace 或 SQL。

## 高風險互動

- 發布／下架、庫存釋放、訂單取消、包裹版本發布、退貨審核、退款執行、客服轉派／取消及規則停用都使用確認對話框。
- 確認框顯示資源、影響、目前狀態及不可逆性；需理由的 Action 在對話框強制輸入受限長度原因。
- 退款執行、完整個資讀取、相容性硬規則停用等仍依後端 Policy、TOTP、用途及 Audit 驗證；前端二次確認不構成安全控制。
- 批次操作先顯示選取數與合法上限，回應逐筆呈現成功／失敗，不以單一 Toast 隱藏部分失敗。

## 建議前端檔案責任

Solution、兩個 Vue 應用、`router/` 與最小 `pages/` 已建立；以下是後續商業功能應補齊並持續遵守的目標路徑：

```text
frontend/customer-web/src/
├─ router/          # 本文件 C-01～C-30、Guard 與安全 ReturnUrl
├─ pages/           # Route 層頁面，只協調 feature 與狀態
├─ features/        # catalog、ai-search、builds、cart、checkout、orders、returns、support
├─ stores/          # session 與非敏感跨頁 UI 狀態
├─ api/generated/   # OpenAPI 產生碼，不手改
└─ api/             # fetch wrapper、Problem Details 與 Antiforgery

frontend/admin-web/src/
├─ router/          # 本文件 A-01～A-28、Admin／2FA／Policy Guard
├─ pages/           # Route 層頁面
├─ features/        # catalog、inventory、orders、shipping、returns、refunds、coupons、cases、reports、ai-usage
├─ stores/          # admin session、選單與 UI 偏好
├─ api/generated/   # OpenAPI 產生碼，不手改
└─ api/             # 共通 wrapper 與錯誤處理
```

跨 App 可共用 OpenAPI 產生流程與設計 Token，但第一版不在尚無證據時先建立大型共用 Component Package；發現至少兩個 App 有穩定相同需求後再抽取。

## 驗收與完成門檻

- C-01～C-30、A-01～A-28 的 Route 都能對應本文件列出的 API 或純導覽責任。
- 19 項 M 功能至少有一個頁面入口；沒有 S／O 頁面混入 M 導覽。
- API-M-01～12 的契約測試及 [[03-架構/M功能測試案例目錄]] 既有案例能覆蓋成功、空白、錯誤、授權、併發與降級。
- 前台與後台使用產生的 TypeScript Client；不得為單頁手寫不同 DTO。
- 桌面代表寬度為 1280px，最低 M 桌面寬度為 1024px；360／768 RWD 只在 S-07 啟動後驗收。
- PM-02 的視覺資產可後補，但不得在沒有 Logo 時阻塞 Router、Server State、權限、表格與表單功能開發。

## 明確排除

- S：收藏、商品評價、檢舉、多語系、消費者前台 RWD、AI 客服摘要、AI 報表分析。
- O：自然語言詢問營運資料。
- 後台手機版、地圖選店、真實金流／物流、正式公網部署。
