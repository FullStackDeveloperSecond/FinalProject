---
type: decision-record
batch_id: AUTO-DEC-012
title: AI 客服階段合併與原子額度契約定版
status: applied
created_at: 2026-08-21
applied_at: 2026-08-21
source: alex 對 DEC-AI-015～017 皆選擇 A
---

# AUTO-DEC-012｜AI 客服階段合併與原子額度契約定版

## 正式決策

1. `DEC-AI-015`：本次以可安全合併的 AI 客服基礎為交付範圍。功能預設關閉；正式同意／額度持久化、訂單 Owner Query、OpenAI Adapter、真正的 GuestOrderAccessToken 整合測試與 E2E 由 AI-13 後續階段完成，不以目前基礎宣稱完整 AI 客服可用。
2. `DEC-AI-016`：額度在所有確定性前置檢查通過、即將呼叫模型前，以單一原子 `TryReserve` 動作預留。未登入、未同意、額度耗盡、敏感內容、資源不屬本人或功能關閉均不消耗；模型逾時、拒絕或服務失敗仍消耗一次；同一次互動的內部重試不得再次扣額度。
3. `DEC-AI-017`：Application 層只回傳領域原因與降級資訊，不持有 HTTP Status。API Controller 統一映射：未同意 `409`、額度耗盡 `429`、敏感內容 `400`、訂單非本人或安全不存在 `404 ai_order_access_denied`、功能／相依服務不可用 `503`。
4. `locale` 必須傳入 Prompt Envelope；`referencedOrderPublicIds` 必須由後端以登入會員做 Owner Query。第一版 Owner Query 尚未接線時，帶訂單參照的請求必須 Fail Closed，不可忽略參照後繼續呼叫模型。

## 最低成本與商業影響

- 直接合併舊版本會留下可競爭超額、參照訂單被靜默忽略及分層 HTTP 契約衝突，不符合安全與資料隔離門檻。
- 本次先擴充既有 Orchestrator 與可替換介面，不新增資料表、套件或外部服務；既有正式資料字典中的 AI 用量持久化仍由後續實作接入。
- 受影響者為 AI 客服使用者、AI Adapter 與訂單 Query 開發者。可量測結果為所有模型呼叫前必須成功預留一次額度；資源越權、功能關閉及前置拒絕的模型呼叫次數與預留次數皆為 0。
- 回復條件：若正式資料來源無法保證 `TryReserve` 的原子性或相同 Request PublicId 的冪等性，AI 功能保持關閉，不得改回讀取後在記憶體扣減。

## 尚未完成

- [ ] 以正式資料表與交易實作 `IAiSupportAdmissionGate`，驗證併發及 Request PublicId 冪等。
- [ ] 以訂單 Owner Query 實作 `IAiSupportContextReader` 並產生去識別資料。
- [ ] 實作 OpenAI Adapter、正式錯誤降級及引用輸出。
- [ ] 以真正的 GuestOrderAccessToken Scheme 補 `403` Integration；目前只有錯誤帳號類型的 Member Scheme 負面測試。
