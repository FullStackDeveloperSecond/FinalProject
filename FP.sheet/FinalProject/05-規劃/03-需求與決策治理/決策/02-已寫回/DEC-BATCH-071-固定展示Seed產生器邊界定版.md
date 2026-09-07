# DEC-BATCH-071｜固定展示 Seed 產生器邊界定版

- 狀態：本機實作、驗證與安全審查通過，待 PR Required CI／合併
- 日期：2026-09-07
- 決策者：Codex（依 alex 常駐授權）
- 來源：DATA-RC-01／DATA-06 與《報表與展示資料》10,000 筆配額

## 問題與證據

- 現有 `--seed-minimal` 只支援開發與 E2E 的最小登入／交易 Fixture，無法由空庫建立報表所需的 10,000 筆固定資料。
- 現行 `DoSelectDbContext` 沒有 AI 搜尋漏斗事件 Entity；`AiInteraction` 是模型互動紀錄，不等同 `RecommendationDisplayed` 等漏斗事件，混用會造成語意造假。
- 正式需求允許依 FeatureProfile 重分配尚未啟用功能的保留配額，但要求總數、固定亂數種子與各類計數可追溯。

## 最低成本分析

1. 不處理／接受現況：不採用；無法重建精確 10,000 筆資料，也不能解除 DATA-RC-02～04 的前置阻擋。
2. 只補文件、訓練或人工匯入：不採用；無法保證重跑冪等、外鍵完整與固定分布。
3. 設定、資料修正或 feature flag：不採用；目前沒有可產生完整資料量的既有設定或匯入檔。
4. 重用既有 EF Core Entity、Migration 與明確手動命令：採用；是第一個能同時滿足固定重建、狀態機與資料完整性的方案。
5. 新增 AI 漏斗 Schema／runtime writer、服務或外部依賴：不採用；展示 Seed 不應先創造沒有產品寫入者與讀取者的資料模型。

## 決策

- 新增版本化 FeatureProfile `implemented-features-v1`，固定亂數種子 `20260907`，資料期間錨定 `2026-03-01T00:00:00Z` 至 `2026-08-31T23:59:59Z`。
- 主業務資料精確為 10,000 筆：會員 600、地址 500、商品 250、SKU 750、商品規格值 1,600、訂單 650、訂單明細 1,600、付款 700、物流 550、庫存異動 800、客服案件 250、客服訊息 900、退貨 150、退款 120、評價 250、收藏 200、優惠券與使用紀錄合計 130、AI 搜尋漏斗事件 0。
- 原 AI 搜尋漏斗 400 筆配額全部重分配至客服訊息，明確保留「AI 漏斗 runtime 尚未實作」的缺口；不得以 Seed 宣稱該功能完成。
- 品牌同時包含合成的真實品牌名稱與虛構品牌；本版不建立商品圖片，避免缺少來源、授權或下載日欄位的素材進入展示資料。
- 產生器只能由明確命令執行，只接受本機／loopback SQL Server 上的 `DoSelectDemo` 或 `DoSelectDemo_<32 hex>` 資料庫名稱；只在新資料庫或完全沒有 user table 的既有空庫建立 repository 現行 Migration。既有非空 Schema 若有 pending migration、主業務資料部分非空或不符 Manifest，一律 fail closed，不自動改 Schema、不刪除或清空資料；已符合相同 Manifest 的完整資料只做驗證並回傳 no-op。
- 合成帳號沒有密碼雜湊，不可登入；不寫入真實 Email、電話、地址、憑證、Token 或 Production 資料。
- DATA-RC-01 只關閉產生器、固定 Manifest、fresh DB 與重跑冪等；特殊分布、故意失敗驗證及報表 P95 分別由 DATA-RC-02～04 驗收，不提前宣告。

## 影響摘要

| 項目 | 結論 |
|---|---|
| 影響對象 | Demo、報表、整合測試與 Release 驗證執行者 |
| 現況損失／風險 | 沒有可重建資料集，報表 P95 與特殊案例覆蓋無法取得可信基準 |
| 觸及範圍／頻率 | 每次展示資料庫重建與 RC 驗證；不觸及 Production runtime |
| 預期可量測成果 | fresh disposable DB 精確 10,000 筆；同 seed 重跑計數與鍵不變；外鍵與狀態驗證通過 |
| 建置／持續成本 | bounded Seeder、Manifest、腳本及 Provider-backed tests；無新套件、Schema 或 recurring service |
| 預期風險成本 | 對錯誤資料庫執行或產生非法狀態；以資料庫名稱 allowlist、非空拒絕、交易與驗證 Gate 控制 |
| 信心 | 中；既有 Entity／Migration 可重用，實際 SQL Server fresh DB 與重跑證據完成後升為高 |
| 成功指標 | exact counts、deterministic IDs、fresh DB、second-run no-op、零重複鍵／孤兒／負庫存／非法狀態 |
| 停止／回復條件 | 任一不明資料庫、部分既有資料、資料破壞、非法狀態、真實個資／秘密或計數漂移立即停止；移除手動命令即可回復，無 Migration 要回滾 |

## 授權與後續 Gate

- alex 已授權在不危害安全與資訊外洩時，自主採版本化文件定義並完成 develop→commit→push→review→test 流程。
- 本決策不授權 Production 資料操作、清庫、Migration 套用、付費外部呼叫或放寬安全門檻。
- 完成實作後仍須通過本機 review／test、安全差異審查、PR Required CI 與 exact-head review；同帳號無法自我核准時，依既有授權使用管理員 bypass squash merge。

## 本機驗證與安全結果

- `DoSelectDemo` 首次建立為 `Created=true`／10,000，重跑及安全收窄後再驗均為 `Created=false`／10,000，分類計數不變。
- `DemoDataSeedSqlServerTests` 4／4 Pass；包含 fresh database、固定鍵、no-op、部分非空拒絕、遠端 SQL 拒絕、DB constraints 與核心狀態不變量。
- Release solution build 0 warning／0 error、`dotnet format --verify-no-changes`、PowerShell parser 與 `git diff --check` 通過。
- 完整 `DoSelect.Infrastructure.Tests` 1,231／1,231 Pass。前期 Security snapshots 在發現遠端同名 DB、既有 pending migration 與預先建立空白 DB 邊界需修正後失效／取消，不作為最終通過證據；程式碼凍結後固定 source snapshot scan `e0e84208-c54c-4b2b-86ca-5e8319d8c3da`、digest `3c3cf15b2728ba5134c7cd52d4d6d3b73ecf8f76c8a1d28feee20f8cb637d055` 覆蓋 5／5，0 candidate／0 finding／0 deferred，coverage `complete`。TAC `not_granted` 為 advisory warning，未降低或略過掃描。
