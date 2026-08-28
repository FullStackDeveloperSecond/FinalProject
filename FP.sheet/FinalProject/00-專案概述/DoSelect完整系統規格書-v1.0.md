---
文件狀態: 已確認／凍結快照
規格版本: "1.0"
基線日期: 2026-08-14
適用範圍: DoSelect 第一版 M 功能與開發驗收基線
文件負責人: alex
就緒度: READY
快照凍結日期: 2026-08-20
追蹤項目:
  - PM-10
---

# DoSelect 懂選｜完整系統規格書 v1.0

## 0. 文件控制

| 項目 | 內容 |
|---|---|
| 專案名稱 | `DoSelect 懂選` |
| 商業標語 | `說出需求，組出適合你的電腦。` |
| 規格版本 | `v1.0` |
| 基線日期 | `2026-08-14` |
| 適用交付 | 第一版 M 功能、桌面 Web、現場展示 |
| 核准狀態 | 已確認的 v1.0 單檔快照；2026-08-20 起不再回填後續變更 |
| Readiness | `READY`；規格可供開發，程式與資料庫實體產物尚待建立 |
| 文件負責人 | alex |
| 變更紀錄 | [[05-規劃/03-需求與決策治理/決策紀錄]] |
| 需求追蹤 | [[05-規劃/03-需求與決策治理/需求追蹤矩陣]] |
| 實作待辦 | [[05-規劃/01-時程與進度/未完成項目追蹤表]] |

### 0.1 文件目的

本文件是 DoSelect v1.0 的完整單檔規格快照，供離線閱讀、交付及追溯 2026-08-20 當時的整合基線。內容涵蓋產品需求、商業規則、角色權限、UI、API、資料、安全、AI、非功能需求及完成門檻。

本快照不再隨後續決策更新。現行開發與 Review 必須從 [[00-專案概述/系統規格書總覽]] 進入最新詳細規格；本文件只在沒有後續覆寫時保留 v1.0 當時的規範意義。Endpoint、DTO、錯誤碼及逐表欄位仍由第 18 章連結的正式附錄維護。

### 0.2 規格效力

發生衝突時依下列順序處理：

1. 已由組長確認並寫入 [[05-規劃/03-需求與決策治理/決策紀錄]] 的後續覆寫決策。
2. [[00-專案概述/系統規格書總覽]] 指定的現行需求與架構文件。
3. [[05-規劃/03-需求與決策治理/需求追蹤矩陣]] 的需求、使用案例、API、資料及測試回連。
4. 已寫回的歷史決策快照只供追溯，不得覆蓋後續決策。
5. 互動中表單、外部原型、聊天內容及程式碼中的臨時行為不構成正式規格。

實作發現矛盾時必須先提出規格變更，不得由開發者自行選擇規則後直接寫入程式。

## 1. 專案定位

### 1.1 商業與展示情境

- 專案型態：台灣單一商家 B2C 電腦及周邊電商。
- 主要客群：知道預算與用途、但不熟悉硬體規格的電腦組裝新手。
- 次要客群：專業創作者。
- 商品：電腦零件、周邊、品牌套裝電腦及自由組裝電腦。
- 幣別：新台幣。
- 團隊：五人，每人每週約 30 小時，總期程約 40 天，包含開發、文件、測試、簡報及彩排。
- 展示：15～20 分鐘，由團隊在單一 Windows 電腦操作；不要求正式公網部署。

### 1.2 問題與價值主張

使用者常能說明預算及用途，卻無法把需求轉成 CPU、GPU、主機板、記憶體、電源與尺寸等專業規格，必須花費大量時間研究，仍可能買到不相容或不合適的商品。

> 讓只知道預算與用途、卻不熟悉硬體規格的消費者，透過自然語言描述需求，快速獲得可解釋、彼此相容且有庫存的電腦與零件組合，降低選購時間與錯配風險。

### 1.3 成功結果

1. 訪客能以自然語言描述預算與用途。
2. 系統只推薦已上架、有庫存且通過後端規則的商品或組合，並解釋理由。
3. 不相容或資料不足的完整組裝不能被宣稱為可相容結帳。
4. 訪客能完成購物車、優惠券、配送、模擬付款及訂單建立。
5. 建單、付款逾時、出貨及取消能正確維護庫存保留。
6. 會員在同意後能用 AI 客服查詢自己的去識別化訂單；拒絕、失敗或低信心時轉人工客服。
7. 後台能處理商品、庫存、訂單、物流、退貨、退款、客服與報表。
8. OpenAI 不可用時，一般電商與人工客服案件仍可運作。

## 2. 範圍與優先級

### 2.1 優先級

| 等級 | 定義 |
|---|---|
| M | v1.0 必須完成、測試並可展示 |
| S | 所有 M 可建置、空白資料庫可由 Migration 重建、API 契約穩定且核心 E2E 通過後才能開始 |
| O | 本期不實作，只保留未來規劃 |

### 2.2 M 功能

- Email 會員註冊、驗證、登入／登出、忘記密碼與重設密碼。
- 管理員登入與 TOTP 2FA。
- 訪客結帳、單筆訂單 Email 驗證、訂單查詢、取消及售後入口。
- 商品、SKU、品牌、分類、標籤、圖片、動態規格、上下架、售價與特價。
- 商品批次上下架、批次調價、XLSX／CSV 匯入、預覽、原子提交及匯出。
- 一般商品搜尋、篩選與排序。
- 購物車、登入合併、價格／庫存／相容性重驗。
- 固定金額、百分比及免運優惠券。
- 多 SKU 訂單、交易快照、狀態歷程及模擬發票／折讓。
- 信用卡、ATM、超商代碼、COD、LINE Pay、Apple Pay、Google Pay 模擬流程。
- 單一倉庫、庫存保留、逾時取消、人工釋放、出貨扣庫及核對。
- 一般宅配、超商取貨、門市、運費、包裹限制版本及批次出貨。
- 單項退貨、附件、人工審核、檢驗、退貨物流及部分退款。
- 人工客服案件、附件、共用佇列、自領、指派、轉派、四級 SLA 與統一案件工作台。
- 七個一般與進階營運報表。
- 自由組裝、保存、分享、多台組裝及每台獨立組裝工作。
- 十三項確定性相容性檢查及相容性規則後台。
- 對訪客及會員開放的 AI 商品搜尋與可解釋推薦。
- 只對登入會員開放、需明確同意、唯讀且可轉人工的 AI 客服。

### 2.3 S 功能

- S-01 收藏。
- S-02 商品評價與後台審核。
- S-03 檢舉。
- S-04 前台繁中、日文與韓文完整支援。
- S-05 AI 客服案件摘要。
- S-06 AI 報表分析。
- S-07 消費者前台手機與平板 RWD。

### 2.4 O 與明確排除

- O：自然語言詢問營運資料。
- 不做多賣家、抽成、分潤及商家提款。
- 不串接真實金流、物流或 SMS。
- 不做手機登入、缺貨預購、Docker、正式公網部署及後台手機版。
- 不允許自然語言產生／執行 SQL。
- AI 不得取消訂單、申請退貨、退款或修改資料。
- M 階段前後台只驗收桌面寬度；消費者前台 RWD 屬 S。

## 3. 參與者、角色與授權

### 3.1 前台參與者

| 參與者 | 允許範圍 |
|---|---|
| 匿名訪客 | 商品瀏覽、一般／AI 搜尋、訪客購物車、自由組裝草稿及訪客結帳 |
| 已驗證訪客購買者 | 以 30 分鐘限單權杖查看、取消符合條件訂單、查看物流、申請單項退貨及查看退款 |
| 會員 | 維護本人資料與地址、會員結帳、本人訂單、通知、組裝清單、AI／人工客服 |
| 已購買會員 | 會員能力；S 啟用後可評價已完成訂單中的商品 |
| 停權會員 | Session 立即撤銷，只保留匿名瀏覽；既有訂單改以訪客驗證或管理協助 |

訪客不能收藏、評價、使用 AI 客服或建立人工客服案件。所有資源所有權由 API 驗證，前端傳入的 Member PublicId 不得作為授權依據。

### 3.2 後台角色

| 角色 | 主要責任 |
|---|---|
| `SuperAdmin` | 全域管理、角色、安全設定與緊急操作；仍不能繞過狀態、金額、所有權及稽核 |
| `CatalogManager` | 商品、SKU、分類、品牌、規格、圖片、上下架、匯入 |
| `InventoryManager` | 庫存、保留、手動釋放、異動及低庫存 |
| `OrderManager` | 訂單、取消、物流、出貨、退貨收件／檢驗及包裹設定 |
| `FinanceManager` | 付款、退款、優惠券及財務／成本報表 |
| `CustomerService` | 客服處理；S 啟用後含評價審核與檢舉 |
| `CustomerServiceSupervisor` | 客服權限、指派／轉派、優先級覆核、SLA 升級及客服報表 |
| `MarketingAnalyst` | 優惠活動、優惠券及一般營運報表 |
| `PrivacyAdmin` | 完整會員個資查看與匯出；每次使用均需用途及稽核 |
| `SecurityAdmin` | 安全、登入、角色及系統 Audit 查詢；不取得完整個資或商業寫入權 |

- 一位管理員可以擁有多角色，權限採聯集，不設顯式 Deny。
- 管理員必須完成 TOTP；高風險操作另需明確 Policy、合法狀態、RowVersion、理由、二次確認及 Audit。
- 退貨核准與退款執行分離：`Return.Approve` 由 OrderManager／SuperAdmin；`Refund.Execute` 由 FinanceManager／SuperAdmin。
- 完整個資只允許 PrivacyAdmin／SuperAdmin；SecurityAdmin 只讀安全 Audit。
- 精確 View／Modify／Approve／Execute／Masked 權限以 [[01-需求/角色與權限]] 為準。

## 4. 系統架構

### 4.1 邏輯架構

```text
Vue customer-web ─┐
                  ├─ ASP.NET Core 10 Controller Web API
Vue admin-web ────┘              │
                         Application Use Cases
                                  │
                               Domain
                                  ↑
                           Infrastructure
                                  │
                  EF Core / SQL Server 2025
```

- 採前後端分離與模組化單體，第一版不拆微服務。
- Vue 只能透過 API 存取資料，不直接連 SQL Server。
- 依賴方向：`Api → Application → Domain`、`Api → Infrastructure`、`Infrastructure → Application + Domain`；Domain 不依賴外部技術。
- Controller 只處理 HTTP、驗證、授權與 Use Case 協調，不直接堆疊商業規則或大量 DbContext 操作。

### 4.2 Solution 結構

```text
DoSelect.slnx
src/backend/
├─ DoSelect.Api/
├─ DoSelect.Application/
├─ DoSelect.Domain/
└─ DoSelect.Infrastructure/
frontend/
├─ customer-web/
└─ admin-web/
tests/
├─ DoSelect.Domain.Tests/
├─ DoSelect.Application.Tests/
├─ DoSelect.Infrastructure.Tests/
└─ DoSelect.Api.IntegrationTests/
```

### 4.3 技術基線

| 能力 | 基線 |
|---|---|
| Backend | .NET SDK `10.0.302`、ASP.NET Core 10、Controller Web API |
| Data | EF Core、SQL Server 2025 Developer、Windows Authentication |
| Frontend | Vue 3、TypeScript、Vite、PrimeVue |
| State | TanStack Query 管 Server State；Pinia 只管 Session 摘要及 UI 狀態 |
| API Client | OpenAPI 產生 TypeScript 型別與 `openapi-fetch` Client，不可手改產生碼 |
| API 文件 | `Microsoft.AspNetCore.OpenApi`＋Scalar，只在 Development 啟用 |
| Background | Hangfire＋SQL Server |
| Email | `IEmailSender`；展示採 Brevo SMTP |
| AI | OpenAI Responses API，後端 Adapter 與白名單工具 |
| File Scan | `IFileScanner`＋Microsoft Defender；掃描不明時 Fail Closed |
| Logging | Serilog 結構化 JSON、Console、每日 Rolling File |
| Source Control | GitHub、受保護 `main`／`dev`、Squash Merge、合併後刪分支 |

## 5. 會員、驗證與通知規格

### 5.1 帳號與登入

- 採 ASP.NET Core Identity 與 HttpOnly Cookie；會員及管理員使用不同 Cookie Scheme。
- Identity User Store 共用，但 `MemberProfile` 與 `AdminProfile` 互斥；管理員需要購物時使用獨立會員帳號。
- 會員只用 Email 註冊／登入，必須驗證 Email；手機只作聯絡資料。
- 支援忘記密碼及 Email 重設密碼；不做手機登入及 SMS。
- 會員閒置 8 小時、活動可滑動延長，絕對上限 7 天；管理員絕對期限 2 小時且不滑動。
- 登出、停權、密碼變更或管理員 TOTP 設定變更後使既有 Session 失效。

### 5.2 Token、鎖定與生命週期

| 用途 | 期限 |
|---|---:|
| Email 驗證 | 24 小時 |
| 密碼重設 | 1 小時 |
| 訪客訂單驗證碼 | 10 分鐘 |
| Guest Order Access | 30 分鐘 |

- Email 驗證、密碼重設 Token 與 Guest 六位數驗證碼單次使用；驗證後的限單 Guest Access Cookie 可在 30 分鐘內多次使用。所有憑證均可撤銷且不得寫入一般 Log。
- 會員連續失敗 5 次鎖定 15 分鐘；管理員 5 次鎖定 30 分鐘。
- 未驗證會員保存 7 天；沒有必須保留的關聯資料時清除。
- 會員刪除採軟刪除與可移除個資匿名化；交易、付款、物流、退貨、退款及 Audit 不刪除。

### 5.3 通知

- Email 驗證、密碼重設、訂單成立、付款結果、取消、出貨、物流、退貨、退款及客服回覆均需 Email／站內通知。
- 寄送失敗不得回滾商業交易；以 Outbox＋Hangfire 保存待送、成功、失敗及重試狀態。
- 開發環境提供不對外寄送的本機實作；Brevo 憑證以 User Secrets／環境變數保存。

## 6. 商品、搜尋、組裝與相容性

### 6.1 商品與 SKU

- Product 保存共用商品資料；SKU 是可定價、可庫存及可下單單位。非變體商品仍有一個預設 SKU。
- 支援品牌、分類、標籤、圖片排序、動態規格、草稿／上架／下架、售價及特價。
- SKU Code 建立後不可變更；已被引用的 SKU 只能停用／下架，不得實體刪除。
- 同一 SKU 有效特價期間不得重疊。
- SKU 以 `RequiresPrepayment` 明確標示限制品；任一 SKU 為 true 或含組裝電腦時，Checkout 不提供 COD。
- 搜尋只回公開可售資料；不同篩選群組用 AND，同一多選欄位用 OR；未知欄位、排序或運算拒絕。
- 有關鍵字依相關度，無關鍵字依近期銷售熱度；同值以時間及不可變 SKU Code 穩定排序。

### 6.2 Excel／CSV

- M 支援批次上下架、調價、匯出及 XLSX／CSV 建立／更新。
- Product、SKU、Specification 為三個邏輯資料集；CSV 為三檔共同預覽與原子提交。
- 分類、品牌、規格語意鍵只能引用既有 Lookup；不得隱式新增。
- 預覽保存 24 小時，顯示新增／更新／無變更／錯誤及逐列穩定錯誤碼。
- 任一列失敗整批回滾；商品匯入不得直接調庫存。
- ImportBatch 固定保存建立者、三組來源檔 Hash／安全顯示名、RowCount、結果 JSON、正規化內容版本與 CorrelationId；商品使用三組來源，庫存只用第一組。不得保留單一 ContentHash 第二版 Schema。
- CSV 固定 UTF-8 BOM、逗號、英文 Header、ISO 8601、`.` 小數；Null 為 `\N`，空欄為空字串。

### 6.3 組裝清單

- 訪客以瀏覽器本地 Adapter 保存草稿；會員可保存至伺服器及跨裝置使用。
- 分享連結不可預測、唯讀、預設不過期且可撤銷；不得揭露建立者資訊。
- 開啟、分享、複製或加入購物車時重新驗證價格、庫存、上下架及相容性。
- 每台收 NT$300 組裝費；同一清單可購買多台，每台建立獨立 AssemblyJob。
- 任一必要 SKU 的總庫存不足時，整筆建單失敗，不部分保留。

### 6.4 確定性相容性

後端至少檢查：CPU／主機板 Socket、晶片組與世代、BIOS 警告、DDR 世代、記憶體插槽及容量、主機板／機殼尺寸、GPU 長度、散熱器 Socket／高度、儲存介面、PSU 瓦數及必要接頭。

- PSU 建議值為結構化功耗加總＋30% 餘裕後向上取 `450/550/650/750/850/1000/1200/1500W` 級距，且不得低於 GPU 原廠建議。
- 核心功耗資料不足時回 `InsufficientData`，不得宣稱整機相容或一鍵加入整套購物車；單獨 SKU 仍可購買。
- AI 只能解釋規則結果，不能更改、補造或取代結果。
- 規則程式由開發者維護；管理員可查看、在安全範圍調警告門檻、測試及依 Policy 啟停，不可修改比較式或語意鍵。

## 7. 購物車、優惠券、訂單、付款、庫存與物流

### 7.1 購物車與合併

- 訪客與會員可使用；購物車不保留庫存，價格只供預覽。
- 開啟、修改及結帳前重驗商品狀態、價格、庫存及相容性。
- 登入合併時一般 SKU 數量相加；組裝群組保持獨立。
- 超量、缺貨、下架、價格改變或相容性失效時保留項目並要求處理，不自動截斷或靜默刪除。
- 合併具冪等性。

### 7.2 優惠券與金額

- 一張訂單最多一張券，不可疊加；支援固定金額、百分比及免運。
- 百分比券必須有最高折抵；訪客可用公開碼，會員券需登入。
- 計算順序：原價 → 特價 → 商品優惠券 → 組裝費 → 配送／滿額免運 → 免運券 → 總額。
- 組裝費不參與商品折扣；只有免運券可折運費。
- 折扣依符合資格商品成交金額比例分攤至明細；`AwayFromZero` 到兩位小數，最後一筆吸收尾差。
- 購物車只預覽優惠，不保留名額；優惠券名額、每人次數、Order 與 CouponRedemption 在同一交易驗證／建立。取消／付款逾時／商家取消返還名額，完成後退貨不返券。
- 優惠券最低消費只比對適用範圍商品小計；購物車不持久化優惠碼，Checkout 必須重新驗證。
- 訪客每人使用鍵為伺服器以 Secret 對正規化訂單 Email 計算的 HMAC-SHA-256，只保存 binary(32) Hash；V1 Secret Version 固定為 1 且 Secret 不進儲存庫。

### 7.3 訂單與快照

- 後端重新計價，不信任前端價格、折扣、運費、組裝費或庫存。
- 訂單保存商品名、SKU Code、規格、單價、成本、折扣、運費、組裝費、收件、門市、政策及 Provider Version 快照。
- 會員地址修改不得回寫歷史訂單。
- 訂單、付款、物流、退款使用分離狀態機，不建立可寫的單一摘要狀態。
- 建立訂單及執行退款要求 Idempotency-Key；同 Key／同 Payload 回原結果，不同 Payload 回 409。

### 7.4 模擬付款

- 即時：信用卡、LINE Pay、Apple Pay、Google Pay；支援成功、失敗、取消及重複回呼。
- 延遲：ATM、超商代碼，產生模擬帳號／代碼與到期時間。
- COD：一般宅配與超取皆可使用；最終應付金額上限 NT$20,000，組裝電腦及任一 `RequiresPrepayment` SKU 不得使用。跨模組判斷使用 Terry 提供的用途專用 Eligibility Query／DTO，不直讀 Repository。
- 一般宅配 COD 於 `Delivered` 且完成收款時 Paid；超取 COD 於 `PickedUp` 且完成收款時 Paid。
- 一次付款失敗不立即取消訂單；可在原付款期限內重試，期限到期才取消並釋放庫存。

### 7.5 庫存

```text
AvailableQuantity = OnHandQuantity - ReservedQuantity
```

- Cart 不保留庫存。Checkout 在同一 SQL Transaction 先建立 Order，再以 OrderId 原子建立 Reservation 並更新 Balance；Reservation 不保存 CartId。
- 建立訂單時以 SQL Server 交易原子保留全部 SKU；最後一件競爭只允許一筆成功。
- 付款成功不扣 OnHand；取消／逾時釋放 Reserved；出貨同時減少 OnHand 與 Reserved；完成不再改實體庫存。
- 保留期限：即時付款 15 分鐘；ATM／超商代碼 3 天；COD 建立後直接確認但仍只保留至出貨。
- InventoryMovement 是異動真實來源；InventoryBalance 是同交易維護的衍生餘額，每日核對，不靜默修正。
- 手動釋放只允許 Active Reservation，需二次確認、理由、冪等與 Audit。

### 7.6 配送

| 配送 | 運費 | 免運門檻 | 主要限制 |
|---|---:|---:|---|
| 超商取貨 | NT$60 | NT$2,000 | 單邊 45cm、三邊和 105cm、5kg |
| 一般宅配 | NT$150 | NT$5,000 | 符合限制可 COD |
| 組裝電腦宅配 | NT$300 | NT$30,000 | 不可超取、不可 COD、必須先付款 |

- 一張訂單一種配送方式及一張主要物流單；不符時結帳前拆單。
- Demo 使用 7-ELEVEN／全家各 50 間虛構門市並明示非官方即時資料。
- 包裹限制使用不可覆寫版本；新版本不回寫既有訂單。
- 批次出貨最多 100 筆，每筆獨立驗證及交易；一筆失敗不回滾其他成功項目。

## 8. 取消、退貨、退款與模擬發票

### 8.1 訂單取消

- 不建立 CancellationRequest；符合條件時直接取消，不符合則引導售後。
- 已付款取消建立退款；已出貨改走退貨／配送退回。
- 例外人工取消需合法角色、原因及 Audit。

### 8.2 退貨政策

- 一般商品於到貨翌日起 7 日內申請；瑕疵、寄錯、運損及保固另走對應流程。
- 現貨零件不得只因品類或拆封一律拒退；必要檢查可退，安裝、使用、缺件、啟用或損壞交人工檢驗。
- 客製組裝在 AssemblyStarted 後不接受無理由取消／退貨，但瑕疵、規格錯誤或組裝錯誤仍須處理。
- 核准後 7 日內交寄；主管可在到期前延長一次 7 日並留下理由及 Audit。
- 訪客經限單 Email 驗證後可取消及申請退貨，不需客服帳號。
- 退貨寄回使用 Kafen 所有的獨立 ReturnShipment／Event，不重用 outbound Shipment；每案同時最多一個有效寄回批次，事件以 Source＋ExternalEventId 去重。
- 宅配取件地址嵌入不可變快照且可不同於原訂單地址；ReturnRequest 不建立 SupportTicketId，補件、附件、審核與通知均留在退貨流程。

### 8.3 部分退款

- 一張訂單可多次部分退款，累計不得超過可退款餘額。
- 退款使用原訂單快照及折扣分攤，不以目前商品、優惠券或政策重算；`OrderCoupon` 保存最低消費門檻，`OrderItem` 保存優惠適用旗標，扣回金額以正值 Allocation Type 表達方向。
- 部分退貨原運費不退；若原免運資格失效，從退款重新收取原配送方式運費。
- 組裝未開始、商家取消或服務瑕疵時退組裝費；正常完成後只退單一零件不退組裝費。
- 綁定贈品或門檻失效時需退回；缺少／損壞交人工審核。
- 只有檢驗為 Resellable 的商品可建立 ReturnToStock；退款成功不自動推定回補庫存。

### 8.4 模擬發票

- 線上付款成功後開立；COD 收款完成後開立；未付款或取消不開立。
- 退款成功依原發票建立模擬折讓，不刪除或覆寫原發票。
- 固定採 5% 稅率與 TWD 整數元；成交總額視為含稅，`Net = Round(Gross / 1.05, 0, AwayFromZero)`、`Tax = Gross - Net`，最後一筆明細吸收尾差。含稅 1,000 固定拆為未稅 952、稅額 48。
- 前台提供本人／有效訪客限單 Scope 發票查詢；後台由 FinanceManager／SuperAdmin 查詢、開立、作廢及依成功退款建立折讓，使用正式 Endpoint、DTO 與五個發票 409 錯誤碼。
- 所有畫面與匯出明示為 DEMO／模擬資料。

## 9. 客服、SLA、案件工作台與附件

### 9.1 客服案件

- 只有會員可建立及使用客服；可關聯本人訂單。
- 七類：訂單、付款、物流、商品與保固、退貨協助、帳號、其他。
- 狀態：Open、Assigned、InProgress、WaitingForCustomer、WaitingForInternal、Resolved、Closed、Cancelled。
- Resolved 3 天未重開自動 Closed；Closed 不可重開，需建立關聯新案件。
- 未指派案件進共用佇列；客服可自領，主管可指派／轉派；全部保存歷程。

### 9.2 優先級與 SLA

| 優先級 | 首次人工回覆 | 目標結案 |
|---|---:|---:|
| Low | 24 小時 | 5 天 |
| Normal | 8 小時 | 3 天 |
| High | 4 小時 | 24 小時 |
| Urgent | 1 小時 | 8 小時 |

- 採 24×7 UTC 計時；AI 回答與內部備註不算首次人工回覆。
- WaitingForCustomer 最多暫停 72 小時；WaitingForInternal 不暫停。
- 80% 通知承辦人；100% 通知承辦人與主管、標示 Overdue 並置頂，但不自動轉派或改級。
- 會員不能選優先級；人工調整需理由並可由主管覆核。

### 9.3 三領域工作台

- SupportTicket、ReportCase、ReturnRequest 為獨立 Aggregate、資料表及狀態機。
- 統一工作台只用授權後的唯讀 `UNION ALL` View 顯示摘要及導向，不建立第四個可寫案件表。
- 寫入仍呼叫原領域 Use Case；工作台可見不代表取得跨領域處理權。
- 工作台 V1 只回 CaseType、CasePublicId、CaseNumber、Title、Status、Priority、RequesterDisplay、AssigneePublicId、CreatedAtUtc、LastActivityAtUtc、SlaDueAtUtc、IsOverdue，不回 RowVersion／CustomerReplyState／另一套 AssignmentState。
- V1 Title 採受控代碼：客服使用 Category、退貨與檢舉使用 ReasonCode；不得直接投影可能含個資的 Subject／Description。RequesterDisplay 只回 `會員`／`訪客`，不回姓名或內部 UserId。
- ReturnRequest 正式保存 Low／Normal／High／Urgent 四級 Priority，建立預設 Normal，後台以具名操作調整；Support 的有效 SLA 依首回／結案階段及最多 72 小時 WaitingForCustomer 暫停計算，Return／Report 的 SLA 欄位為 Null。
- 檢舉固定 `Open → Assigned → InReview → Actioned/Rejected → Closed`；補件是 InReview Action。一般檢舉由 CustomerService 自領，高風險案件須 CustomerServiceSupervisor 指派或覆核，不新增角色。

### 9.4 附件

- 客服、檢舉、退貨各領域每案最多 3 個附件，每個 10 MB；允許 PNG、JPG、PDF。
- 檔案置於網站根目錄外私有目錄，只能透過授權 API 下載。
- 驗證副檔名、MIME、檔案簽章並以 Defender 掃描；掃描不可用／不明即拒絕。
- 結案後保存 180 天；未關聯暫存檔 24 小時清理；Legal Hold 期間不得刪除。

## 10. AI 規格

### 10.1 共通邊界

- AI 是亮點而非基本交易依賴；預算基準 US$100。
- OpenAI 不直接連 SQL Server，不取得 Entity，不執行寫入工具。
- Prompt、JSON Schema 與工具契約版本化且不可覆寫；Interaction 保存模型與版本。
- 禁止傳送姓名、Email、電話、地址、密碼、Token、Secret 及其他顧客資料。

### 10.2 AI 商品搜尋

1. AI 以 Structured Output 將自然語言轉為白名單業務條件。
2. 後端驗證預算、用途、品牌、規格及既有零件；必要資料不足時補問，不猜測。
3. SQL Server 查詢已上架、有庫存的候選 SKU。
4. 後端確定性相容性及排序處理候選。
5. AI 只根據已驗證結果生成推薦理由、預算說明及限制。

- 第一版不使用 Embedding／向量資料庫。
- 搜尋逾時 8 秒；暫時錯誤最多重試一次，Schema 最多修復一次；失敗轉一般搜尋。
- 訪客每日 10 次（IP Hash＋Browser ID）；會員每日 30 次。

### 10.3 AI 客服

- 只對登入會員開放；呼叫前取得外部 AI 處理同意，不同意直接轉人工。
- 會員識別由後端 Session 取得；工具再次驗證本人訂單及用途。
- 只讀工具提供本人去識別化訂單摘要、FAQ、退換貨政策及核准商品資料。
- 非串流回傳結構化答案、引用、免責及轉人工資訊。
- 逾時 12 秒；暫時錯誤最多重試一次、格式最多修復一次；失敗或低信心轉人工。
- 每會員每日 20 則訊息。

### 10.4 保存與成本

| 資料 | 保存 |
|---|---:|
| 已結客服原始對話 | 180 天 |
| OpenAI 原始請求／回答 | 90 天 |
| 去識別化使用統計 | 1 年 |
| Audit | 1 年 |

- 記錄功能、使用者類型、模型、Token、估算成本、成功／失敗及降級，不保存不必要個資。
- AI 用量頁限 SuperAdmin、FinanceManager、CustomerServiceSupervisor、MarketingAnalyst；成本金額只限 Finance／SuperAdmin。累計估算成本首次跨越 US$70 時，透過 Outbox 對設定中的唯一 Active SuperAdmin 建立一次 Email 與站內通知；設定或角色失效時 Fail Closed。US$90 停用非 Demo AI 流量，Demo Allowlist 不繞過登入、同意、授權或遮蔽。
- 商品搜尋與摘要使用 `gpt-5.6-luna`，客服使用 `gpt-5.6-terra`，統一走 Responses API Adapter。

## 11. 報表與展示資料

### 11.1 七個 M 報表

| Key | 報表 |
|---|---|
| `sales-overview` | 銷售總覽 |
| `product-abc` | 商品排行與 ABC 分級 |
| `period-comparison` | 同期比較 |
| `inventory-turnover` | 庫存周轉分析 |
| `gross-margin` | 毛利分析 |
| `product-associations` | 關聯組合分析 |
| `forecast-anomalies` | 預測與異常偵測 |

- 未知 Key 回 `400 report_key_invalid`。
- 第一版即時查正式交易資料並建立必要索引；10,000 筆下優化後仍超過 P95 3 秒才另決策反正規化。
- 歷史毛利使用 OrderItem 成本快照。
- COD 宅配以 Delivered＋收款、超取以 PickedUp＋收款認列；成功退款在退款成功日列負值，不回寫原銷售月。
- 分母為 0 顯示 `—`，不得顯示假 0%。
- ABC：SKU 淨營收累計 80%／95% 分 A／B／C。
- 關聯：共同訂單至少 5，Support≥1%、Confidence≥20%、Lift>1。
- 預測：近 30 天線性迴歸預測 7 天；資料少於 14 天不預測；異常採 `|z|>2`。

### 11.2 匯出

- 報表依目前篩選匯出 CSV／XLSX，最多 100,000 明細列。
- 檔頭保存 Report Key、名稱、時間欄、時區、區間、篩選、產生及資料截至時間。
- CSV 使用 UTF-8 BOM、防公式注入；不得匯出收件個資或 AI 對話全文。

### 11.3 Demo 資料

- 固定產生 10,000 筆主要商業資料，涵蓋最近六個月。
- 至少：完成訂單 250、取消／逾時 100、退款 100、付款失敗／逾時 100、低庫存 SKU 100。
- 使用固定亂數種子與 Seed Manifest；可由腳本重建並驗證筆數、孤兒、非法狀態及負庫存。
- 真實品牌／公開規格需 Source Register；價格、成本、庫存、會員、交易及營運數字全部虛構。
- 圖片需 Asset Register、來源、作者、授權、下載日及 SHA-256；來源不完整不得進 Demo。
- 前台頁尾、後台、報表與匯出均標示畢業專題／DEMO DATA／無品牌背書。

## 12. UI、Web Route 與前端狀態

### 12.1 消費者前台 Route

| 範圍 | Routes |
|---|---|
| 公開與搜尋 | `/`、`/products`、`/products/:productId`、`/ai-search` |
| 組裝 | `/builds/new`、`/builds/:buildId`、`/builds/shared/:shareToken` |
| 驗證 | `/register`、`/verify-email`、`/login`、`/forgot-password`、`/reset-password` |
| 購物與訂單 | `/cart`、`/checkout`、`/orders/:orderId/payment`、`/orders/:orderId` |
| 訪客訂單 | `/guest-orders/access`、`/guest-orders/verify` |
| 售後 | `/orders/:orderId/returns/new`、`/returns/:returnId` |
| 會員 | `/account`、`/account/addresses`、`/account/orders`、`/account/builds`、`/notifications` |
| 客服 | `/support`、`/support/ai`、`/support/tickets`、`/support/tickets/new`、`/support/tickets/:ticketId` |

消費者前台不設 `/shop` 或 `/frontend` 共同前綴。完整 C-01～C-30 頁面責任與 API 對照見 [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]]。

### 12.2 管理後台 Route

| 範圍 | Routes |
|---|---|
| 驗證／首頁 | `/admin/login`、`/admin/totp`、`/admin` |
| 商品 | `/admin/products`、`/admin/products/new`、`/admin/products/:productId`、`/admin/products/import` |
| 型錄／相容性 | `/admin/catalog/lookups`、`/admin/catalog/specifications`、`/admin/catalog/compatibility` |
| 庫存 | `/admin/inventory`、`/admin/inventory/reservations`、`/admin/inventory/imports` |
| 訂單／物流 | `/admin/orders`、`/admin/orders/:orderId`、`/admin/shipping/batches`、`/admin/shipping/stores`、`/admin/shipping/package-limits` |
| 售後 | `/admin/returns`、`/admin/returns/:returnId`、`/admin/refunds`、`/admin/refunds/:refundId` |
| 行銷／案件 | `/admin/coupons`、`/admin/cases`、`/admin/support/sla`、`/admin/support/tickets/:ticketId` |
| 報表／AI | `/admin/reports/:reportKey`、`/admin/ai/usage` |

### 12.3 前端狀態與共同 UX

- TanStack Query 保存 Server State；Query Key 含 Filter、Page／Cursor 及 PublicId。
- Pinia 只保存 Session 摘要、UI 偏好及非敏感跨頁流程；不得複製一般 API Cache。
- 可分享的列表篩選與頁碼寫入 Router Query；Cursor 只存在記憶體。
- 每頁處理 Loading、Empty、Validation、401、403、404、409、429、503 及未預期錯誤。
- 409 concurrency 保留表單草稿並要求重載，不自動覆寫。
- 退款、庫存釋放、取消、發布、指派等高風險命令使用確認對話框；前端確認不取代後端 Policy。
- M 代表寬度 1280px，最低 1024px；360／768 只在 S-07 啟動後驗收。

## 13. API 契約

### 13.1 共通規則

- 第一版前綴 `/api/v1`；管理 API 全部位於 `/api/v1/admin/*`。
- 一般資源用標準 GET／POST／PUT／PATCH／DELETE；狀態轉移及高風險命令用 `POST /.../actions/{action}`。
- 未知 Action 回 `400 validation_failed`；合法 Action 但狀態不允許回對應 `409 *_state_conflict`。
- API 不直接接受或回傳 EF Entity；以穩定 DTO、OpenAPI 及 typed Client 為準。
- 時間用 UTC ISO 8601；TWD 金額 `decimal(18,2)`；比例 `decimal(9,6)`。
- 對外資源使用小寫 UUID v7 PublicId，不暴露 bigint Id。
- 錯誤使用 Problem Details＋穩定 snake_case code；不暴露 Stack Trace、SQL、Token 或內部例外。

### 13.2 分頁

- 一般清單：`pageNumber=1`、`pageSize=20`，最大 100，回 `PageResult<T>`。
- Cursor 只允許：庫存保留、後台訂單、客服 SLA、統一案件工作台、匯入預覽列、報表明細。
- Cursor 綁定篩選、排序與授權，回 `CursorPage<T>`，不承諾 totalCount／totalPages。

### 13.3 瀏覽器安全與併發

- Cookie 跨 Origin 只允許設定白名單並啟用 Credentials。
- 所有寫入要求 Anti-forgery Header；Token 只在記憶體，登入切換後重新取得。
- 一般可編輯資料用 SQL Server rowversion；衝突回 409。
- 訂單、退款、回呼與背景工作具冪等；庫存、優惠券及多 SKU 建單使用交易、條件更新及唯一約束。

### 13.4 契約權威來源

- 完整 Endpoint／Method／Policy：[[03-架構/02-API與前端契約/API Endpoint目錄]]。
- 具名 Request／Response／Query／DTO 欄位：[[03-架構/02-API與前端契約/API DTO與Schema契約]]。
- 完整錯誤碼與 HTTP Status：[[03-架構/02-API與前端契約/API錯誤碼目錄]]。
- OpenAPI Client 流程：[[03-架構/02-API與前端契約/OpenAPI與前端Client流程]]。

## 14. 資料規格

### 14.1 共通資料基線

- 內部 PK 為 `bigint identity`；對外 PublicId 為 Application 產生的 UUID v7＋唯一索引。
- Table／Column 使用 PascalCase；FK 為 `{Entity}Id`。
- UTC 時間 `datetime2(3)`；外部原始偏移必要時 `datetimeoffset(3)`。
- 金額 `decimal(18,2)`、比例 `decimal(9,6)`、數量 `int`。
- FK 預設 Restrict；只有 Aggregate 內無獨立生命週期的 Owned Detail 可 Cascade。
- 訂單、付款、庫存、退款、客服及 Audit 不得 Cascade 刪除。

### 14.2 正規化與反正規化

- 可變交易主資料以 3NF 為基線，關聯及多值資料使用 Entity／Join Table，不用逗號字串或任意 JSON 取代。
- 訂單商品、價格、成本、折扣、地址、運費、政策及門市為不可變交易快照。
- InventoryBalance 是衍生餘額，InventoryMovement／Reservation 是可核對來源。
- 統一案件工作台是唯讀 View，不是第四個寫入模型。
- 報表快照、搜尋索引或預彙總只有在量測不達標且另有同步、重建及防漂移決策後才能加入。

### 14.3 主要 Aggregate

| 領域 | Aggregate／主要資料 |
|---|---|
| 身分 | Identity User、MemberProfile、AdminProfile、Address、RoleAssignment、Notification |
| 商品 | Brand、Category、Product、Sku、ProductImage、SpecificationDefinition／Value、SalePrice |
| 庫存 | InventoryBalance、InventoryMovement、InventoryReservation、ReconciliationCase |
| 組裝 | BuildList、BuildListItem、BuildShareToken、CompatibilityCheckResult、AssemblyJob、AssemblyJobStatusHistory |
| 購物 | Cart、CartItem、Order、OrderItem、GuestOrderAccessRequest／Token、Coupon、CouponCategory／Product／ExcludedProduct、OrderCoupon、CouponRedemption |
| 付款物流 | PaymentAttempt／Event、Shipment、ShippingMethod、ConvenienceStore、PackageLimitVersion |
| 售後 | ReturnRequest／Item／Inspection／Attachment、ReturnShipment／Event、Refund／Allocation、SimulatedInvoice／Allowance |
| 客服 | SupportTicket／Message／Attachment／AssignmentHistory／SlaEvent、ReportCase |
| AI／治理 | AiConsent、Conversation、Interaction、ToolInvocation、Citation、UsageLedger、Outbox、IdempotencyRecord、AuditLog |

實際 Entity、Fluent Mapping、Index、Check Constraint、Filtered Unique 及 Migration 必須逐項比對三份資料字典。Haru、Kafen、Terry、Yinyin 已收束或待覆核的欄位級交付統一由 [[03-架構/09-資料表實作交付/README]] 進入；該交付可用於 Entity／Configuration 實作，但不取代正式資料字典，也不代表 Migration 已核准。

資料責任補充：`SalePrices` 是 SKU 特價唯一可寫來源，第一版不建立重複的 `Promotions.SpecialPrice`；優惠券範圍以正規化關聯表保存。`BuildShareTokens.ExpiresAtUtc` 可為 Null 表示不自動到期；物流 COD 欄位只表達配送能力，最終資格仍由 Application 依金額、組裝及 SKU 預付旗標驗證。`ImportRows` 與 `ImportBatches` 欄位及物流狀態列舉以三份資料字典與狀態機文件為唯一來源。Owner／Assignee 使用 Identity FK；Identity 有交易相依時採停權、軟刪除或匿名化，中央 AuditLog 保存不可變 Actor PublicId／角色快照。

## 15. 安全、隱私、檔案與稽核

- 會員／管理員 Cookie Scheme 隔離，管理員強制 TOTP。
- API 驗證角色、Policy、資源所有權、狀態、金額與庫存；Router Guard 不是授權。
- 所有私人資源都必須以 User A／User B 負面測試證明：其他使用者無法從明細、列表、搜尋、匯出、附件、AI、快取或背景結果讀取、修改或刪除；拒絕後不得有資料或副作用變更。
- 任何 Hard／Soft／附件／批次刪除都必須在伺服器端異動前驗證角色、Actor Scope、資源所有權與業務狀態；前端確認視窗不是授權。
- Request 價格、角色、庫存、折扣、運費及組裝費全部視為不可信。
- Vue 預設轉義；禁止未清理 `v-html`；EF 使用參數化查詢；CSV／XLSX 防公式注入。
- Secret、OpenAI Key、SMTP 憑證、SQL Login 連線及 Token 不得進 Git／Log。
- 新增 Package 必須能由正式 Registry 乾淨 Restore／Install，精確名稱與版本需核對官方來源並提交 Lock／中央版本檔；Commit／PR／Build Artifact 必須通過 Secret 檢查。
- 檔案以私有路徑、簽章／MIME／大小驗證、伺服器檔名及授權下載防止路徑穿越與公開存取。
- Prompt Injection 內容與系統指令隔離；工具後端重新授權；模型沒有寫入工具。
- 高風險 Audit 至少保存 Actor、角色、Action、Entity、Before／After、UTC、IP、TraceId 及結果；敏感值遮蔽。
- 完整個資存取、退款、角色、庫存、出貨及設定操作必須 Audit；Audit 查詢／匯出本身也要 Audit。

## 16. 非功能需求

### 16.1 效能與容量

| 要求 | 門檻 |
|---|---:|
| 展示資料 | 10,000 筆主要商業資料 |
| 一般列表／讀取 | P95 ≤ 1 秒 |
| 非 AI 寫入 | P95 ≤ 2 秒 |
| 七個報表 | P95 ≤ 3 秒 |
| AI 搜尋外部逾時 | 8 秒 |
| AI 客服外部逾時 | 12 秒 |
| 併發驗收 | 20 個同 SKU 結帳、50 讀取、10 後台操作 |
| 前台 Web Vitals | LCP≤2.5s、INP≤200ms、CLS≤0.1 |

### 16.2 可用性與復原

- AI、Email 或背景工作失敗不得拖垮交易主流程；錯誤需可觀測、可重試及可降級。
- `/health/live` 檢查 Process；`/health/ready` 的 v1 目標檢查 SQL Server 與必要依賴。SH-11A 第一階段先驗證本機資料根目錄可寫，SQL／Migration／Hangfire 依 Infrastructure 完成後加入。
- 可由空白資料庫套 Migration、固定 Seed 重建；重設不得覆蓋 Secret。
- 每日完整備份，重大 Migration／Demo 重設前額外備份；RPO 24 小時、RTO 2 小時。

### 16.3 相容性與維護

- M 前後台只要求桌面 ≥1024；前台 RWD S 再驗收 `<768`、`768–1023`、`≥1024`。
- 前台支援 Chrome、Edge、Firefox、Safari 最新及前一版；後台支援 Chrome、Edge 最新及前一版。
- 第一版前後台繁中；日文、韓文及長字串格式為 S。
- Domain＋Application 行覆蓋率 ≥70%；前端核心 Composable／Store ≥60%。
- Rolling Log 最長 14 天、單檔 100 MB、最多 20 檔，將理論上限控制在約 2 GB；一般 Log 只記 PublicId 及遮蔽 IP。

## 17. 測試、驗收與交付閘門

### 17.1 測試分層

| 層級 | 工具 | 證明內容 |
|---|---|---|
| Domain／Application | xUnit | 計算、狀態、規則及邊界 |
| API／SQL 整合 | xUnit＋Mvc.Testing＋專用 SQL Server | 路由、驗證、授權、交易、約束、冪等及併發 |
| Vue | Vitest＋Vue Test Utils | Composable、Store、元件互動及錯誤狀態 |
| E2E | Playwright | 前台、後台、API、資料庫的核心商業流程 |

不以 EF InMemory 取代需要驗證 SQL Server 查詢、約束、交易或併發的測試。

### 17.2 PR 閘門

- .NET Restore／Build，CI 使用 `-warnaserror`。
- `dotnet format DoSelect.slnx --verify-no-changes --no-restore`。
- .NET 單元及受影響 API 整合測試。
- Vue Lint、Typecheck、Test、Build。
- OpenAPI 重新產生 Client 後 Git Diff 必須為空。
- 高風險資料、授權、金額、庫存、退款、AI 隱私及 Migration 變更需附對應測試。

### 17.3 五條核心 E2E

1. 自然語言推薦 → 相容組裝 → 阻擋不相容替換。
2. 訪客多 SKU → 優惠券 → 配送 → 模擬付款 → 訂單與庫存保留。
3. 並行競爭最後庫存，只允許一筆成功。
4. AI 客服只查本人去識別化訂單；拒絕同意、越權或故障時安全降級。
5. 後台出貨扣庫 → 單項退貨 → 部分退款 → 報表反映結果。

### 17.4 Definition of Ready

- M 需求、角色、狀態、API、資料不變量及驗收已確認。
- Endpoint、DTO、錯誤碼及 UI Route 具有權威來源。
- 資料表提案完成模組 Review；跨模組 FK／責任無衝突後才建立正式 Migration。
- Secret、測試資料庫及本機工具準備方式已知。

### 17.5 Definition of Done

- 所屬成功、驗證、授權、空白、衝突、冪等、併發與降級案例完成。
- Build、Lint、Typecheck、測試、Coverage、契約 Diff 與 Review 通過。
- Migration 可由空白 SQL Server 重建且通過安全審查；Seed 可重現。
- Log、Audit、Health、背景工作及失敗復原可驗證。
- [[03-架構/04-安全與檔案/安全與供應鏈強制驗收標準]] 的五項阻擋條件都有證據；新增私人資源具 Actor A／B 測試，新增 Package 具來源與 Restore／Install 證據，提交內容不含 Secret。
- Demo 腳本與備援路徑完成彩排，文件與決策同步。

### 17.6 No-Go

- 未解決的角色／資源越權、個資外洩、重複退款、超賣、金額錯誤或不可恢復 Migration。
- 任何跨使用者讀取／修改／刪除未證明隔離、Package 無法查證或提交內容疑似含真實 Secret。
- API／DTO／資料字典互相衝突而未先更新規格。
- OpenAI 故障會使一般購物、訂單或人工客服案件無法使用。
- Demo Seed 無法重建、核心 E2E 未通過或備份／復原未驗證。

## 18. 開發環境、操作與正式附錄

### 18.1 本機基線

- SQL Server Instance：`.\SQL2025`；Database：`DoSelectDb`。
- Windows Authentication；設定 Key `ConnectionStrings:DefaultConnection`。
- 無密碼範例：`Server=.\SQL2025;Database=DoSelectDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;`
- Node.js 24 LTS＋npm；兩個 Vue 專案提交各自 `package-lock.json`。
- 根目錄預計建立 `global.json`、`.editorconfig`、`Directory.Build.props`、`Directory.Packages.props`。
- 一鍵腳本：`start-all.ps1`、`stop-all.ps1`、`status.ps1`、`reset-demo-data.ps1`、`health-check.ps1`。

### 18.2 v1.0 規範性附錄

以下文件不是參考建議，而是本規格書的詳細契約附錄：

| 類別 | 附錄 |
|---|---|
| 範圍／名詞／商業規則 | [[01-需求/功能範圍]]、[[01-需求/專案名詞表]]、[[01-需求/核心商業規則]] |
| 授權 | [[01-需求/角色與權限]] |
| 領域需求 | `02-領域需求` 下各已確認正式規格 |
| UI | [[03-架構/02-API與前端契約/M功能桌面UI與Route規格]] |
| API | [[03-架構/02-API與前端契約/API共通規範]]、[[03-架構/02-API與前端契約/API Endpoint目錄]]、[[03-架構/02-API與前端契約/API DTO與Schema契約]]、[[03-架構/02-API與前端契約/API錯誤碼目錄]] |
| 狀態／一致性 | [[03-架構/03-資料與一致性/狀態機設計]]、[[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]] |
| 資料 | [[03-架構/03-資料與一致性/資料模型與ERD]]、[[03-架構/03-資料與一致性/資料字典索引]]、三份領域資料字典、[[03-架構/09-資料表實作交付/README]]、[[03-架構/03-資料與一致性/PublicId與資料完整性設計]] |
| AI | [[03-架構/06-AI設計/AI應用詳細設計]]、[[03-架構/06-AI設計/AI測試與評估規格]] |
| 安全／非功能 | [[03-架構/04-安全與檔案/威脅模型與安全檢查表]]、[[03-架構/04-安全與檔案/安全與供應鏈強制驗收標準]]、[[03-架構/01-系統與環境/非功能需求]]、[[03-架構/04-安全與檔案/設定與Secrets管理規範]] |
| 測試／Demo | [[03-架構/08-測試與驗收/測試策略]]、[[03-架構/08-測試與驗收/M功能測試案例目錄]]、[[04-展示/01-Demo與彩排/Demo流程]]、[[04-展示/01-Demo與彩排/Demo操作腳本]] |

## 19. 開發分工與尚待產出

### 19.1 目前分工

- alex：專案／資料庫／API 共用基線、全部 AI、整合、非功能及 Demo 環境。
- haru：會員、驗證、訂單及 S-01 收藏。
- kafen：退貨、客服、檢舉、S-07 RWD 及 PM-02 品牌視覺。
- yinyin：優惠券、付款、退款、發票及付款庫存物流 E2E。
- terry：商品、購物車、庫存、物流、組裝、相容性、M-15 一般與進階營運報表及 S-02 評價審核。
- 待分配：S-04 多語系、DEMO-06 備援影片、DEMO-07 完整彩排。

### 19.2 未完成但不構成規格缺口

- 建立實際 Solution、Vue 專案、設定檔與套件鎖定。
- 組員提交資料表提案，完成 ERD／FK／Index／責任 Review。
- 產生 EF Core Entity、Mapping、Migration、Seed 與資料驗證腳本。
- 實作 OpenAPI、Controller、Application、Domain、Infrastructure 與兩個 Vue App。
- 建立測試、自動化、AI 評估資料集、Brevo 設定、腳本、備份演練、錄影及彩排。

## 20. 規格變更流程

1. 說明變更原因、影響的 M／S／O、資料、安全、API、UI、測試及 Demo。
2. 核心或高影響變更由組長確認；低影響實作基線依既定自動定案原則處理。
3. 同一批更新本文件、所屬詳細附錄及 [[05-規劃/03-需求與決策治理/決策紀錄]]。
4. 影響待辦、狀態、負責人或完成定義時更新 [[05-規劃/01-時程與進度/未完成項目追蹤表]]。
5. 影響 M 追溯鏈時更新 [[05-規劃/03-需求與決策治理/需求追蹤矩陣]]。
6. 覆寫舊決策時保留歷史與覆寫關係，不直接刪除舊快照。

## 21. 修訂紀錄

| 版本 | 日期 | 狀態 | 說明 |
|---|---|---|---|
| `v1.0` | 2026-08-20 | 已確認／FROZEN | 完成文件分類與權威來源收束；本單檔規格自此凍結，後續變更改由系統規格書總覽、正式詳細文件與決策紀錄維護 |
| `v1.0` | 2026-08-19 | 已確認／READY | 寫回 DEC-P271～DEC-P280：優惠門檻基準、Coupon 狀態機、Cart 不持久化優惠碼、SQL Server 查詢測試、付款期限、訂單優惠快照、退款 Allocation 方向，以及模擬發票 Endpoint／錯誤碼／5% 整數元契約；實作由 DES-21／DES-22 追蹤，功能範圍與版本號不變 |
| `v1.0` | 2026-08-18 | 已確認／READY | 寫回 DEC-P263～DEC-P270：Guest Challenge／限流／清理、30 分鐘限單 Cookie、AssemblyJob 獨立歷程、結構化地址、差異化 Lockout 與 Haru DES-20 Review Gate；功能範圍與版本號不變 |
| `v1.0` | 2026-08-17 | 已確認／READY | 寫回 DEC-P250～DEC-P262：Order-only Reservation、完整評價生命週期、ImportBatch 契約、Identity／Audit 邊界、SKU 預付旗標、獨立退貨物流、12 欄工作台、檢舉狀態／權限及 Checkout-bound CouponRedemption；功能範圍與版本號不變 |
| `v1.0` | 2026-08-15 | 已確認／READY | 依 DEC-P243～DEC-P249 收斂分享期限、COD 資料責任、特價唯一來源、ImportRows Schema、優惠券範圍及物流狀態資料契約；功能範圍與版本號不變 |
| `v1.0` | 2026-08-14 | 已確認／READY | 將既有正式需求、架構、UI、API、資料、安全、AI、非功能、測試、Demo 及交付閘門整合為可獨立閱讀的完整系統規格主文件；依 DEC-P73 統一一般宅配與超取 COD 規則 |
