# DEC-BATCH-072｜展示 Seed 特殊分布與版本 Marker 定版

- 狀態：本機實作、驗證與安全審查通過，待 PR Required CI／合併
- 日期：2026-09-07
- 決策者：Codex（依 alex 常駐授權）
- 來源：DATA-RC-02／DATA-07 與《報表與展示資料》特殊案例門檻

## 問題與證據

- `implemented-features-v1` 已有 120 筆 Refund，但只有 60 筆進入 `Succeeded`；若只用資料表筆數宣稱「至少 100 筆全額或部分退款」，會混淆待審退款與實際成功退款。
- v1 回傳只有各 Entity 筆數，無法由機器直接讀取完成、取消／逾時、退款、付款失敗／逾時、低庫存及各工作流狀態分布。
- v1 no-op 使用固定品牌 Marker；若調整資料分布卻沿用同一 Marker，舊資料可能被新版本名稱誤認。

## 最低成本分析

1. 接受 v1 或只補文件：不採用；無法證明成功退款至少 100，也無法提供資料庫實際查詢結果。
2. 只以人工 SQL 查詢一次：不採用；結果不會隨 Seed 命令輸出，重跑與版本關聯容易漂移。
3. 重用既有 Seeder、EF Query 與 JSON 結果：採用；在不新增 Schema、套件、服務或清庫流程下，同時修正分布並提供可重跑摘要。
4. 新增持久化 Manifest／稽核資料表：不採用；DATA-RC-02 不需要新的 Production Schema。

## 決策

- FeatureProfile 升為 `implemented-features-v2`；100 筆 Refund 經既有 `Approve → Processing → Succeeded` 狀態機完成，對應 100 筆 ReturnRequest 完成，其餘 20 筆等待退款、30 筆等待寄回。
- Seed Marker 改為 v2 專屬 `DEMO-V2-BRAND-001`。舊 v1 資料即使總筆數相同，也不會被回報為 v2；Seeder 依原有非空保護 fail closed，不就地改寫。
- `DemoSeedResult` 新增唯讀 `Distribution` JSON，保存正式門檻及配送、客服、退貨、評價審核狀態；不輸出會員、地址、訂單或客服內容。
- DATA-RC-02 的 revision-pinned machine-readable 證據為 [[05-規劃/04-稽核與報告/2026-09-07-DATA-RC-02特殊案例分布摘要.json]]。
- v2 使用新的本機 allowlist `DoSelectDemo_00000000000000000000000000000002` 建立並保留給 DATA-RC-03／04；舊 `DoSelectDemo` v1 不刪除、不覆寫。

## 安全與驗收結果

- 程式 revision `28737b0702abc890f0140e56ab3ace012423b2a3`：Release build 0 warning／0 error、focused SQL Server 4／4、完整 Infrastructure 1,231／1,231、format 與 diff check 通過。
- v2 DB 首次 `Created=true`，精確 10,000；同 revision 重跑 `Created=false` 且 Counts／Distribution 完全相同。
- 正式最低分布：完成訂單 250、取消或付款逾時訂單聯集 125、Refund 120／Succeeded 100、付款失敗 100、付款逾時 100、低庫存上架 SKU 100，全部通過。
- Security diff scan `40571960-dd72-4c25-ab83-5ecc49662a10`，snapshot `133e52af0faa755f995c515b97da89413ad9762fd278ae3bc9c9c15c13682817`，2／2 source、0 candidate、0 finding、0 deferred、coverage `complete`。

## 後續 Gate

- 本項仍須通過文件 review、commit、push、PR Required CI 與 exact-head review 才能關閉 DATA-RC-02／DATA-07。
- DATA-RC-03 另建可非零結束的完整驗證腳本與故意失敗測試；DATA-RC-04 再量測七個報表 P95，本決策不提前宣告。
