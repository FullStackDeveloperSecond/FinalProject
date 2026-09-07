# DoSelect AI v8 Release baseline 報告

## Evaluation decision

- Verdict：`FAIL`；66 次 Live Provider 請求全部完成但 automated thresholds 未通過；後續人工覆核已完成 66／66，44 Pass／22 Fail。
- Feature／revision：AI 商品搜尋推薦與 AI 客服；`dev@155bafa363f99641e530f65727d7a6cbf677f626`（PR #122 squash merge）。
- Model／configuration：商品 `gpt-5.6-luna`＋`product-search-v8`；客服 `gpt-5.6-terra`＋`support-v2`；Standard／short-context 單價於 2026-09-06 依 OpenAI 官方價格頁重新核對。
- Dataset／grader：`zh-TW-v1.0.5-draft`／`deterministic-v1.1.4`；Fixture `v1.0.4`。
- Live external calls：Yes；22 個 live-eligible 案例各 3 輪，共 66 次。
- 成本：US$0.110179；停止線 US$0.12；未觸發成本中止。
- 執行時間：2026-09-06 04:29:48Z～04:31:59Z，共 131.286 秒。
- 證據層級：T2；原始產物保存在 Git 忽略的 `.run/ai-evals/20260906T042931Z-v8-release-dev-155bafa3/`。

本結果是可重現且可診斷的失敗 baseline，不是整體系統 Release verdict，也不得用來宣稱 AI-09 已完成。14 個 deterministic-only Release 案例依契約不由 Live Adapter 執行，仍須引用既有 orchestration 證據；66 個顧客可見輸出已完成逐筆人工覆核，完整結果見 [`../reviews/2026-09-06-v8-release-baseline-human-review.md`](../reviews/2026-09-06-v8-release-baseline-human-review.md)。

## 執行前 Gate

| Gate | 結果 |
|---|---:|
| 固定 revision | `155bafa363f99641e530f65727d7a6cbf677f626` |
| 工作樹 | 乾淨；Windows CRLF 重建後與 Git blob 零實質 diff |
| Dataset build check | 120／120 通過 |
| Dataset validation | 120／120 通過；六組與三個 split 分布正確 |
| 隱私掃描 | 無真實樣式 Email、台灣手機、API Secret 或 Private Key |
| Release dry run | 36 selected、22 live、14 deterministic-only、66 planned requests |
| 標註／Live readiness | `AnnotationsApproved=true`、`IsLiveReady=true`、無 blocker |
| Secret gate | API Key 存在；四個單價存在且符合官方 Standard／short-context 值；無環境變數覆寫 |

## Thresholds and results

| Category | Threshold | Result | Runs／variance | Status |
|---|---:|---:|---|---|
| Schema valid | ≥ 98% | 100% | 66／66 | Pass |
| Intent field accuracy | ≥ 90% | 61.54% | 24／39 商品案例輪次通過；5 案均三輪失敗 | **Fail** |
| Clarification precision | ≥ 90% | 75% | `SEARCH-NOVICE-020` 三輪產生不符合期待的補問 | **Fail** |
| Clarification recall | ≥ 85% | 100% | 需要補問的案例均有補問 | Pass |
| Valid recommendation | 100% | 90% | `SEARCH-NOVICE-020` 三輪未形成可評分推薦 | **Fail** |
| Citation grounding | ≥ 95% | 100% | 27 個客服案例輪次 | Pass |
| Privacy／authorization | 100% | 100% | 相關 deterministic checks 全數通過 | Pass |
| 商品 P95 latency | ≤ 5,000 ms | 3,863 ms | 39 個商品案例輪次 | Pass |
| 客服 P95 latency | ≤ 10,000 ms | 2,626 ms | 27 個客服案例輪次 | Pass |
| 商品平均成本 | ≤ US$0.01 | US$0.000534 | 39 個商品案例輪次 | Pass |
| 客服平均成本 | ≤ US$0.03 | US$0.003310 | 27 個客服案例輪次 | Pass |
| 整體 deterministic pass | 未設獨立門檻 | 77.27% | 51／66 | Observation |
| 顧客視角人工覆核 | 全部需覆核 | 44 Pass／22 Fail | 66／66 已覆核；原始 Runner pending 產物保留 | **Fail** |

Token 合計為 Input 108,339、Output 6,736。商品平均延遲 1,954 ms，客服平均延遲 2,032 ms。實際 HTTP 模型請求與規劃相同，皆為 66，未觀察到額外 retry。

## Hard failures

- 未觀察到 Privacy、Authorization、Unsafe action、Schema、HTTP、Quota、成本停止線或延遲 Gate 失敗。
- 自動失敗集中於 5 個商品搜尋案例，共 15 個案例輪次；每案三輪結果一致，顯示不是單輪隨機抖動。
- `SEARCH-NOVICE-020` 涉及既有零件確認邊界；在確認完成前停止推薦符合 DEC-P399，但模型把明確的主機板單品需求分類為 `CustomBuild`，並補問整機用途，仍屬產品意圖分類缺口。

## Regression／contract analysis

| Case | 三輪觀察 | 與既有決策比對 | 初步分類 | 後續責任 |
|---|---|---|---|---|
| `SEARCH-CREATOR-014` | 三輪皆為 `CustomBuild`、VideoEditing、NT$50,000；擷取 2TB 為 2,048GB，並另保留 SSD 條件 | DEC-P400 固定 1TB=1024GB；使用者也明說 SSD。舊 expectation 只允許一個 opaque spec，因 count 精確比對而誤判 | Dataset／grader contract drift（高信心） | 版本化 expectation；以結構化容量＋SSD 條件覆核 |
| `SEARCH-CREATOR-015` | 三輪皆分類 `CustomBuild` | DEC-P389 明定「用途＋預算的整機需求」採 `CustomBuild`；舊 expectation 仍是 `PrebuiltComputer` | Dataset contract drift（高信心） | 修正 frozen label 並加入非 Release 泛化案例 |
| `SEARCH-NOVICE-020` | 三輪皆誤分 `CustomBuild`、補問整機用途；同時辨識未確認 AM5 CPU，Runner 未進推薦 | DEC-P127 允許具類別／關鍵字的 `SingleProduct`；DEC-P241／399 要求既有零件先確認、確認前不推薦；舊 expectation 的 `General` 與直接 recommend 亦未完整反映此流程 | 混合：模型分類缺口＋Dataset 流程漂移（高信心） | 保留 SingleProduct 期待；明確定版確認階段與用途空集合，再做零成本回歸 |
| `SEARCH-NOVICE-021` | 三輪皆正確取得 SingleProduct、Storage、2,048GB、NT$4,000，推薦有效；只因 purposes 為空而 intent mismatch | DEC-P401 明定未明說的用途不可補成 `General`；舊 expectation 仍要求 `General` | Dataset contract drift（高信心） | 移除虛構 `General` 期待；保留其餘欄位 |
| `SEARCH-NOVICE-023` | 三輪皆取得 SingleProduct、Mouse、NT$2,000 並有效推薦，但未將「遊戲滑鼠」映射為 Gaming | 文字直接表達遊戲型滑鼠，Release expectation 要求 Gaming；現行 Prompt 對「商品名稱中的用途詞」沒有足夠明確的泛化規則 | Prompt／模型意圖缺口（中高信心） | 以 development／challenge 同義案例先驗證泛化，不直接對 Release 原句硬編碼 |

上述分析沒有把所有失敗都歸咎於資料：`SEARCH-NOVICE-020` 與 `023` 保留真實產品行為缺口；也沒有為追求分數而放寬 Privacy、Authorization、Schema、Citation、成本或延遲門檻。

## Lowest-Cost Analysis

1. 維持現況：可以保留真實 FAIL，但無法關閉已證實的 frozen expectation 與現行決策衝突，不能滿足 AI-09 Release 證據需求。
2. 只修改文件或人工把 66 案標為 Pass：無法修正自動 intent／clarification／recommendation Gate，且會掩蓋 `020`／`023` 的真實行為缺口，不採用。
3. 修改設定或放寬門檻：失敗是案例語意與流程不一致，不是 Timeout 或成本設定；降低 90%／100% 門檻會削弱既有發布契約，不採用。
4. 重用既有路徑做最小版本化修正：修正有既有決策證據的 dataset／grader drift，並只對 `020`／`023` 補最小泛化分類規則；搭配新增 development／challenge 案例、聚焦回歸與 dry run。這是第一個能完整滿足正確性、避免 release-set 過度調校且不新增依賴／Schema／服務的方案，建議採用。
5. 立即再次付費跑完整 66 次：在零成本契約與回歸尚未關閉前只會重複花費，不採用。

## Business Impact

| 欄位 | 評估 |
|---|---|
| 受影響角色 | 電腦組裝新手、專業創作者、AI 搜尋使用者與展示人員 |
| 現況損失／風險 | 13 個商品 Live 案例中有 5 案三輪一致失敗；其中 3 案主要是假失敗，2 案含真實分類／流程缺口，會同時扭曲發布判斷與顧客體驗 |
| Reach／frequency | 本次商品案例輪次 15／39 失敗；這是固定評估樣本比例，不能直接外推正式流量 |
| 預期可量測結果 | 修正後聚焦案例與新增非 Release 泛化案例通過，Release dry run 無 blocker；新付費 Gate 仍需另行授權與人工覆核 |
| Build／recurring cost | 最小 dataset／grader／Prompt 或 Adapter 回歸，不新增依賴、資料表、服務或持續成本；付費重驗另計 |
| 預期風險成本 | 修改已觀察的 Release set 可能造成過度調校；以提升版本、保留歷史 FAIL、加入 development／challenge 同義案例與禁止硬編碼原句降低風險 |
| 信心 | 014／015／021 contract drift 為高；020 混合缺口為高；023 Prompt 泛化缺口為中高 |
| 成功指標 | 不降低原門檻；零成本回歸與新非 Release 泛化案例全通過；新 Live 結果再獨立判定 |
| Stop／rollback | 若修正需改公開 API、資料庫或放寬安全門檻即停止並重新決策；dataset／Prompt／grader 以版本 commit 可個別回復 |

## Reproducibility

- Redacted command：`dotnet run --no-restore --project tools/DoSelect.AiEvals/DoSelect.AiEvals.csproj -- --project-root . --split release --trials 3 --output <run-directory> --execute --stop-after-cost-usd 0.12`。
- 系統 PowerShell；Windows；.NET SDK `10.0.303`；Node `v24.18.1`。
- 執行 revision：`155bafa363f99641e530f65727d7a6cbf677f626`；工作樹內容與該 commit blob 一致。
- 官方單價來源：[OpenAI API Pricing](https://developers.openai.com/api/docs/pricing)。Responses API 不另收 API 費用，Token 依模型輸入／輸出費率計價。

## Artifact index

| Artifact | Bytes | SHA-256 | 目的 |
|---|---:|---|---|
| `run-metadata.json` | 709 | `3203AD8ECAD63EF1984A26D9BE27A27770B432D9865638F33E3E429DABA59E97` | revision、版本、案例與成本契約 |
| `checkpoint.json` | 394 | `6256768ECDA8FAE7A7CB0E1E7F2428E992120CA1331CD9C9D993BBE8414D7CFD` | 最終狀態與中斷復原點 |
| `case-results.jsonl` | 96,780 | `7B5C2D256A8C4B18146033A1CD97650777F53DEBB38D2E6BF2B0881D5CFAA18B` | 66 筆 machine-readable 結果 |
| `summary.json` | 1,494 | `C2AC16BFF947A07900B970BB6437F8962CBB882B77318419AA7241916CA51BED` | 門檻、成本、Token、延遲與 Verdict |
| `human-review.md` | 39,008 | `017717A63359ECF05B7A568888E6B0BBA3A03478A98488F0EBE73F8DBDD2C42F` | 66 筆顧客視角人工覆核表 |
| `evidence-manifest.json` | 4,484 | `3DB7C81C44DCB8B86001AAA8EB7992FC18EB11510E6BF3B23D4DB372C028BF84` | T2 證據索引與安全邊界 |

## Sanitization

- 資料只使用合成／去識別 Fixture；manifest 明示不含 production data 與真實個資。
- 對全部原始產物重新掃描：OpenAI Key、Authorization Header、Private Key、Email、台灣手機、連線字串均為 0。
- Runner 不保存 API Key；InvalidOutput 不保存 raw model output。本輪沒有 InvalidOutput。

## Limitations

1. 66 個顧客可見輸出已人工覆核；44 Pass／22 Fail。人工結果不會把 automated FAIL 改成 PASS。
2. 14 個 deterministic-only 案例未由 Live Adapter 執行；本報告不取代其既有 Application／Domain／UI orchestration 證據。
3. Release split 已被用於本次診斷；修正不得只針對原句，必須提升版本並加入未用來調 Prompt 的 development／challenge 泛化證據。
4. 本次結果只支援 AI 特定功能的失敗判定，不是整體專案發布就緒結論。
