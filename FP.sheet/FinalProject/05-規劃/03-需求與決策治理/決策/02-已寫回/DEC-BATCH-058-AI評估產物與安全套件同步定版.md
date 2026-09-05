---
batch_id: DEC-BATCH-058
status: applied
decision_date: 2026-09-05
decision_ids:
  - DEC-P398
---

# DEC-BATCH-058｜AI 評估產物與安全套件同步定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P398 | `cases-source.mjs` 是 AI 評估 Dataset 與 Fixture 的可編輯單一來源，提交前必須以既有產生器更新衍生檔，CI 使用 `--check` 阻擋飄移；本次將既有 Fixture `v1.0.4` 的 8TB 儲存裝置名稱與 Badges 補回來源，不得以重新產生為由降回 `v1.0.3`。`DoSelect.AiEvals` 直接引用中央定版 `Newtonsoft.Json 13.0.4`，覆蓋 Hangfire 帶入的脆弱 `11.0.1`；不壓制 `NU1903`，也不為此擴大升級 Hangfire。 |

## Lowest-Cost Analysis

1. 不處理：AI Evaluation Contract、Backend、Browser E2E 與彙總 Required CI 持續失敗，未採用。
2. 只壓制 `NU1903` 或從 Solution 排除評估工具：會隱藏已知高嚴重度弱點或削弱既有 Gate，未採用。
3. 升級 Hangfire：影響正式背景工作相依面，且直接安全版本覆蓋已足以通過 Restore，未採用。
4. 延伸既有 Central Package Management：只在評估工具加入直接參考並精確定版安全版本，同時修正既有來源／產物同步，採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 五人開發團隊與 PR 審查者；產品顧客流程不變 |
| 現況風險 | Solution Restore 因 `Newtonsoft.Json 11.0.1` 的 `NU1903` 被阻擋；AI 評估來源與衍生檔不一致 |
| 可量測結果 | CI 同款 `restore -warnaserror`、Build、AI Contract 與 Required CI 可通過；解析版本為 `13.0.4` |
| 建置與維護成本 | 一個中央版本、一個評估工具直接參考及既有產生流程；無服務、Schema 或執行費用 |
| 風險與回復 | 相容風險低且限於評估工具／Hangfire 共用相依解析；若回歸可移除直接參考並另案升級上游套件 |
| 信心 | 高；CI 已直接指出兩個根因，本機 CI 同款 Restore 與聚焦測試可驗證 |

## 驗證與邊界

- `node scripts/build-ai-eval-dataset.mjs --check` 與 120 筆資料／隱私驗證通過，Fixture 維持 `v1.0.4`。
- `dotnet restore DoSelect.slnx --configfile NuGet.config --no-cache -warnaserror` 通過，`DoSelect.AiEvals` 實際解析 `Newtonsoft.Json 13.0.4`。
- Solution Build `-warnaserror` 為 0 warning／0 error；Application AI 47／47、Infrastructure AI 31／31、API AI 7／7 通過。
- Package Source Evidence 正式驗證與拒絕路徑自測通過；自測夾具同步納入 `tools/*.csproj`，未放寬來源或版本規則。
- 系統 PowerShell 查詢 NuGet 漏洞資料源後，九個 .NET 專案均沒有回報已知脆弱套件。
- 未呼叫 OpenAI，沒有公開 API、DTO、資料庫、Migration、模型、逾時或費用設定變更。

## 影響文件與追蹤

- `Directory.Packages.props`、`DoSelect.AiEvals.csproj`、AI 評估來源與衍生檔。
- 決策索引／紀錄、未完成項目追蹤表與本次日誌。
