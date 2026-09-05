---
文件狀態: 已確認
最後更新: 2026-08-30
追蹤項目:
  - REQ-02
  - REQ-03
  - AI-02
  - AI-04
  - AI-05
  - AI-08
  - AI-13
---

# AI 搜尋與客服驗收規格

本文件將已確認的 AI 邊界轉成使用案例。模型、Responses API、JSON Schema 型別／上限、工具 DTO、既有零件格式及補問品質門檻均已定案。

## UC-AI-SEARCH-01｜以自然語言解析商品需求

**角色**：匿名訪客、會員

**主流程**：輸入自然語言 → OpenAI 產生符合版本化 Schema 的條件 → 後端驗證 → SQL 查候選 → 確定性規則驗證 → 回傳商品與理由。

**驗收條件**：

- Given 使用者描述用途、預算、規格或品牌偏好，When AI 回傳符合 Schema 的資料，Then 後端仍需驗證型別、允許值與商業限制。
- Given 必要資訊不足，Then 系統回傳澄清問題，不自行猜測後直接查詢。
- Given 自然語言提到既有零件，Then 系統只回傳 `ProposedExistingPart` 與確認提示；使用者確認並補齊必要規格前，不得查詢候選或宣稱相容。
- Given AI 拒絕、輸出截斷、Schema 不合法或業務驗證失敗，Then 不使用該輸出查詢商品。
- Given 任一 AI 條件包含資料庫欄名、任意 SQL 或未允許運算式，Then 後端拒絕。
- Given 同一功能日額已用盡，Then 不呼叫 OpenAI，改用一般搜尋並記錄用量限制結果。

Schema 採小型業務條件且不得暴露資料庫欄位；用途 Enum 與補問規則已確認，其餘完整欄位型別與 Enum 由 `AI-02` 追蹤。搜尋模型使用 `gpt-5.6-luna`。

## UC-AI-SEARCH-02｜只推薦可購買且符合規則的結果

**角色**：匿名訪客、會員

**驗收條件**：

- Given AI 解析出條件，Then 商品候選只能由 SQL Server 的公開、上架、價格與庫存資料取得。
- Given 候選為組裝清單，Then 後端相容性規則必須通過；AI 不能覆寫 `CompatibilityCheckResult`。
- Given Intent 為 `CustomBuild`，Then 成功結果包含 CPU、主機板、記憶體、顯示卡、儲存裝置、電源供應器、機殼及 CPU 散熱器八類完整清單，不得只回傳獨立商品候選。
- Given 使用者帶入既有零件，Then 該零件顯示於完整清單並參與相容性驗證，但不計入本次新購小計；最高預算等於新購零件小計加固定 NT$300 組裝費。
- Given 完整組合為 `Blocked`、`InsufficientData`、缺少必要類別或超出最高預算，Then 不得回傳推薦；`Warning` 可回傳但必須顯示提醒。
- Given 商品缺貨、下架、價格超出正式預算或組裝不相容，Then 不得因 AI 推薦文字而放行。
- Given 有合法結果，Then 每個推薦需說明和用途、預算或偏好的關聯，且理由只能引用後端提供的資料。
- Given 沒有合法結果，Then 提供可解釋的無結果原因、放寬條件建議或一般篩選入口，不虛構商品。

## UC-AI-SEARCH-03｜AI 搜尋故障降級

**角色**：匿名訪客、會員

**驗收條件**：

- Given OpenAI 逾時、限流、服務錯誤或內容不安全，Then 系統依正式策略停止 AI 流程並降級為關鍵字搜尋或補問。
- Given AI 故障，Then 商品瀏覽、一般搜尋、購物車及結帳仍可運作。
- Given 失敗已降級，Then 回應不得暴露 API Key、原始例外、系統 Prompt 或內部 Stack Trace。
- Given 降級發生，Then 記錄功能、使用者類型、錯誤類別、Token／成本估算及降級結果，不記錄不必要個資。

- 商品搜尋的單次意圖模型呼叫最長 5 秒，固定使用 `reasoning.effort: none`、`text.verbosity: low` 與預設 service tier；限流、暫時服務錯誤、逾時或 Schema 格式錯誤均不在同步請求內重試，立即 Fail Closed 並降級為關鍵字搜尋。strict Schema、白名單與後端驗證不得因低推理量而省略。推薦理由只能由後端使用核准候選事實確定性產生，不得再呼叫第二次模型。
- 一般「主機」且沒有用途、效能目標或配／組／組裝字詞時，Intent 應為 `PrebuiltComputer`；明示組裝或提出用途＋預算的整機需求時，Intent 應為 `CustomBuild`。職稱或創作領域含「遊戲」不得單獨推論 Gaming 用途。
- InvalidOutput 的內部評估證據只允許固定失敗原因碼與欄位名稱；不得保存模型 raw output、使用者文字、Secret 或個資，也不得把診斷欄位加入公開 HTTP DTO。

## UC-AI-SUPPORT-01｜取得 AI 處理同意

**角色**：已登入會員

**驗收條件**：

- Given 會員尚未同意目前版本，When 進入 AI 客服，Then 先顯示外部 AI 處理說明與拒絕入口，不先傳送對話。
- Given 會員同意，Then 保存同意版本、時間、會員及用途後才可呼叫 AI。
- Given 會員拒絕，Then 不傳送資料至 OpenAI，直接提供建立人工客服案件的流程。
- Given 未登入訪客或只持有 GuestOrderAccessToken，When 呼叫 AI 客服，Then API 拒絕。
- Given 會員之後撤回同意，Then 後續對話不得再送外部 AI；歷史資料處理依 `AI-12`、`AI-14` 正式規則執行。

## UC-AI-SUPPORT-02｜查詢本人訂單並去識別化

**角色**：已同意 AI 處理的登入會員

**驗收條件**：

- Given 會員要求查詢訂單，Then 後端先以登入身分驗證訂單所有權，不能接受 AI 或前端傳入的會員 ID 作授權依據。
- Given 訂單屬於當前會員，Then 只提供完成問題所需的狀態、商品與去識別化資料。
- Given 訂單屬於其他會員，Then 工具／Use Case 拒絕存取，OpenAI Request 中不得出現該訂單內容。
- Given 資料即將送出，Then 姓名、Email、電話、地址、密碼、Token 與其他祕密均不得包含。
- Given 使用該會員自己的歷史客服對話，Then 只能限當前會員／案件用途，不得成為其他顧客共用知識。

- AI 只能使用白名單只讀 Application 工具；工具從登入內容取得會員身分、再次驗證所有權並回傳去識別化 DTO。

## UC-AI-SUPPORT-03｜禁止 AI 寫入商業資料

**角色**：已登入會員

**驗收條件**：

- Given 使用者要求取消訂單、申請退貨、退款或修改資料，Then AI 只能說明正式流程或導向對應頁面，不得執行動作。
- Given Prompt Injection 要求忽略政策或呼叫未授權工具，Then 系統不提供寫入工具，也不能提升資料範圍。
- Given AI 回答和後端訂單狀態、商品價格、庫存或政策衝突，Then 以前述確定性資料為準並提供人工客服入口。
- Given AI 無法回答、信心不足或使用者要求人工服務，Then 建立／引導人工客服案件，不把 AI 回答當成已完成的人工處理。

## UC-AI-SUPPORT-04｜客服紀錄、用量與成本保護

**角色**：已登入會員、具查看權限的管理員

**驗收條件**：

- Given AI 客服完成一次互動，Then 保存允許保存的對話、用途、模型、Token、估算成本、成功／失敗與降級狀態。
- Given 會員單日 AI 客服達 20 則，Then 後續不再呼叫 AI並提供人工客服入口。
- Given 估算累計成本首次由低於 US$70 跨越門檻，Then 透過 Outbox 對設定中的唯一 Active SuperAdmin 建立一次 Email 與站內通知；Given 收件設定或角色不合法，Then 在呼叫 OpenAI 前 Fail Closed；達 US$90，Then 停用非 Demo Allowlist 的 AI 流量。
- Given AI 服務停用，Then 既有人工客服案件、訂單與基本電商功能仍正常。
- Given OpenAI 原始請求／回答超過 90 天，已結案件原始對話超過 180 天，或去識別化統計超過 1 年，Then 依 `AI-14` 的清除工作處理並記錄結果。

## 後續實作與評估

- OpenAI 模型與 Responses API 已定案；仍需以 120 筆資料集完成成本與品質實測。
- 完整自然語言搜尋 JSON Schema 欄位型別與 Enum 已定於 [[03-架構/06-AI設計/AI應用詳細設計]]。
- AI 客服四個工具、引用欄位及精確 Request／Response DTO 已定於 [[03-架構/06-AI設計/AI應用詳細設計]] 與 [[03-架構/02-API與前端契約/API DTO與Schema契約]]。
- Prompt、Schema、工具版本規則已確認，實作格式詳見 [[03-架構/06-AI設計/AI應用詳細設計]]。
- 個資遮蔽、跨會員、同意、額度預留、最後一額、併發競爭、語系、唯讀工具、Schema、故障與 Prompt Injection 信任分層已有自動化證據。`dev` 已包含 SQL Server Admission Gate、append-only 同意／額度、本人訂單／客服 Owner Query、RequestPublicId 冪等、Guest Cookie 403、Responses Adapter、M-19 完整客服切片，以及 PR #62 合併的 M-18 SearchIntent、既有零件確認閘門、公開 Endpoint、10／30 額度、八類完整 CustomBuild、新購小計＋NT$300 組裝費、正式相容性、推薦理由與降級 UI。公開搜尋 Playwright 目前證明的是故障降級旅程；Live evaluation 仍由 AI-09 獨立追蹤，詳見 [[03-架構/06-AI設計/AI測試與評估規格]]。
