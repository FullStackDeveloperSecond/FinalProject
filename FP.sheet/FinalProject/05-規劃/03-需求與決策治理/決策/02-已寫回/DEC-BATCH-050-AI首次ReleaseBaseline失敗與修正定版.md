---
batch_id: DEC-BATCH-050
status: applied
decision_date: 2026-09-04
decision_ids:
  - DEC-P366
  - DEC-P367
  - DEC-P368
  - DEC-P369
  - DEC-P370
  - DEC-P371
---

# DEC-BATCH-050｜AI 首次 Release baseline 失敗與修正定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P366 | Commit `5e7cc8f2` 的首次三輪 Release baseline 已完成，實際成本 US$0.149338、未達 US$0.50 停止線；Schema 74.75%、Intent 16.67%、Citation 77.78%、Deterministic 28.28%、商品／客服 P95 17,831／3,023 ms，正式結果為 `FAIL`，不得宣稱 AI-09 完成或可發布。 |
| DEC-P367 | Live Runner 定位為 Adapter baseline：商品搜尋只評估 `recommend`／`clarify`，並排除需要相容性、型錄無候選、額度或降級 orchestration 的兩個 Primary Group；客服評估 `answer_with_citations`／`refuse_and_redirect`。被排除案例必須另有 deterministic／orchestration 證據，不得視為自動通過。 |
| DEC-P368 | `refuse_and_redirect` 必須回覆不執行寫入／越權要求的簡短拒絕及安全導引；只有使用核准政策或本人訂單資料時才引用該來源。安全拒絕不得因回傳 `Answered` 被誤判，仍須人工覆核 required points、禁止內容與無副作用。 |
| DEC-P369 | Prompt 版本提升為 `product-search-v2`／`support-v2`，grader 提升為 `deterministic-v1.1.0`；商品 Prompt 明定保留明示預算、不得臆測用途、只有缺少必要資訊才補問。產物必須是真正一行一物件 JSONL，分開保存 intent／explanation 階段狀態與延遲，且 Verdict 必須實際套用 grader 中適用於現有樣本的品質、延遲與成本門檻。 |
| DEC-P370 | 所有 deterministic checks 通過但人工覆核未完成時只能回 `PENDING_HUMAN_REVIEW`；修正版 dry run 不產生成本，任何 OpenAI smoke 或完整 Release baseline 重跑仍須另外核准案例、輪次與成本停止線。 |
| DEC-P371 | Live evaluation 不得只在批次結束後保存資料：呼叫模型前建立不含 Secret 的 `run-metadata.json` 與空 JSONL，之後每個案例／trial 完成立即追加結果並更新 `checkpoint.json` 的累計成本、Token、含 retry 的實際 HTTP 模型請求數、最後案例與狀態；中斷時保留已完成證據，最終 Summary／人工覆核仍在正常收束時產生。 |

## Lowest-Cost Analysis

1. 維持現況：evaluator 會誤判安全拒絕且混用 Adapter／orchestration 分母，不能提供可信 release 證據，未採用。
2. 只補報告：可保存失敗事實，但無法修正錯誤 gate，未採用。
3. 只改資料或設定：無法修正 JSONL、分項摘要與 Runner 判定，未採用。
4. 延伸既有 Prompt、Runner 與 focused tests：不新增套件、服務、Schema 或公開 API，能處理已確認缺陷，採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | AI 商品搜尋與客服使用者、Alex、發布決策者、展示評審 |
| 現況風險 | 真實模型品質不足，且 evaluator confound 會造成錯誤調參與錯誤發布判定 |
| 預期結果 | Adapter 指標只涵蓋可驗證行為、安全拒絕契約自洽、產物可解析且人工 Gate 不被略過 |
| 建置／持續成本 | 沿用既有程式與測試；無新增套件或服務。後續付費重跑另行核准 |
| 信心 | evaluator 修正高；模型品質改善須由新版本付費 smoke／baseline 才能確認 |
| 成功指標 | focused tests、資料驗證及 dry run 通過；22 live／14 另需證據、三輪 96 規劃請求；JSONL 單行可解析，首個外部請求前已有 metadata／checkpoint |
| 停止／回復條件 | 若修正擴及公開 API、資料庫、隱私邊界或需提高外部成本，停止並重新決策 |

## 證據

- `FP.dev/evals/ai/v1/results/2026-09-04-release-baseline-5e7cc8f2.md`
- 原始 Run ID：`20260904T074007Z`
- 修正版不含付費重跑；AI-09 維持進行中。
