---
文件狀態: 已確認
最後更新: 2026-08-20
追蹤項目:
  - QA-03
  - REQ-02
  - REQ-03
  - DES-20
  - DES-21
  - DES-22
  - DES-23
---

# M 功能測試案例目錄

本目錄把 37 個 M 使用案例分配到建議測試層級與固定交叉覆核配對。欄位格式為「主責／覆核」；alex 負責跨模組架構、最終整合、安全與 AI 第二線，不取代各案例主責的實作責任。

## 身分、會員與購物

| 使用案例 | 單元測試重點 | 整合測試重點 | E2E | 負責人 |
|---|---|---|---|---|
| UC-AUTH-01 | 驗證期限與帳號狀態 | 唯一欄位、Token、Email 工作 | 註冊→驗證→登入 | haru／yinyin |
| UC-AUTH-02 | Identity 失敗計數、會員 15 分鐘／管理員 30 分鐘 Lockout | 共用登入服務、Cookie、登出、鎖定、停權 | 登入與鎖定提示 | haru／yinyin |
| UC-AUTH-03 | Email／密碼 Token 單次性與失效 | 寄信、重設、工作階段撤銷 | 忘記密碼主流程 | haru／yinyin |
| UC-ADMIN-AUTH-01 | TOTP 驗證規則 | 2FA Cookie 與恢復流程 | 管理員登入 | haru／yinyin；alex 安全覆核 |
| UC-GUEST-ORDER-01 | Challenge 5／60／3、三 Scope 限流、10／30 分鐘期限與限單授權 | 相同 202、HMAC、重寄、Cookie 多次使用、跨訂單拒絕、30 天清理 | 訪客查單→物流→退貨／退款進度 | haru／yinyin；alex 安全覆核 |
| UC-CART-01 | 價格／庫存重驗結果 | 購物車讀寫與錯誤碼 | 庫存變化提示 | terry／kafen |
| UC-CART-02 | SKU 合併與組裝群組 | 訪客／會員 Cart 交易合併 | 登入後處理衝突 | terry／kafen |
| UC-CHECKOUT-01 | 金額快照與保留計算 | 交易、冪等、最後庫存併發 | 核心 E2E 2、3 | terry／kafen；alex 整合覆核 |
| UC-CHECKOUT-COD-01 | COD 配送限制 | 訂單確認與庫存保留 | COD 超商流程 | terry／kafen |
| UC-PAY-01 | 付款狀態、重試及 `Min(付款方式期限, 訂單期限)` | 重複回呼、逾時 Job、重試不得延長訂單 | 付款成功／失敗 | yinyin／haru |
| UC-COUPON-01 | 適用商品小計門檻、折扣、分攤、狀態轉移 | SQL Server CouponRuleReader、併發用券與最低消費／適用旗標快照 | 結帳套券 | yinyin／haru |
| UC-RETURN-01 | 可退數量與期限 | 單項退貨狀態、附件、庫存 | 核心 E2E 5 | kafen／terry |
| UC-REFUND-01 | 部分退款、門檻重算與正值 Allocation 加減方向 | 審核、冪等、折扣／免運扣回、付款退款模擬 | 核心 E2E 5 | yinyin／haru；kafen 提供退貨案例 |

## 商品、搜尋、組裝與後台

| 使用案例 | 單元測試重點 | 整合測試重點 | E2E | 負責人 |
|---|---|---|---|---|
| UC-SEARCH-01 | 篩選與穩定排序 | SQL 查詢、分頁與白名單 | 搜尋／篩選 | terry／kafen |
| UC-IMPORT-01 | 欄位驗證、`\\N`、NFKC 與差異分類 | 24h Staging、擁有者／Hash、原子提交、錯誤檔 | 後台匯入預覽 | terry／kafen |
| UC-BUILD-01 | 組裝費與快照 | 保存、分享 Token、加入購物車 | 核心 E2E 1 | terry／kafen |
| UC-COMPAT-01 | 六類相容規則 | 規則版本與商品資料 | 核心 E2E 1 | terry／kafen；alex 整合覆核 |
| UC-ADM-PROD-01 | 變體合法性 | 建立交易、稽核、權限 | 建立商品變體 | terry／kafen |
| UC-ADM-PROD-02 | 可修改欄位與價格期間 | RowVersion、唯一鍵、稽核 | 修改 SKU | terry／kafen |
| UC-ADM-INV-01 | 可釋放條件與核對差異 | 庫存異動、Balance 同交易、每日核對、併發、權限 | 手動釋放 | terry／kafen |
| UC-ADM-SHIP-01 | 版本期間驗證 | 唯一有效版本、RowVersion | 發布包裹設定 | terry／kafen |
| UC-ADM-STORE-01 | 門市欄位與狀態 | 搜尋、唯一鍵、稽核 | 維護門市 | terry／kafen |
| UC-ADM-ORDER-01 | 允許操作推導 | 投影、分頁、狀態衝突 | 訂單操作 | haru／yinyin；terry 提供訂單案例 |
| UC-ADM-ORDER-02 | 個資遮蔽策略 | 特定權限、稽核 | 查看收件資料 | haru／yinyin；alex 安全覆核 |
| UC-ADM-SHIP-02 | 逐筆驗證結果 | 部分成功、物流單唯一性 | 批次出貨 | terry／kafen |

## AI、客服與報表

| 使用案例 | 單元／評估重點 | 整合測試重點 | E2E | 負責人 |
|---|---|---|---|---|
| UC-AI-SEARCH-01 | Schema 與意圖評估 | OpenAI Stub、後端驗證 | 核心 E2E 1 | alex 主責；terry 提供商品資料與領域覆核 |
| UC-AI-SEARCH-02 | 候選與理由 Grounding | SQL／相容性／庫存邊界 | 核心 E2E 1 | alex 主責；terry 提供庫存、組裝與相容性案例及覆核 |
| UC-AI-SEARCH-03 | 錯誤分類與重試 | 逾時、限流、格式修復 | AI 降級 | alex 主責；terry、kafen 提供降級案例 |
| UC-AI-SUPPORT-01 | 同意版本規則 | 同意紀錄、拒絕不呼叫 | 核心 E2E 4 | alex 主責；haru 提供登入與同意案例，kafen 領域覆核 |
| UC-AI-SUPPORT-02 | 遮蔽與工具 DTO | 本人訂單、跨會員拒絕 | 核心 E2E 4 | alex 主責；haru 提供本人訂單授權案例，kafen 領域覆核 |
| UC-AI-SUPPORT-03 | 工具白名單 | 無寫入工具、Prompt Injection | 核心 E2E 4 | alex 主責；kafen 提供客服案例與領域覆核 |
| UC-AI-SUPPORT-04 | 額度與成本門檻 | Interaction、清理、停用 | 非 Demo 流量停用 | alex 主責；kafen、terry 提供流量與降級案例 |
| UC-SUPPORT-01 | 狀態、指派、取消 | 佇列、權限、歷程 | 案件處理 | kafen／terry |
| UC-SUPPORT-02 | 檔案數量 400、大小 413、格式 415、惡意內容 422、掃描失敗 503 | Defender Stub、私有下載、`file_*` 契約 | 上傳與拒絕 | kafen／terry |
| UC-SLA-01 | 計時、暫停、重開 | Hangfire 提醒與逾時 | SLA 工作台 | kafen／terry |
| UC-WORKBENCH-01 | 正規化排序 | 三領域 Union、授權、分頁 | 工作台篩選 | kafen／terry |
| UC-REPORT-01 | 指標公式 | SQL 彙總、退款後數字 | 核心 E2E 5 | terry 主責；kafen 交叉覆核，yinyin 覆核退款數字 |

## M 桌面頁面支撐 Endpoint 契約測試

下列項目不是新增商業使用案例，而是讓既有 37 個案例能由完整頁面操作的查詢、明細及命令契約。每列至少建立成功、未登入、越權／跨資源、驗證失敗及指定併發測試。

| 契約群組 | 支撐範圍 | 整合測試重點 | 前端／E2E 證據 |
|---|---|---|---|
| API-M-01 公開型錄 | 商品詳情、分類／品牌／規格篩選選項 | 只回已發布內容、成本與草稿不外洩、404 隱匿 | 商品列表→詳情→選 SKU |
| API-M-02 會員 Session 與個人資料 | Session、基本資料、地址 | Cookie Scheme 隔離、Owner、RowVersion、刪地址不改訂單快照 | 登入後 Header、會員資料與地址維護 |
| API-M-03 站內通知 | 列表、單筆已讀、全部已讀 | 只讀本人通知、命令冪等、白名單導向 | 從通知開啟本人訂單／客服案件 |
| API-M-04 購物車明細 | 加入、改量、移除、重驗與登入合併 | 價格不可由前端指定、RowVersion、庫存／上下架、合併冪等 | 商品→購物車→衝突處理 |
| API-M-05 配送與訂單查詢 | 配送選項、示範門市、會員訂單列表／明細、訪客限單查詢／取消 | 配送限制、Owner／Guest Scope、取消狀態與 RowVersion | 結帳、會員查單、訪客查單 |
| API-M-06 組裝清單生命週期 | 列表、修改、刪除、分享、撤銷、分享讀取與加入購物車 | Owner、分享去識別化、失效統一 404、加入購物車冪等 | 儲存→分享→重驗→加入購物車 |
| API-M-07 後台型錄 | 商品 CRUD、SKU、Lookup、規格範本、批次、匯出、圖片 | Policy、受保護 Semantic Key、不可變 Code、圖片授權與 RowVersion | 商品列表→編輯→圖片→發布／批次 |
| API-M-08 匯入與庫存 | 商品／庫存 Preview、Status、Rows、Errors、Confirm；庫存 Balance／Movement／Reservation | 建立者範圍、24h、Hash、Cursor、原子提交、釋放只一次 | 預覽→錯誤下載／確認；保留釋放 |
| API-M-09 後台訂單物流 | 訂單列表／明細／收件、合法 Action、包裹版本、門市、批次出貨 | Policy、個資用途稽核、Action 白名單、逐筆交易 | 訂單→出貨→CSV 結果 |
| API-M-10 售後、優惠券與發票 | 退貨列表／明細／收貨／檢查／延長／審核、退款查詢／執行、優惠券管理、發票查詢／開立／作廢／折讓 | Return.Approve 與 Refund.Execute 分離；`Coupon.Manage` 僅 FinanceManager／MarketingAnalyst／SuperAdmin，`Invoice.Manage` 僅 FinanceManager／SuperAdmin，皆驗證管理員 TOTP／MFA 正反例；另驗冪等、金額上限、RowVersion、五個發票 409 code、5% 表頭整數元、1,000→952＋48、明細兩位小數、三條核對口徑、最後明細稅額尾差、付款前整數化，以及 Order／Payment／Paid／Invoice 四者一致與不一致快照拒絕 | 退貨審核→部分退款→折讓；優惠券建立→套用；訂單→模擬發票 |
| API-M-11 客服工作台 | 會員案件列表、後台明細、內部備註、自領／指派／轉派／優先級／狀態 | Owner、內部備註隔離；Handle 允許 CS／CSS 且拒絕僅 SA；Supervise 允許 CSS／SA 且拒絕僅 CS；多角色聯集；競爭自領 409 不回承辦人擴充欄位，前端失效並重查明細與佇列；理由、RowVersion 與歷程 | 會員送件→客服承接→回覆／結案 |
| API-M-12 報表與 AI 用量 | 七個 Report Key、匯出、會員／後台 AI 用量 | Report Policy、日期與 Cursor、成本明細權限、匯出公式注入 | 報表篩選／匯出、AI 額度提示 |

## 完成條件

- 每個 M 使用案例至少有一個成功案例與一個失敗／邊界案例。
- 涉及角色或資源所有權的案例必須有未登入、無角色及跨資源測試。
- 涉及付款、庫存、優惠券、退款或工作指派的案例必須有重複請求或併發測試。
- 外部服務使用 Stub／Fake 驗證成功、逾時、限流、格式錯誤及重複通知；不在一般測試中產生真實付款或出貨。
- 所有外部資源案例驗證 Route／DTO 只使用 PublicId；需寫入副作用的案例驗證 Outbox 或 IdempotencyRecord。
- `POST /.../actions/{action}` 必須測試未知 Action 固定回 `400 validation_failed`、合法 Action 的 Policy／狀態／RowVersion，以及不可藉 Action 名稱指定任意下一狀態。
- 程式碼覆蓋率門檻與具名交叉覆核責任均已定；主責不得自行完成最終驗收，測試資料庫與逐案自動化腳本屬後續實作工作。
