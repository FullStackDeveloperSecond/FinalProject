---
type: decision-record
batch_id: AUTO-DEC-011
title: AI 客服敏感內容與結果碼定版
status: applied
created_at: 2026-08-21
applied_at: 2026-08-21
source: alex 採用 AI 客服敏感內容回應與 Result Code 建議
---

# AUTO-DEC-011｜AI 客服敏感內容與結果碼定版

## 正式決策

1. AI 客服訊息若含 Token、API Key、Cookie、密碼或禁止外送的個資，後端必須在模型呼叫前停止。
2. 對外回 `400 Bad Request` 與既有 `validation_failed`；只顯示不含原文的安全提示，不指出、記錄或回顯偵測內容。
3. 此錯誤不建立 `AiSupportAnswerDto`，因此不使用成功回應的 `resultCode`。
4. `AiSupportAnswerDto.resultCode` 固定允許 `answered`、`safe_rejection`、`degraded`。
5. `safe_rejection` 只供未來以正常回應呈現、且不含敏感內容的安全拒絕；`degraded` 只能在確實執行既定替代流程時使用，不可用來掩飾未處理錯誤。

## 最低成本與商業影響

- 直接接受並轉送敏感內容不符合既定禁止外送政策與 AI-13 發布門檻。
- 新增專用敏感內容錯誤碼會擴大前後端契約與維護面；既有 `400 validation_failed` 已能安全表達輸入不可處理，因此不新增公開錯誤碼。
- 受影響者為 AI 客服使用者與前端錯誤處理；預期結果是敏感內容不離開後端，前端只需沿用既有驗證錯誤流程。
- 無新服務、套件、Schema 或外部成本；成功指標為敏感案例的 Model Client 呼叫次數為 0，且回應與測試輸出不包含原文。

## 回復條件

- 若 UX 測試證明通用驗證錯誤無法提供可理解的修正方式，可另案新增專用安全錯誤碼；變更前必須同步 API 錯誤碼目錄、前端映射與測試。
- 不論回應碼是否改版，模型前阻擋與不回顯敏感內容的安全條件不可回退。
