# DoSelect product-search-v8 中文預算零成本修正報告

## Evaluation decision

- Verdict：`ZERO_COST_REGRESSION_PASS`；025／026 的已知 Adapter 缺口已關閉，但這不是 Live Smoke 或 Release baseline。
- 修正範圍：`SEARCH-NOVICE-025` 的明確口語最高預算、`SEARCH-NOVICE-026` 的衝突安全上限與補問。
- Prompt／Dataset／Fixture／Grader：`product-search-v8`／`zh-TW-v1.0.5-draft`／`v1.0.4`／`deterministic-v1.1.4`。
- Live external calls：No；Token 0、成本 US$0。

## Failure reproduction and correction

| 案例 | v7 失敗重現 | v8 確定性行為 | 零成本結果 |
|---|---|---|---|
| `SEARCH-NOVICE-025` | 模型回傳 `budget=null` 並詢問最高預算 | 從明確「三萬五」保存 `maximum=35000`，移除純預算補問，品牌偏好／排除與用途維持原輸出 | Pass |
| `SEARCH-NOVICE-026` | 模型指出衝突但回傳 `budget=null` | 從「兩萬元以上／最多一萬五」保留 `maximum=15000`、清空不安全的 minimum，產生衝突補問 | Pass |
| 模糊金額負例 | 「三萬五左右」可能被過度解讀 | 不覆寫模型輸出，保留補問 | Pass |

## Verification evidence

- `OpenAiProductSearchClientTests`：20／20 Pass，包含明確上限、衝突上限、「左右／兩三萬」模糊負例與非 `zh-TW` 不套用。
- `LiveEvaluationPlanTests`：23／23 Pass；025 以實際失敗形狀進入正式 Runner／grader 路徑。
- `dotnet build DoSelect.slnx --no-restore -warnaserror`：0 warning、0 error。
- 變更 C# 檔案 format verification：Pass。
- Dataset build check：120 筆與 `zh-TW-v1.0.5-draft` 來源一致。
- Dataset validation：120 筆有效，分組與 Split 正確，真實 Email、台灣手機、API Secret、Private Key 掃描為 0。
- Release dry run：36 selected、22 live、14 deterministic-only、三輪 66 次規劃請求、標註已核准、`IsLiveReady=true`。

## Safety and compatibility

- Guard 僅套用 `zh-TW`；模糊量詞、約數與範圍式表達維持模型結果，不自行推斷。
- 不變更公開 API／DTO、資料庫、模型選擇、5 秒逾時、零同步重試、額度、關鍵字降級或個資邊界。
- 不保存或輸出 raw model output、Secret 或個資。

## Limitations and next gate

1. 零成本回歸只能證明已知 025／026 失敗在 Adapter 邊界被穩定處理，不能證明 Provider 對全部六案的品質或變異。
2. 歷史 v7 Smoke 仍為 `FAIL`，不得追溯改寫。
3. 後續 v8 六案 Live Smoke 已於 Commit `45eeed27` 完成：自動 Gate 全數通過，Alex 顧客視角人工覆核 6／6 Pass，正式 Smoke Verdict 為 `PASS`；詳見 [`2026-09-06-v8-smoke-45eeed27.md`](2026-09-06-v8-smoke-45eeed27.md)。66 次 Release baseline 仍是獨立 Gate，尚未因本報告或 Smoke 自動獲得授權。
