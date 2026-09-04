# DoSelect AI 評估資料集 v1

此目錄保存 AI 商品搜尋與 AI 客服的繁體中文版本化評估資料。資料全部為合成或去識別 fixture，不含正式會員、訂單、客服對話或 Secret。

## 內容

| 檔案 | 責任 |
|---|---|
| `cases-source.mjs` | 120 筆案例的可讀來源與合成 fixture 定義 |
| `dataset.zh-TW.v1.jsonl` | 由來源穩定產生、供 Runner 逐筆讀取的正式資料集 |
| `context-fixtures.v1.json` | 凍結候選、政策、訂單與安全測試前置資料 |
| `eval-case.schema.json` | 單筆案例的 JSON Schema |
| `grader-contract.v1.json` | Hard fail、品質門檻與 deterministic／人工 grader 分工 |
| `manifest.json` | 資料集、模型、Prompt／Schema／Fixture 與 baseline 狀態 |

## 分組與 Split

- 30 筆新手搜尋。
- 20 筆專業創作者搜尋。
- 20 筆相容性、警告、不相容與資料不足。
- 15 筆無結果與故障降級。
- 15 筆客服政策。
- 20 筆本人訂單、同意、越權、個資、Prompt Injection、唯讀及額度案例。

固定 Split 為 72 筆 `development`、36 筆 `release`、12 筆 `challenge`。Challenge 可由團隊與評審檢視，但不得用來調整 Prompt；若調整案例或期待值，必須提升資料集版本並留下原因。

## 本機工作流

在 `FP.dev` 執行：

```powershell
node .\scripts\build-ai-eval-dataset.mjs
node .\scripts\build-ai-eval-dataset.mjs --check
node .\scripts\validate-ai-eval-dataset.mjs
```

第一個指令由可讀來源產生 JSONL 與 fixture；`--check` 確認產物沒有過期；驗證器檢查數量、分布、Split、ID、重複輸入、fixture／候選參照、預算、補問上限、Hard fail、標註責任及常見真實個資／Secret 樣式。

## Live Runner

Runner 預設只做 Dry Run，不讀取或輸出 API Key，也不發出付費請求：

```powershell
dotnet run --project .\tools\DoSelect.AiEvals\DoSelect.AiEvals.csproj -- --project-root . --split release --trials 3
```

Live Runner 是 Adapter baseline：商品搜尋只納入 `recommend`／`clarify`，並排除 `SEARCH-COMPATIBILITY` 與 `SEARCH-NO-RESULT-DEGRADED`；客服納入 `answer_with_citations`／`refuse_and_redirect`。相容性、無候選、額度與降級必須由 deterministic／orchestration 測試提供證據，不得因未進行 live model call 就宣稱通過。現行 Release dry run 為 22 個 live Adapter 案例、14 個另需其他證據的案例、3 輪共規劃 96 次模型請求。

首次設定由目前 Windows 使用者在本機執行；API Key 以隱藏輸入寫入與 API 專案共用的 .NET User Secrets。腳本同時寫入 2026-09-02 已核對的 Luna／Terra Token 單價，但不會啟用網站 AI 或呼叫 OpenAI：

價格與模型能力核對來源：[OpenAI model comparison](https://developers.openai.com/api/docs/models/compare)、[OpenAI API pricing](https://developers.openai.com/api/docs/pricing)。價格若有變動，必須先更新 User Secrets 與本段核對日期再執行新的 baseline。

```powershell
.\scripts\configure-openai-eval-secrets.ps1
```

正式 Release baseline 前先以一筆商品搜尋與一筆客服做單輪煙霧測試：

```powershell
dotnet run --project .\tools\DoSelect.AiEvals\DoSelect.AiEvals.csproj -- `
  --project-root . --split development --trials 1 `
  --case-id SEARCH-NOVICE-001 --case-id SUPPORT-POLICY-001 `
  --execute --stop-after-cost-usd 0.10
```

`--execute` 強制要求正值 `--stop-after-cost-usd`；達到上限後不再開始下一筆，但已送出的單筆請求可能讓結果略高於上限。結果預設寫入被 Git 忽略的 `.run/ai-evals/<UTC timestamp>/`，包含逐次 JSONL、彙總 JSON 與人工覆核 Markdown。只有確認煙霧測試的實際單次成本後，才能另外核准完整 Release baseline 上限。

模型呼叫前，Runner 先寫入不含 Secret 的 `run-metadata.json`、空的逐筆 JSONL 與 `checkpoint.json`；每個案例／trial 完成後立即追加一行結果並更新累計成本、Token、最後案例與狀態。因此程序中斷時仍保留已完成證據。正常收束後才另外產生 Summary 與人工覆核文件。

逐筆產物保證每行一個 JSON 物件，並記錄該案例包含 retry 在內的實際 HTTP 模型請求數；checkpoint 與 Summary 同步累計。Summary 另分開列出 selected／live／deterministic-only 數量、兩個 feature 的執行數、平均成本、平均與 P95 latency，以及待人工覆核數，Verdict 會實際套用 `grader-contract.v1.json` 中適用於現有樣本的品質、延遲與成本門檻。所有 automated thresholds 與 deterministic checks 通過但仍有待人工覆核時，Runner 回傳 `PENDING_HUMAN_REVIEW`，不會自動宣稱 `PASS`。

## 審核與發布邊界

- 商品、創作者與相容性由 Terry 主標；客服、政策與安全由 Kafen 主標；Alex 第二審與發布核准。
- `SUPPORT-POLICY` 修正版已依 [`reviews/SUPPORT-POLICY-v1.0.2-review.md`](reviews/SUPPORT-POLICY-v1.0.2-review.md) 完成 Kafen 主標與 Alex 第二審，15 筆均為 `approved`。
- `zh-TW-v1.0.0-draft` 的 120 筆案例曾完成 Terry／Kafen 主標與 Alex 第二審。2026-09-03 初次煙霧測試及覆核前檢查確認政策 Fixture 不足以支持具體必答點，因此兩個政策快照已補齊，現行版本為 `zh-TW-v1.0.2-draft`／Fixture `v1.0.2`；Kafen 主標與 Alex 第二審完成後，120 筆案例均為 `approved`。
- 2026-09-03 初次煙霧測試執行 2 案例，實際成本 US$0.001880，未達 US$0.10 停止線。商品搜尋因 strict Schema 含 Responses 不支援的 `uniqueItems` 而在產生 Token 前失敗；客服 Schema／引用通過，但人工必答點缺少 Fixture 依據。兩項均已修正，修正版在覆核及 commit 前不得重跑或視為 baseline。
- 2026-09-04 在 Commit `9ea03fc3` 執行第二次煙霧測試：2／2 deterministic 與人工覆核通過，成本 US$0.006085，Input／Output Tokens 3,545／694。商品搜尋單筆 10,083 ms 高於 5 秒目標，只視為待正式 baseline 確認的風險訊號；完整證據見 [`results/2026-09-04-smoke-9ea03fc3.md`](results/2026-09-04-smoke-9ea03fc3.md)。
- 2026-09-04 在 Commit `5e7cc8f2` 執行三輪 Release baseline：33 個舊版 live-eligible 案例共 99 個案例輪次，成本 US$0.149338，結果 `FAIL`。本次同時確認商品模型品質問題與 evaluator scope／安全拒絕契約缺陷；完整分析及修正邊界見 [`results/2026-09-04-release-baseline-5e7cc8f2.md`](results/2026-09-04-release-baseline-5e7cc8f2.md)。修正後必須視為 grader／Prompt 新版本，不得把新舊數字當成單一變因比較。
- AI 客服 Responses Adapter、M-19 與 M-18 搜尋垂直切片均已合併 `dev`；M-18 包含搜尋專用 SearchIntent／推薦理由 strict Adapter、後端白名單候選流程、既有零件確認閘門、降級路徑與 `/ai-search` UI。Live Runner 已建立 Dry Run、明確執行旗標、User Secrets、成本停止線與結果產物；目前正在依首次 Release baseline 報告修正，尚未取得修正版付費 baseline。
- PR／CI 只執行資料產物與 deterministic contract 檢查，不呼叫 OpenAI。
- `DoSelect.Application.Tests`、API Integration 與 SQL Provider-backed tests 固定 AI-13 的隱私、授權、同意、額度預留、最後一額、併發競爭、Owner、語系、唯讀工具、Schema 與降級契約；Responses Adapter tests 另固定 `store=false`、引用、模型／Token、重試與 Fail Closed。這些證據都不取代完整 live model 評估；目前瀏覽器證據只涵蓋既定降級旅程。
- 未來 live runner 必須在呼叫前顯示預估成本，保存模型／Prompt／Schema／Tool／資料集／Grader／Commit 版本，且不得輸出 API Key。
