---
batch_id: DEC-BATCH-060
status: applied
decision_date: 2026-09-06
decision_ids:
  - DEC-P403
---

# DEC-BATCH-060｜AI 019 容量與偏好評分標準調整

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P403 | `SEARCH-NOVICE-019` 的「8TB 儲存裝置」沒有「至少／以上」語意，因此正式必要規格改為 `STORAGE_CAPACITY_GB eq 8192 GB`。偏好仍以「家庭照片」為概念，但 Grader 先移除 Unicode 非英數字元，再允許模型偏好文字包含該概念；期待與實際偏好數量仍須相同，無關偏好或額外偏好必須失敗。Dataset 升為 `zh-TW-v1.0.5-draft`、Grader 升為 `deterministic-v1.1.4`，覆寫 DEC-P402 對 019 operator 與偏好完全字串比對的部分。歷史 v7 Smoke Verdict 不改寫，025／026 仍為未解決失敗。 |

## Lowest-Cost Analysis

1. 維持 `gte` 與完全字串比對：會把語意合理的 `eq 8TB` 和「用於儲存家庭照片」誤判為失敗，未採用。
2. 只修改報告或人工判定：自動 Gate 仍會重複誤判，不能滿足可重複評估，未採用。
3. 修改單一案例來源並延伸既有 Grader：不新增公開 API、資料表、套件或服務，可保留容量、分類、偏好數量與無關內容的嚴格邊界，採用。
4. 加入模型評分器或新語意服務：既有 deterministic 比對已足夠，會增加成本與變異，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | AI 商品搜尋評估執行者、審查者，以及以 8TB 精確容量搜尋的顧客 |
| 現況風險 | 自動評分把合理顧客輸出視為 Intent 錯誤，造成錯誤阻塞與錯誤改善方向 |
| 可量測結果 | 019 的 `eq 8192GB` 與「用於儲存家庭照片」通過；`gte`、4TB、無關偏好與額外偏好仍失敗 |
| 建置與維護成本 | 延伸現有 Dataset／Grader 與聚焦測試；無外部呼叫或持續費用 |
| 風險／回復 | 語意包含可能過度放寬，因此保留偏好數量相等及負向案例；可由版本化 Dataset／Grader 回復 |
| 信心 | 對 019 deterministic 契約信心高；整體 v7 品質仍受 025／026 阻塞 |

## 驗證與邊界

- RED：舊 `gte`／完全字串標準對 `eq 8192GB`＋「用於儲存家庭照片」為 1 Fail／2 Pass。
- GREEN：019 聚焦測試 5／5 通過，涵蓋正例、舊 `gte`、錯誤容量、無關偏好與額外偏好。
- 120 筆 Dataset 來源同步、Schema 驗證、分組／Split 與隱私掃描通過；沒有 OpenAI 呼叫、Token 或費用。
- 三輪 Release Dry Run 為 36 案、22 live、14 deterministic-only、66 次規劃請求，`AnnotationsApproved=true`、`IsLiveReady=true`。
- 不改公開 API、正式搜尋 DTO、資料庫、Fixture、Prompt 或歷史 Smoke 原始產物。
- 本決策只關閉 019 的評分契約缺口；025／026 修正、新 Smoke 與 Release baseline 仍需獨立處理與授權。

## 影響文件與追蹤

- `FP.dev/evals/ai/v1` 的來源、Dataset、Schema、Manifest、Grader、README 與 Live Runner 測試。
- AI 測試規格、測試策略、安全檢查表、AI-09 追蹤、M 功能矩陣、Alex 交付計畫、決策索引／紀錄與日誌。
