# DEC-BATCH-074｜展示報表 P95 量測口徑定版

- 狀態：✅ 已完成；本機實作、review、測試、正式量測、Security、PR Required CI 與 squash merge 均通過
- 日期：2026-09-07
- 決策者：Codex（依 alex 常駐授權）
- 來源：DATA-RC-04／M-15／NFR-PERF-03

## 決策

1. 量測既有 `OperationalReportCatalog` 的七個 Report Key，不另選較快子集。
2. 使用 `implemented-features-v2` 固定 10,000 筆展示資料與完整 Seed 日期區間；量測前先跑 DATA-RC-03 的 11 項唯讀 Validator，任一失敗就不產生效能通過結論。
3. 每份報表依固定 Catalog 順序執行 3 次 warm-up，再循序執行 30 次有效樣本；不平行、不重試、不刪除離群值。
4. P50／P95 採 nearest-rank；P95 門檻沿用 NFR-PERF-03 的 3,000 ms，每份報表都必須通過，任一超標時 CLI exit 1。
5. 計時邊界為既有 Query Service 到 SQL Server 查詢與 materialization；不包含 API 啟動、HTTP、認證、網路與 JSON serialization。這是第一版查詢效能基準，不得冒充端到端或併發效能。
6. 只輸出 Dataset 版本、量測設定、逐次毫秒數與統計；不輸出連線字串、資料列、機器名、Secret、真實 PII 或 Production data。

## 最低成本判斷

- 不處理：無法取得 DATA-RC-04 要求的 revision、Seed、命令、原始 timing 與摘要，不採用。
- 只補文件／人工 Stopwatch：無法重複執行、無法 fail closed，也無法保證七報表與 P95 演算法一致，不採用。
- 重用既有能力：以 `DemoDataValidator`、`OperationalReportCatalog`、Query Service 與本機 DB allowlist 加一個 Development-only CLI，是第一個完整方案，採用。
- 未新增 HTTP endpoint、套件、Schema、Migration、服務或效能平台；不建立反正規化表，因全部 P95 已遠低於 3 秒門檻。

## v1 正式結果

- 程式 revision：`99412604a5b003413623c7c422ca823c6cc0b391`
- Run：`20260907T070234Z-data-rc-04-99412604`
- 七報表 P95（ms）：銷售 6.122、ABC 10.682、同期 3.995、庫存周轉 63.603、毛利 9.361、關聯 6.820、預測 2.990。
- 最慢為 `inventory-turnover` 63.603 ms，僅占 3,000 ms 門檻約 2.12%；7／7 通過。
- 完整原始時間與環境摘要：[[05-規劃/04-稽核與報告/2026-09-07-DATA-RC-04報表P95原始時間.json]]。
- 交付 Gate：PR #149；head `def5f1d1c83da315b19bbb44d68de591be7d2832`；CI Run `34095108111` 全綠；Codex Security `7c785a81-def9-4abe-a278-18490c8bb733` 12／12、0 finding；squash merge `b024fe150204c2232c282c50ff005d6432452e8e`。

## 邊界

- 結果只適用於所列本機展示環境、固定資料版本與 Query 層範圍；不推論 Production、遠端網路、HTTP、匯出、併發或前端渲染效能。
- DATA-RC-04 已在 exact-head review、Security、PR Required CI 與 squash merge 後關閉；上述量測邊界不因結案而擴張。
