---
batch_id: DEC-BATCH-033
status: applied
decision_date: 2026-08-28
decision_ids:
  - DEC-P331
  - DEC-P332
  - DEC-P333
  - DEC-P334
---

# DEC-BATCH-033｜OpenAI Responses API Adapter 定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P331 | AI 客服 Adapter 直接以既有 .NET `HttpClient` 呼叫固定的 `POST https://api.openai.com/v1/responses`，實作既有 `IAiSupportModelClient`；不新增 OpenAI NuGet 套件、第二套 AI Client 抽象或可由設定覆寫的外送 Host。 |
| DEC-P332 | Responses Request 固定送出 `store=false`，第一版不送 `previous_response_id`、不啟用 Provider-side 對話狀態，也不在本支客服 Adapter 暴露 Tool。DoSelect 自身的客服與 AI 紀錄仍依 90／180／365 天規則管理；OpenAI Request 是否保存不取代本系統保存與清除責任。 |
| DEC-P333 | AI 客服模型由 `OpenAI:SupportModel` 設定，預設 `gpt-5.6-terra`；單次嘗試逾時預設 12 秒。429、Request Timeout、5xx、網路錯誤、回應截斷或結構不合法最多重試一次；其他 4xx、模型明確要求轉人工或呼叫端取消不重試。所有失敗回到既有人工客服降級，不退款、不重複預留額度。 |
| DEC-P334 | 客服輸出採 strict JSON Schema，答案最多 4,000 字、引用最多 8 筆。引用只能指向本次後端核准的 `order`、`faq`、`return_policy`、`product` 來源，標題與版本由後端可信資料覆寫，模型不得產生任意 URL。成功回應必須同時帶回 Provider 的實際模型名稱與 Input／Output Token；缺少、負值、越界引用或未知欄位均 Fail Closed。成本由後續 M-19 依版本化價格政策計算與保存，本批不硬編可能飄移的價格。 |

## Lowest-Cost Analysis

1. 維持停用 Adapter：無法完成 M-19 真實 Provider 邊界與後續 live 評估，不符合本階段交付，未採用。
2. 只更新文件或以人工腳本呼叫：不能證明正式 DI、逾時、重試、Schema、引用與降級契約，未採用。
3. 重用既有 `IAiSupportModelClient`、安全閘門、Owner Query、`HttpClient` 與 Feature Flag，增加單一 Responses Adapter：不新增套件、服務、資料表或公開 API 即可滿足契約，採用。
4. 新增 OpenAI SDK、通用 Provider Framework、對話狀態服務或向量資料庫：目前沒有多 Provider、伺服器端 Conversation State 或向量檢索需求，會增加依賴與維護面，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用 AI 客服的登入會員、客服人員、負責 AI 成本與隱私的組長 |
| 現況風險 | Fake Client 無法驗證正式 OpenAI 請求、回應、引用及錯誤降級；若直接外送可能洩漏非核准資料或接受幻造來源 |
| 預期可量測結果 | 未通過 Admission／Owner／內容檢查的 OpenAI 呼叫為 0；成功輸出 100% 通過 Schema 與引用白名單；暫時錯誤最多兩次 Provider 呼叫；每次成功保留模型與 Token |
| 建置／持續成本 | 一個既有介面的 Infrastructure Adapter、設定驗證與 deterministic tests；無新增固定服務費，只有功能啟用後的實際 API 用量 |
| 風險成本 | 真實模型品質、P95 與單次成本尚未量測，仍可能不達發布門檻；功能預設關閉並由 AI-09 live baseline 控制 |
| 信心 | 高（傳輸、Schema、引用、重試、取消與完整後端 1,808／1,808）；中（真實模型品質與延遲尚無 live 證據） |
| 成功指標 | Adapter focused tests、Application／API 回歸、SQL Provider-backed 回歸、格式與完整後端測試通過；後續 120 筆 live baseline 達既定品質、P95 與成本門檻 |
| 停止／回復條件 | 發生個資／Secret 外送、越權引用、非暫時錯誤重試、Token 不可追蹤或回歸失敗即停止合併；回復為 `Features:AiEnabled=false` 與原 Fail Closed Client |

## 影響文件

- [[02-領域需求/04-客服與售後/客服與AI功能]]
- [[02-領域需求/90-驗收規格/AI搜尋與客服驗收規格]]
- [[03-架構/04-安全與檔案/設定與Secrets管理規範]]
- [[03-架構/06-AI設計/AI應用詳細設計]]
- [[03-架構/06-AI設計/AI測試與評估規格]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
