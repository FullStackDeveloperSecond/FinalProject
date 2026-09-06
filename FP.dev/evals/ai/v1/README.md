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

Live Runner 是 Adapter baseline：商品搜尋只納入 `recommend`／`clarify`，並排除 `SEARCH-COMPATIBILITY` 與 `SEARCH-NO-RESULT-DEGRADED`；客服納入 `answer_with_citations`／`refuse_and_redirect`。相容性、無候選、額度與降級必須由 deterministic／orchestration 測試提供證據，不得因未進行 live model call 就宣稱通過。現行 Release dry run 為 22 個 live Adapter 案例、14 個另需其他證據的案例、3 輪共規劃 66 次模型請求。

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

目前商品意圖 Prompt 為 `product-search-v11`：沿用單次 Responses 意圖請求、5 秒零同步重試、`reasoning.effort: none` 與 `text.verbosity: low`，只對商品搜尋使用可回復的 `service_tier: fast`；設定只接受 `default` 或 `fast`，無效值 Fail Closed。單一且明確的繁中口語金額可由後端確定性保存為最高預算，最低值高於最高值時保留安全上限並產生衝突補問，模糊約數或範圍不強制覆寫。Prompt 同時避免把儲存情境誤當一般用途、資訊完整時不追問未提及的螢幕／周邊，並以分類對應 Semantic Key 白名單驗證規格；明確的商品標籤可映射對應用途，「遊戲美術」等創作職務仍按創作用途處理。顧客明確指定單一零件／周邊且提供預算與分類時，即使提及現有硬體也維持 `SingleProduct`；模型只提出結構化既有零件，由應用層另行取得確認，不追問整台電腦用途。頂層搜尋條件會移除與 proposal 完全重複的規格，以及「可選的需要／支援／相容前綴 + 完整 proposal 顯示名稱」等值偏好；較長且描述待購商品能力的偏好不會被誤刪。`STORAGE_CAPACITY_GB` 專供儲存容量，TB 在後端以 `1 TB = 1024 GB` 正規化；未含上下界措辭的明確容量採 `eq`，明說 SSD 時保留為硬條件；RAM 仍使用 `MEMORY_*`。明確「配／組／組裝」詞會窄範圍覆寫誤判的 Prebuilt 分類。strict Schema、精確大寫 Semantic Key、白名單與後端驗證仍為內部正確性邊界；顧客可見的推薦理由只使用在地化名稱與自然語言，直接承接用途、預算、硬性規格、軟性偏好及核准 Badge，不顯示 SearchIntent enum、Semantic Key、Fixture ID、品牌／分類代碼或「後端候選」術語。單品屬於固定相容性元件類別時，回答會明示安裝前仍需核對現有設備規格，且商品卡沒有核准證據時不宣稱軟性偏好已被滿足。模型補問中的已知用途 enum 在對外回傳前會以既有多語顯示名稱取代。InvalidOutput 只保存固定原因碼與欄位名稱，不保存 raw output。客服 Prompt 為 `support-v5`：安全與引用邊界維持不變，要求先直接回答，再只補充核准資料支持且與問題相關的條件、期限、費用、例外、不確定性，以及適用的付款、履約管道與資格限制，必要時以顧客下一步收尾；若核准退貨政策允許必要檢查且商品完整仍可退，不得概括成「拆封商品原則上不可退」，並禁止內部代碼、Enum、資料庫欄位、Fixture ID 與實作用語。人工覆核表會標出兩個 Prompt 版本、客服每案實際模型可用的核准來源及核准資料，並明示必要回答重點只供評分、不送入模型；商品案例另外集中列出意圖模型實際取得的分類／品牌／Semantic Key Metadata，逐案列出確實進入推薦階段的後端核准商品事實，且清楚區分後者不屬模型輸入。grader `deterministic-v1.1.8` 除精確核對分類、結構化必要規格、既有零件提案與偏好概念，也以未送入模型的 `requiredFacts` 驗證指定客服政策必要事實；資料集現行版本為 `zh-TW-v1.0.10-draft`，Fixture 為 `v1.0.4`。120 案案例定義已核准，任何新顧客可見輸出仍須重新人工覆核。

逐筆產物保證每行一個 JSON 物件，並記錄該案例包含 retry 在內的實際 HTTP 模型請求數；checkpoint 與 Summary 同步累計。Summary 另分開列出 selected／live／deterministic-only 數量、兩個 feature 的執行數、平均成本、平均與 P95 latency、客服必要事實涵蓋率，以及待人工覆核數；必要事實涵蓋率門檻固定為 100%。Verdict 會實際套用 `grader-contract.v1.json` 中適用於現有樣本的品質、延遲與成本門檻。所有 automated thresholds 與 deterministic checks 通過但仍有待人工覆核時，Runner 回傳 `PENDING_HUMAN_REVIEW`，不會自動宣稱 `PASS`。

## 審核與發布邊界

- 商品、創作者與相容性由 Terry 主標；客服、政策與安全由 Kafen 主標；Alex 第二審與發布核准。
- `SUPPORT-POLICY` 修正版已依 [`reviews/SUPPORT-POLICY-v1.0.2-review.md`](reviews/SUPPORT-POLICY-v1.0.2-review.md) 完成 Kafen 主標與 Alex 第二審，15 筆均為 `approved`。
- `zh-TW-v1.0.0-draft` 的 120 筆案例曾完成 Terry／Kafen 主標與 Alex 第二審。2026-09-03 政策 Fixture 補齊並完成 v1.0.2 覆核。2026-09-04 依失敗 Smoke 證據，v1.0.3 只為 `workstation-3d-70` 新增合成名稱、`GPU 預算優先`與 `64GB RAM`；`SEARCH-CREATOR-008`、`013` 已依 [`reviews/SEARCH-CREATOR-v1.0.3-review.md`](reviews/SEARCH-CREATOR-v1.0.3-review.md) 完成 Terry 主標與 Alex 第二審，120 筆均為 `approved`，完整 Release plan 為 `AnnotationsApproved=true`、`IsLiveReady=true`。
- 2026-09-05 的 Fixture v1.0.4 依既有已核准 `SEARCH-NOVICE-019` 必答點，為 `storage-nas-8tb` 補上顧客可讀名稱、8TB 容量與「單一裝置不等同完整備份」Badge；這是補齊既有案例所需合成證據，不新增產品事實或改變案例預期。
- 2026-09-03 初次煙霧測試執行 2 案例，實際成本 US$0.001880，未達 US$0.10 停止線。商品搜尋因 strict Schema 含 Responses 不支援的 `uniqueItems` 而在產生 Token 前失敗；客服 Schema／引用通過，但人工必答點缺少 Fixture 依據。兩項均已修正，修正版在覆核及 commit 前不得重跑或視為 baseline。
- 2026-09-04 在 Commit `9ea03fc3` 執行第二次煙霧測試：2／2 deterministic 與人工覆核通過，成本 US$0.006085，Input／Output Tokens 3,545／694。商品搜尋單筆 10,083 ms 高於 5 秒目標，只視為待正式 baseline 確認的風險訊號；完整證據見 [`results/2026-09-04-smoke-9ea03fc3.md`](results/2026-09-04-smoke-9ea03fc3.md)。
- 2026-09-04 在 Commit `5e7cc8f2` 執行三輪 Release baseline：33 個舊版 live-eligible 案例共 99 個案例輪次，成本 US$0.149338，結果 `FAIL`。本次同時確認商品模型品質問題與 evaluator scope／安全拒絕契約缺陷；完整分析及修正邊界見 [`results/2026-09-04-release-baseline-5e7cc8f2.md`](results/2026-09-04-release-baseline-5e7cc8f2.md)。修正後必須視為 grader／Prompt 新版本，不得把新舊數字當成單一變因比較。
- Commit `f195c453` 的 6 案修正版 Smoke 已完成正式人工內容複核：6／6 已審、3 Pass／3 Fail，與 automated `FAIL` 一致；結果只描述當時 v2／v1.0.2 Run，不得用來宣稱後續 v4 修正通過。完整報告見 [`results/2026-09-04-remediation-smoke-f195c453.md`](results/2026-09-04-remediation-smoke-f195c453.md)。
- 同日以系統 PowerShell 執行 v4 的 6 案／1 輪 Smoke，實際 6 次 Responses 請求、成本 US$0.006287，Input／Output Tokens 3,003／524，正式結果 `FAIL`。客服 2／2 通過；商品只有 1／4 在 5 秒內完成，商品 P95 5,033 ms，唯一完成案例的確定性理由仍缺品牌偏好／排除必答點。品牌理由已在工作樹修正並通過 12／12 聚焦測試；組長後續定版保留 5 秒並加入 `reasoning.effort: none`、`text.verbosity: low`，payload contract 已納入測試。本批未付費重驗，未執行 66 次 baseline。完整報告見 [`results/2026-09-04-v4-smoke-f195c453.md`](results/2026-09-04-v4-smoke-f195c453.md)。
- DEC-BATCH-054 設定完成後，以系統 PowerShell 執行同一組 6 案／1 輪 Smoke：6 次 Responses 請求、成本 US$0.007224，商品 P95 3,013 ms、4／4 在 5 秒內取得 Provider 結果，證明 Timeout 明顯改善；但只有 3／4 形成可用 SearchIntent，Schema 83.33%、Intent 25%、有效推薦 66.67%，Verdict 仍為 `FAIL`。DEC-BATCH-055 後續完成 3 Pass／3 Fail 正式人工覆核、定版「主機」taxonomy，並將候選 Prompt 升為 v5、安全診斷欄位納入 Runner；尚未付費重驗，未執行 66 次 baseline。完整報告見 [`results/2026-09-04-low-latency-smoke-f195c453.md`](results/2026-09-04-low-latency-smoke-f195c453.md)。
- 2026-09-05 的 v5 固定 6 案 Smoke 為 `FAIL`；後續確認舊商品回答未切合顧客問題且暴露內部術語，歷史結果見 [`results/2026-09-05-v5-smoke-c3494813.md`](results/2026-09-05-v5-smoke-c3494813.md)，DEC-BATCH-057 的顧客視角零成本修正見 [`results/2026-09-05-v6-customer-audience-remediation.md`](results/2026-09-05-v6-customer-audience-remediation.md)。同日於 `dev@eb83ecf6` 執行 v6 固定 6 案／1 輪 Smoke：6 次請求、US$0.007227，商品／客服 P95 4,199／2,126 ms，Schema、推薦、引用與隱私安全均通過；但 Intent 75%、補問精確率 50%、Deterministic 66.67%，正式 Verdict 仍為 `FAIL`。Alex 已完成顧客方向與根因審查，但該輪 Runner 會在模型要求補問時仍虛構推薦階段，且 `SEARCH-NOVICE-019` 的資料集／grader 把 8TB 儲存需求錯用記憶體容量語意，因此舊六案表不作為 v7 通過證據。完整歷史證據見 [`results/2026-09-05-v6-smoke-eb83ecf6.md`](results/2026-09-05-v6-smoke-eb83ecf6.md) 與 [`reviews/2026-09-05-v6-smoke-review.md`](reviews/2026-09-05-v6-smoke-review.md)。
- 依 DEC-BATCH-059 完成 `product-search-v7` 零成本修正：Runner 與正式流程一致，補問／既有零件確認時停止推薦；型錄新增 `STORAGE_CAPACITY_GB`、分類對規格白名單與 TB→GB 正規化；資料集升為 `zh-TW-v1.0.4-draft`、grader 升為 `deterministic-v1.1.3`。兩個失敗案例的回歸、Application、Infrastructure、API、SQL Server、資料驗證與 Solution Build 均通過，未呼叫 OpenAI。結果見 [`results/2026-09-05-v7-zero-cost-remediation.md`](results/2026-09-05-v7-zero-cost-remediation.md)；新 v7 Live Smoke 與 66 次 baseline 仍須另行授權。
- 2026-09-06 依 DEC-BATCH-060 調整 `SEARCH-NOVICE-019`：未帶「至少／以上」的 8TB 採 `eq 8192GB`，偏好允許「用於儲存家庭照片」包含「家庭照片」概念，但期待／實際偏好數量仍須相同。Dataset／Grader 升為 `zh-TW-v1.0.5-draft`／`deterministic-v1.1.4`；019 聚焦 5／5、120 筆同步與驗證通過，未呼叫 OpenAI。歷史 v7 Smoke Verdict 維持 `FAIL`。
- 同日依 DEC-BATCH-061 完成 `product-search-v8` 中文預算確定性保護：025 即使模型漏掉「三萬五」仍保存最高預算並移除純預算補問；026 即使模型回傳 null 仍保留 NT$15,000 安全上限並提出衝突補問；模糊金額不強制判定。Adapter 20／20、Live Evaluation 23／23、Build、Format、120 筆驗證及 66 次 Release dry run 通過，未呼叫 OpenAI。完整零成本證據見 [`results/2026-09-06-v8-budget-guard-remediation.md`](results/2026-09-06-v8-budget-guard-remediation.md)；當時尚待授權的 v8 Live Smoke 後續已由 DEC-BATCH-062 定版為通過。
- 2026-09-06 在 Commit `45eeed27` 執行 `product-search-v8 + support-v2` 固定六案／一輪 Live Smoke：6 次請求、成本 US$0.007117，Schema、Intent、補問、有效推薦、Citation、Privacy／Authorization、deterministic、P95 與平均成本 Gate 全數通過；Alex 完成顧客視角人工覆核 6／6 Pass，正式 Smoke Verdict 為 `PASS`。原始 Runner Summary 的 `PENDING_HUMAN_REVIEW` 保留為自動化階段歷史證據；完整報告見 [`results/2026-09-06-v8-smoke-45eeed27.md`](results/2026-09-06-v8-smoke-45eeed27.md)。66 次 Release baseline 仍須另行決策與費用授權。
- 同日在 `dev@155bafa3` 完成 v8 三輪 Release baseline 後，依原始逐輪證據修正五案：014／021 補正精確規格與期待、015 修正為 `CustomBuild`、020 改以既有零件確認階段評分、023 以通用商品標籤規則補齊 Gaming 用途。Prompt／Dataset／Grader 升為 `product-search-v9`／`zh-TW-v1.0.6-draft`／`deterministic-v1.1.5`，Fixture 維持 `v1.0.4`；development 009 與 challenge 030 只作非 Release 泛化保護。零成本測試、120 筆驗證與 66 次 Dry Run 通過，未呼叫 OpenAI；完整證據見 [`results/2026-09-06-v9-five-case-zero-cost-remediation.md`](results/2026-09-06-v9-five-case-zero-cost-remediation.md)。新版本小型 Live Smoke 與人工覆核仍需另行授權。
- v8 人工分析另發現客服回答缺少必要政策事實時仍可能因 Schema／Citation 正確而自動通過；因此後續將 Dataset／Grader 升為 `zh-TW-v1.0.7-draft`／`deterministic-v1.1.6`，為 `SUPPORT-POLICY-011`、`013` 加入未送入模型的結構化必要事實，Summary 新增 100% 涵蓋率 Gate。同一修正也讓相容性元件單品與無核准 Badge 的軟性偏好採明確證據不足表述，不擴充公開商品 DTO。這批修正未呼叫 OpenAI；新 Live Smoke 與人工覆核仍須另行授權。
- 歷史 v8 三輪 Release baseline 已完成 66／66 顧客視角人工裁定：44 Pass／22 Fail，商品 21／39 Pass、客服 23／27 Pass；Privacy／Authorization／Unsafe action 人工 Hard Fail 為 0。原 automated 與人工 Verdict 均維持 `FAIL`，完整逐筆理由見 [`reviews/2026-09-06-v8-release-baseline-human-review.md`](reviews/2026-09-06-v8-release-baseline-human-review.md)。此覆核只關閉 AI-RC-03，不證明現行 v9／support-v3 Live 品質。
- 2026-09-06 在 `dev@36f43839` 執行 `product-search-v9 + support-v3` 固定七案／一輪 Live Smoke：7 次請求、成本 US$0.012995，未觸及 US$0.02 停止線；自動 Gate 全數通過，Codex 經 alex 授權完成顧客視角人工覆核 7／7 Pass。此 Smoke 覆蓋 v8 失敗的五個商品案例與兩個客服 required-fact 案例，但沒有 Privacy／Authorization 案例；完整 66 次 Release baseline 仍須另行費用授權。正式報告見 [`results/2026-09-06-v9-support-v3-smoke-36f43839.md`](results/2026-09-06-v9-support-v3-smoke-36f43839.md)。
- 2026-09-07 在 `dev@743496a3` 完成 `product-search-v9 + support-v3` 三輪 Release baseline：66／66 請求完成、成本 US$0.120857；Intent 32／39、Support required facts 11／12、整體 deterministic 58／66，因此正式 `FAIL`。失敗集中於 014、019、020、027、SUPPORT-011；依既有決策以 `product-search-v10 + support-v4` 與 `zh-TW-v1.0.8-draft` 做最小修正，不放寬門檻。完整證據見 [`results/2026-09-07-v9-support-v3-release-baseline-743496a3.md`](results/2026-09-07-v9-support-v3-release-baseline-743496a3.md)。新版本付費驗證尚未授權。
- 2026-09-07 在 `dev@ecb5b0cd` 執行 v10／support-v4 五案各三輪 Live smoke：系統環境 15／15 請求完成，成本 US$0.019291，四個搜尋案 12／12 Intent 通過。原 Runner 因 grader 把「無法貨到付款」誤當政策矛盾而 `FAIL`；正式人工覆核 14 Pass／1 Fail，另找到一輪補問暴露用途 enum。Dataset／grader 候選版升為 `zh-TW-v1.0.9-draft`／`deterministic-v1.1.7`，並重用既有多語顯示名稱在回傳前正規化補問用途；不放寬 100% 必答與顧客語言門檻。環境失敗、付費執行、人工覆核與修正詳見 [`results/2026-09-07-v10-support-v4-five-case-smoke-ecb5b0cd.md`](results/2026-09-07-v10-support-v4-five-case-smoke-ecb5b0cd.md)。
- 2026-09-07 在 `dev@4f094ac7` 先完成 v10／support-v4 兩案各三輪聚焦復驗，6／6 自動與人工 Pass、成本 US$0.013841；其後完整三輪 Release baseline 執行 66／66、成本 US$0.123067，所有彙總自動門檻通過，但正式人工覆核為 65 Pass／1 Fail。`SUPPORT-SECURITY-017` 一輪錯把拆封概括成原則上不可退，與核准政策矛盾，因此完整 baseline 維持 `FAIL`。最低成本修正升為 `support-v5`／`zh-TW-v1.0.10-draft`／`deterministic-v1.1.8`，新增提示詞護欄與該輪詞彙回歸，不放寬既有安全或品質門檻。證據見 [`results/2026-09-07-v10-support-v4-focused-postmerge-4f094ac7.md`](results/2026-09-07-v10-support-v4-focused-postmerge-4f094ac7.md) 與 [`results/2026-09-07-v10-support-v4-release-baseline-4f094ac7.md`](results/2026-09-07-v10-support-v4-release-baseline-4f094ac7.md)。
- 2026-09-07 在 `dev@390f3d02` 的 support-v5 聚焦復驗為 3／3 自動與人工 Pass；隨後完整 baseline 66／66 執行、成本 US$0.123835，但一筆商品 5 秒逾時與一筆客服 outcome mismatch 使自動 Gate 與人工覆核 64／66，正式 `FAIL`。兩案三輪診斷成本 US$0.019745；客服 3／3 Pass，商品再次出現 5.019 秒逾時，並重現既有 AM5 CPU 被重複寫入偏好。證據見 [`results/2026-09-07-v10-support-v5-release-baseline-390f3d02.md`](results/2026-09-07-v10-support-v5-release-baseline-390f3d02.md) 與 [`results/2026-09-07-v10-support-v5-unavailable-diagnostic-390f3d02.md`](results/2026-09-07-v10-support-v5-unavailable-diagnostic-390f3d02.md)。
- AI 客服 Responses Adapter、M-19 與 M-18 搜尋垂直切片均已合併 `dev`；現行商品搜尋仍為單次 SearchIntent Responses Adapter，推薦理由由後端核准候選事實確定性產生，其餘白名單候選、既有零件確認閘門、降級路徑與 `/ai-search` UI 契約不變。`product-search-v11` 依 DEC-BATCH-065 只將商品請求切至可回復的 Fast 層級並加入 proposal 偏好去重；須完成 PR review、Required CI、安全審查及合併後 revision-pinned 聚焦 Live 復驗，通過後才可重跑完整 baseline。
- 2026-09-04 補齊 14 筆 deterministic-only orchestration 證據，正式 Application／Domain 路徑 14／14、無結果 UI 聚焦套件 7／7 通過，外部模型呼叫與成本均為 0；完整報告見 [`results/2026-09-04-deterministic-orchestration-v1.0.3.md`](results/2026-09-04-deterministic-orchestration-v1.0.3.md)。此證據不取代 v4 Live 品質、P95、Token 與成本評估。
- PR／CI 只執行資料產物與 deterministic contract 檢查，不呼叫 OpenAI。
- `DoSelect.Application.Tests`、API Integration 與 SQL Provider-backed tests 固定 AI-13 的隱私、授權、同意、額度預留、最後一額、併發競爭、Owner、語系、唯讀工具、Schema 與降級契約；Responses Adapter tests 另固定 `store=false`、引用、模型／Token、重試與 Fail Closed。這些證據都不取代完整 live model 評估；目前瀏覽器證據只涵蓋既定降級旅程。
- 未來 live runner 必須在呼叫前顯示預估成本，保存模型／Prompt／Schema／Tool／資料集／Grader／Commit 版本，且不得輸出 API Key。
