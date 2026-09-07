# DoSelect product-search-v9／support-v3 Release baseline 報告

## Evaluation decision

- Verdict：`FAIL`；66 次 Live Provider 請求全部完成，但 Intent Field Accuracy、Support Required Fact Coverage 與整體 deterministic checks 未通過既定門檻。
- Feature／revision：`product-search-v9 + support-v3`／`dev@743496a30025580a181b21455f999c7b76fabf50`（PR #133 squash merge）。
- Run ID：`20260906T162551Z-v9-support-v3-release-dev-743496a3`。
- Dataset／Fixture／grader：`zh-TW-v1.0.7-draft`／`v1.0.4`／`deterministic-v1.1.6`。
- Model：商品 `gpt-5.6-luna`、客服 `gpt-5.6-terra`。
- Live external calls：Yes；22 個 live-eligible Release 案例各 3 輪，共 66 次 Responses 請求。
- 成本停止線：US$0.20；實際 US$0.120857，未因成本停止。
- 證據層級：T2；原始產物保存在 Git 忽略的 `.run/ai-evals/20260906T162551Z-v9-support-v3-release-dev-743496a3/`。

本結果是可重現的失敗 baseline，不是整體系統 Release verdict，也不得用來關閉 AI-09。Runner 原始人工覆核表仍為 66／66 Pending；本次先依 automated failure evidence 修正，沒有把人工未覆核項目宣稱為 Pass。

## Thresholds and results

| 類別 | 門檻 | 結果 | Runs／variance | 狀態 |
|---|---:|---:|---|---|
| Schema Valid Rate | ≥98% | 100% | 66／66 | Pass |
| Intent Field Accuracy | ≥90% | 82.05% | 商品 32／39 | **Fail** |
| Clarification Shape | 必須符合 | 100% | 商品 39 輪 | Pass |
| Clarification Precision | ≥90% | 100% | 商品 39 輪 | Pass |
| Clarification Recall | ≥85% | 100% | 商品 39 輪 | Pass |
| Valid Recommendation Rate | 100% | 100% | 可推薦商品案例 | Pass |
| Citation Grounding Rate | ≥95% | 100% | 客服 27 輪 | Pass |
| Support Required Fact Coverage | 100% | 91.67% | 11／12 facts；5／6 applicable runs 完整 | **Fail** |
| Privacy／Authorization deterministic | 100% | 100% | 15／15 | Pass |
| 全部案例 deterministic checks | 必須通過 | 87.88% | 58／66 | **Fail** |
| 商品 P95 | ≤5,000 ms | 4,064 ms | 39 輪 | Pass |
| 客服 P95 | ≤10,000 ms | 3,400 ms | 27 輪 | Pass |
| 商品平均成本 | ≤US$0.01 | US$0.000550 | 39 輪 | Pass |
| 客服平均成本 | ≤US$0.03 | US$0.003682 | 27 輪 | Pass |
| Formal human review | 66／66 完成 | 0／66 已正式裁定 | Runner 原始表全部 Pending | Pending |

Token 合計為 Input 114,486、Output 7,079。商品平均延遲 2,492 ms，客服平均延遲 2,305 ms；實際請求數與規劃相同，未觀察到額外 retry。

## Failed case-runs and ruling

| Case | 失敗輪次 | 觀察 | 既有決策／契約裁定 | 最小修正 |
|---|---:|---|---|---|
| `SEARCH-CREATOR-014` | 1、2、3 | 2TB 被輸出為 `eq` 或 `gte`，SSD 有時降為偏好或遺漏 | DEC-P407 的「2TB SSD 精確規格」表示容量為 `eq 2048GB` 且 SSD 是硬條件；原 dataset 的 `gte` 需版本化修正 | Prompt 明示精確容量／SSD；後端確定性保存；dataset `gte` 改 `eq` |
| `SEARCH-NOVICE-019` | 2 | 8TB 被輸出為 `gte` | DEC-P403：未說「至少／以上」即為精確 `eq 8192GB` | Prompt 與後端將無上下界措辭的明確容量正規化為 `eq` |
| `SEARCH-NOVICE-020` | 1、3 | 既有 AM5 CPU 的 `CPU_SOCKET` 同時出現在頂層 requiredSpecs | DEC-P407：既有 CPU 先走 proposal／確認階段，不先成為搜尋條件 | 移除與 proposed existing part 完全相同的頂層規格 |
| `SEARCH-NOVICE-027` | 2 | 「幫我組電腦」被分類為 Prebuilt | DEC-P389：明確「配／組／組裝」為 CustomBuild | Prompt 維持通則，後端以窄範圍組裝詞覆寫分類 |
| `SUPPORT-POLICY-011` | 2 | 只涵蓋 2／3 必要事實，漏掉組裝電腦預付款限制 | 已核准 requiredFacts 與 support-v3 安全／引用邊界不變 | support-v4 要求政策回答納入適用的付款、履約管道與資格限制 |

共 8 個失敗 case-runs、5 個案例。修正不降低任何門檻，不硬編碼案例 ID／完整原句，也不改公開 API、資料庫、依賴或安全政策。

## Lowest-Cost Analysis

1. 維持現況：可保留真實 FAIL，但無法修正已重複觀察的分類與必要事實缺漏，不符合 Release Gate。
2. 只改文件或人工覆核為 Pass：不能改變 82.05%／91.67% 的自動結果，且會掩蓋真實缺口，不採用。
3. 放寬門檻：會削弱已核准的 Intent 與客服完整性契約，不採用。
4. 重用既有 Prompt、解析器與 dataset 版本化路徑：以窄範圍確定性正規化處理已裁定語意，並以單元測試保護上下界、既有零件及組裝詞；這是第一個完整滿足契約且成本最低的方案，採用。
5. 立即重跑付費 baseline：在零成本修正與 CI 尚未通過前只會重複成本，不採用；後續另行取得費用授權。

## Business Impact

| 欄位 | 評估 |
|---|---|
| 受影響角色 | 商品搜尋使用者、組裝電腦客戶、客服政策詢問者與發布審核者 |
| 現況損失／風險 | 5／22 個 live-eligible 案例至少一輪失敗；可能遺漏硬性規格、誤分組裝需求、提前套用未確認零件，或漏答適用付款限制 |
| Reach／frequency | 本次固定 Release 樣本中 8／66 case-runs 失敗；不可直接外推正式流量 |
| 預期結果 | 聚焦回歸、完整零成本 AI 測試、Required CI 通過；新版本 Live 驗證另行裁定 |
| Build／recurring cost | 重用現有程式與 Prompt；無新依賴、Schema、服務或持續營運成本；付費重驗另計 |
| 風險成本 | 關鍵風險是過度正規化；以窄詞彙、上下界負向測試、exact duplicate 比對與版本化 dataset 限制範圍 |
| 信心 | 高；五案均能由逐輪結果與既有決策直接對應 |
| 成功指標 | 不降門檻；受影響案例與泛化負向測試全綠；新 Live Smoke／baseline 再獨立判定 |
| Stop／rollback | 若需公開契約、資料庫、安全邊界或新依賴即停止重決；本修正可按單一 commit 回復 |

## Reproducibility and evidence index

- Redacted command：`dotnet run --project tools/DoSelect.AiEvals/DoSelect.AiEvals.csproj --no-restore -- --project-root . --split release --trials 3 --output <run-directory> --execute --stop-after-cost-usd 0.20`。
- 執行環境：Windows；Repo 鎖定 .NET SDK `10.0.303`。
- 執行時間：2026-09-06T16:25:54.0028773Z 至 2026-09-06T16:28:33.6867477Z，共 159.684 秒。

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `case-results.jsonl` | 111,396 | `C12A014282992183922084A6019A6F9614150F71DD4E2D11FECC1A2701C013F4` |
| `checkpoint.json` | 405 | `6024E1A716FC07BA6F08907C73D9BA2C8D3C6EB5698D33F994625A4962E6181F` |
| `human-review.md` | 83,051 | `AFEE3C77485084E98A8615D1CCD81B0EBF9EE2CAE33B168B396B67E85256B2FE` |
| `run-metadata.json` | 720 | `FDAE3985AB717A9F89426B1C7B724F2ED578032BB700BB0B921B537B370568BF` |
| `summary.json` | 1,546 | `8BD811ABFE244DB683CC6EB7A3074658931A6470DB66E0F4F5FEFC1A0F4FBF23` |
| `evidence-manifest.json` | 4,717 | `A0D3388086B78CF49093D3A9CE952A7AA3C8935085B1B4ECF8D06756D9B2A698` |

Secret-like、Private Key、Bearer Token、非合成 Email 與台灣手機號碼掃描均為 0；metadata 明示沒有 Production Data 或真實個資。證據狀態為 `PASS`，測試狀態仍為 `FAIL`。

## Zero-cost remediation evidence

- TDD Red：`OpenAiProductSearchClientTests` 先固定 5 個預期失敗，分別覆蓋精確容量、SSD、既有零件重複規格、組裝詞分類與 Prompt 契約；「至少 8TB」負向控制維持通過。
- TDD Green：`OpenAiProductSearchClientTests` 27／27（含非儲存商品提及 SSD 時不得注入儲存規格，以及同句精確 storage／最低 RAM 分開判定的負向保護）；Application AI 47／47；排除既知 SQL Provider 環境組後的 Infrastructure AI 84／84。
- 完整 Infrastructure AI 曾執行 97 案：程式期待失敗已修正；其餘 15 案全為既有本機 SQL TLS／SSPI 連線錯誤，未與本次純 Prompt／解析器修正混為產品回歸。
- Dataset：120／120 build check 與 validate 通過；六組／三 split 分布正確；隱私掃描通過；Live OpenAI calls `not performed`。
- Build：`dotnet build DoSelect.slnx --no-restore --configuration Release` 成功，0 warning／0 error。
- Format：本次 5 個 C# 變更檔案的 whitespace、style、analyzers `--verify-no-changes` 全數通過；全解決方案 format 另揭露既有未變更檔 `IntegrationTestEnvironment.cs` 的 `CA2255` warning。
- Release dry run：36 selected、22 live-eligible、14 deterministic-only、3 trials、66 planned requests；Annotations Approved、Usage blocker false、Live Ready true、blockers 空集合。
- 本修正階段沒有 Live 外部呼叫，新增成本 US$0。

## Limitations and next gate

1. 66 個輸出的正式顧客視角人工覆核仍為 Pending；本報告只裁定自動失敗與修正責任。
2. 14 個 deterministic-only Release 案例不由 Live Adapter 執行，仍須引用既有 orchestration 證據。
3. `product-search-v10 + support-v4` 必須先完成零成本測試、PR review 與 Required CI；之後付費重驗仍需另行明確授權。
4. 建議先對 5 個受影響案例各 3 輪執行 15-request Live Smoke，停止線 US$0.05；通過後再決定完整 66-request baseline，避免直接重複 US$0.12 等級成本。
