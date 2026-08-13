---
文件狀態: 已確認
最後更新: 2026-08-13
追蹤項目:
  - QA-03
  - REQ-02
  - REQ-03
---

# M 功能測試案例目錄

本目錄把 37 個 M 使用案例分配到建議測試層級與固定交叉覆核配對。欄位格式為「主責／覆核」；alex 負責跨模組架構、最終整合、安全與 AI 第二線，不取代各案例主責的實作責任。

## 身分、會員與購物

| 使用案例 | 單元測試重點 | 整合測試重點 | E2E | 負責人 |
|---|---|---|---|---|
| UC-AUTH-01 | 驗證期限與帳號狀態 | 唯一欄位、Token、Email 工作 | 註冊→驗證→登入 | haru／yinyin |
| UC-AUTH-02 | 鎖定計數與狀態 | Cookie、登出、鎖定、停權 | 登入與鎖定提示 | haru／yinyin |
| UC-AUTH-03 | Token 單次性與失效 | 寄信、重設、工作階段撤銷 | 忘記密碼主流程 | haru／yinyin |
| UC-ADMIN-AUTH-01 | TOTP 驗證規則 | 2FA Cookie 與恢復流程 | 管理員登入 | haru／yinyin；alex 安全覆核 |
| UC-GUEST-ORDER-01 | 存取 Token 期限 | Email＋訂單編號、資源授權 | 訪客查單 | haru／yinyin |
| UC-CART-01 | 價格／庫存重驗結果 | 購物車讀寫與錯誤碼 | 庫存變化提示 | terry／kafen |
| UC-CART-02 | SKU 合併與組裝群組 | 訪客／會員 Cart 交易合併 | 登入後處理衝突 | terry／kafen |
| UC-CHECKOUT-01 | 金額快照與保留計算 | 交易、冪等、最後庫存併發 | 核心 E2E 2、3 | terry／kafen；alex 整合覆核 |
| UC-CHECKOUT-COD-01 | COD 配送限制 | 訂單確認與庫存保留 | COD 超商流程 | terry／kafen |
| UC-PAY-01 | 付款狀態與重試期限 | 重複回呼、逾時 Job | 付款成功／失敗 | yinyin／haru |
| UC-COUPON-01 | 折扣、門檻、分攤 | 併發用券與快照 | 結帳套券 | yinyin／haru |
| UC-RETURN-01 | 可退數量與期限 | 單項退貨狀態、附件、庫存 | 核心 E2E 5 | kafen／terry |
| UC-REFUND-01 | 部分退款分攤 | 審核、冪等、付款退款模擬 | 核心 E2E 5 | yinyin／haru；kafen 提供退貨案例 |

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
| UC-REPORT-01 | 指標公式 | SQL 彙總、退款後數字 | 核心 E2E 5 | kafen／terry；yinyin 覆核退款數字 |

## 完成條件

- 每個 M 使用案例至少有一個成功案例與一個失敗／邊界案例。
- 涉及角色或資源所有權的案例必須有未登入、無角色及跨資源測試。
- 涉及付款、庫存、優惠券、退款或工作指派的案例必須有重複請求或併發測試。
- 外部服務使用 Stub／Fake 驗證成功、逾時、限流、格式錯誤及重複通知；不在一般測試中產生真實付款或出貨。
- 所有外部資源案例驗證 Route／DTO 只使用 PublicId；需寫入副作用的案例驗證 Outbox 或 IdempotencyRecord。
- 程式碼覆蓋率門檻與具名交叉覆核責任均已定；主責不得自行完成最終驗收，測試資料庫與逐案自動化腳本屬後續實作工作。
