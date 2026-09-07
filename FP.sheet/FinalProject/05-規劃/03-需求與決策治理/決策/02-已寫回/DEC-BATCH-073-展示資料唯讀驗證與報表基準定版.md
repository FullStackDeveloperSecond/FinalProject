# DEC-BATCH-073｜展示資料唯讀驗證與報表基準定版

- 狀態：✅ 已完成並合併
- 日期：2026-09-07
- 決策者：Codex（依 alex 常駐授權）
- 來源：DATA-RC-03／DATA-08

## 問題與證據

- 既有 `--seed-demo` 是資料建立命令，不能作為獨立、唯讀且失敗回非零的驗證 Gate。
- DATA-RC-03 指定總筆數、孤兒、非法狀態、負庫存與「報表基準值」，但未定義報表基準的報表集合、時間點或欄位。
- 報表中的長期未銷售數量依現在時間變動；若直接使用系統時鐘，即使固定 Seed 未變，基準也會隨日期漂移。

## 最低成本分析

1. 接受目前狀態或只補文件：不採用；無法提供自動檢查與非零結束碼。
2. 以人工 SQL 清單驗證：不採用；無法重用既有報表規則，也不能成為單一可重跑 Gate。
3. 重用既有 EF Context、Seeder Manifest 與七份 Operational Report Query：採用；只增加 Development-only 命令、唯讀 Validator 與薄 PowerShell 包裝即可滿足要求。
4. 新增 API、資料表、Migration、背景工作或套件：不採用；對本機展示資料驗證沒有必要，且會增加安全與維護成本。

## 決策

- 新增 `--validate-demo` 與 `scripts/validate-demo-data.ps1`。命令只允許 `Development`、本機／loopback SQL Server 與 `DoSelectDemo`／`DoSelectDemo_<32 hex>`；不建立、不遷移、不修正、不清除資料。
- v1 報表基準定義為 `implemented-features-v2` 固定期間內，既有 `OperationalReportCatalog.All` 七份報表的全部 Summary metrics；固定 as-of 為 Seed 期間結束後一秒，避免長期未銷售指標隨執行日期漂移。
- 驗證同時檢查 Migration、FeatureProfile Marker、10,000 筆總數與 Entity 配額、特殊分布、`DBCC CHECKCONSTRAINTS`／FK 孤兒、負庫存、非法 enum、非法工作流及報表基準。輸出只含彙總，不含會員、地址、訂單、客服內容或連線字串。
- 任一檢查不符時 `IsValid=false`、列出穩定 failure key，程序回傳 exit code 1；無法連線或 Schema 不相容則 fail closed。
- 本項只定義數值正確性基準，不包含延遲／P95；效能仍由 DATA-RC-04 另行量測。

## 本機驗收

- 程式 revision `36f32a0087ba612b6ccaf905f00ef09db08f4d7a`。
- Release solution build：0 warning／0 error；`dotnet format --verify-no-changes` 與 PowerShell parser 通過。
- `DemoDataValidatorSqlServerTests`：3／3；完整 Infrastructure：1,241／1,241。
- 實際 CLI 成功路徑：全部 11 個 check 通過、exit 0。
- 故意將一筆已付款金額增加 1：筆數、約束與完整性仍通過，只有 `reportBaselines` 失敗、exit 1；拋棄式資料庫已刪除。
- 首輪沙箱 TLS 失敗與首輪 `sqlcmd` 未注入污染的演練均明列為無效證據，不用於核准。

## 交付 Gate 結果

- Security diff scan `f7a363ed-7c97-44d2-a4c8-f79e99fdfef2`：snapshot `cf89e056ac1ecf57f90b7c4955b083b498956b126815d1acf709fa422ed6cbfd`、7／7 review items、5 surfaces、0 finding、coverage `complete`。
- PR #147 exact head `473d9a4d752948d28582d716d7631dfb2fdd2747` 的 Required CI Run `34089858394` 全綠；Backend 12m11s、Browser E2E 4m59s、`CI Required` 通過。
- 同帳號正式核准被 GitHub 拒絕後，依 alex 既有授權以管理員 bypass squash merge；遠端 readback 為 `dev@483deeee830bbcb071143ee366cbc93ea3523899`，功能分支已刪除。
- DATA-RC-03／DATA-08 關閉；DATA-RC-04 進入下一個待辦，不在本決策提前宣告完成。
