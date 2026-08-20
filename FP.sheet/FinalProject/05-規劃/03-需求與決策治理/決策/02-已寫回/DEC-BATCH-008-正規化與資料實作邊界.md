---
type: decision-record
batch_id: DEC-BATCH-008
title: 正規化與資料實作邊界
status: applied
decision_count: 30
decision_range: DEC-P175～DEC-P204
submitted_at: 2026-08-12
applied_at: 2026-08-12
source: 原始 Meta Bind 互動表單；依 AUTO-DEC-008 由 Git 歷史追溯
---

# DEC-BATCH-008｜正規化與資料實作邊界

## 決策

| ID | 決策結果 |
|---|---|
| DEC-P175 | 交易寫入模型以 3NF 為基線，只允許文件化的交易快照、衍生餘額與唯讀投影例外。 |
| DEC-P176 | 動態規格值使用單一 `SkuSpecificationValue` 表，以 String／Decimal／Boolean／Option 等互斥型別欄位與 Check Constraint 實作。 |
| DEC-P177 | 標籤、角色等多對多關聯使用明確 Join Entity，不使用逗號字串或任意 JSON。 |
| DEC-P178 | 會員地址為可編輯正規化資料；訂單成立時建立不可變地址快照，歷史不回指目前會員地址。 |
| DEC-P179 | 商品名、SKU Code、規格摘要、單價與成本以 Order／OrderItem 明確快照欄位保存。 |
| DEC-P180 | OrderCoupon 保存優惠碼、規則版本與訂單級結果；OrderItem 保存折扣分攤，退款依快照計算。 |
| DEC-P181 | 訂單同時保存物流 Provider Version FK 與成立時的限制、門市及運費精確值快照。 |
| DEC-P182 | InventoryMovement／Reservation 是稽核來源，InventoryBalance 同交易更新；每天核對，差異建立調整案件，不靜默修正。 |
| DEC-P183 | 統一案件工作台第一版使用 SQL `UNION ALL` View＋EF Core Keyless Entity 唯讀投影。 |
| DEC-P184 | 訂單主要摘要狀態第一版查詢時由正式狀態推導，不保存可寫摘要欄位。 |
| DEC-P185 | 七個報表先使用正式交易表與索引；任一報表查詢優化後仍超過 P95 3 秒，才個別核准反正規化。 |
| DEC-P186 | 若建立 Report Snapshot，必須保存 `AsOfUtc`、來源版本與重建狀態，可由交易表完整重建並在畫面標示資料時間。 |
| DEC-P187 | 核心可查詢、需 FK／Constraint 的資料關聯化；JSON 只保存版本化輔助內容與外部事件非關鍵摘要。 |
| DEC-P188 | 所有對外資源使用 PublicId，包括會員、管理員、商品、SKU、訂單、付款、物流、售後、案件、組裝、圖片與附件。 |
| DEC-P189 | PublicId 由 Application 產生 UUID v7，建立非叢集唯一索引；`bigint identity` 維持內部叢集主鍵。 |
| DEC-P190 | MemberProfile 與 AdminProfile 互斥；管理員若需前台購買，使用獨立會員帳號。 |
| DEC-P191 | Email、Code 等保存正規化欄位與唯一索引；只限制有效資料時採 Filtered Unique Index。 |
| DEC-P192 | Cascade Delete 只允許逐項白名單的無獨立生命週期 Owned Detail，其他關聯一律 Restrict。 |
| DEC-P193 | AuditLog 保存白名單欄位的結構化差異 JSON；個資只記已變更或遮蔽值。 |
| DEC-P194 | 使用單一 `OutboxMessages` 表，保存 Type、Payload Version、最小 JSON、處理／重試與 Correlation；不可含 Secret。 |
| DEC-P195 | IdempotencyRecord 以 Actor Scope＋Operation＋Key 唯一，保存 Request Hash、Response 摘要、狀態與 24 小時到期。 |
| DEC-P196 | 匯入預覽使用持久化 ImportBatch／ImportRow Staging，保存正規化預覽與錯誤 24 小時；提交前驗證擁有者、版本與 Hash。 |
| DEC-P197 | Inventory Import 採全批原子成功或回滾，每列建立 Adjustment Movement，預覽與提交分離。 |
| DEC-P198 | CSV Null 固定使用 `\N`，空字串保留空欄；日期用 ISO 8601，小數使用 `.`，不依本機語系猜測。 |
| DEC-P199 | 金額分攤使用 `MidpointRounding.AwayFromZero` 至 2 位，最後一筆吸收尾差；前端不得自行重算。 |
| DEC-P200 | 一般字串 Trim＋Unicode NFKC；Email 使用 Identity NormalizedEmail／Invariant 規則，不自行改寫 local-part。 |
| DEC-P201 | SKU、優惠碼等系統 Code 不分大小寫唯一，正規化為大寫保存；顯示名稱保留原值。 |
| DEC-P202 | Constraint／Index 採 `IX_`、`UX_`、`FK_`、`CK_` 命名；所有 FK 先建索引，再依查詢調整複合順序。 |
| DEC-P203 | 第一版 Brevo 使用已驗證單一寄件者 `alexyang920528@gmail.com`；取得自有網域後再另行切換。 |
| DEC-P204 | 使用專案外單一資料根目錄並依環境分子目錄；備份保留每日 7 份、每週 4 份，SQL 與檔案共用 Backup Set ID。 |

## 一致性與保留事項

- DEC-P175～P187 將既有正規化原則收束成可實作邊界；新增任何副本仍需先量測並定義同步、重建及防漂移。
- DEC-P188／P189 完成 PublicId 範圍與 Guid 策略，但逐表欄位、路由及索引仍須在資料字典與 OpenAPI 中落實。
- DEC-P190 補足單一 Identity Store 下 Profile 的互斥規則，不改變會員／管理員分離 Cookie 與 Policy。
- DEC-P192 只決定白名單策略；實際可 Cascade 的關聯仍需逐表列出，未列者維持 Restrict。
- DEC-P193～P197 完成稽核、Outbox、冪等與匯入暫存方向；精確欄位、保存期限與 API 仍待詳細設計。
- DEC-P203 取代 DEC-P173 未能成立的自有 Gmail 網域方案，正式改採 Brevo 已驗證單一寄件者。
- DEC-P204 固定備份保留政策，但實際絕對資料根目錄與磁碟代號仍需在展示電腦確認後設定。

## 已寫回文件

- [[01-需求/核心商業規則]]
- [[02-領域需求/02-商品庫存與組裝/商品、組裝與相容性]]
- [[02-領域需求/03-交易與履約/優惠券規則]]
- [[02-領域需求/02-商品庫存與組裝/庫存規則]]
- [[02-領域需求/01-會員與身分/會員、驗證與通知]]
- [[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]
- [[03-架構/02-API與前端契約/API共通規範]]
- [[03-架構/03-資料與一致性/資料模型與ERD]]
- [[03-架構/03-資料與一致性/資料字典索引]]
- [[03-架構/03-資料與一致性/資料庫正規化與反正規化策略]]
- [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]
- [[03-架構/03-資料與一致性/匯入暫存與庫存調整設計]]
- [[03-架構/05-背景工作與維運/備份與復原策略]]
- [[03-架構/01-系統與環境/系統架構]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/需求追蹤矩陣]]

## 追蹤結果

- 本批沒有僅靠決策即可完全結案的實作項目。
- 已解除待確認並轉進行中：`TECH-04`。
- 已縮小但仍進行中：`DES-07`、`DES-08`、`DES-10`、`DES-13`、`DES-15`、`DOM-10`、`TECH-09`、`TECH-11`、`DATA-01`、`QA-07`、`DEV-02`。
- 新增：資料一致性／Outbox／冪等與匯入 Staging 的實作追蹤內容，沿用 `TECH-09`、`TECH-11`、`DOM-10` 及 `DES-08`。
- 下一批仍需決策：Owned Cascade 白名單、PublicId 路由格式、規格 Option 模型、Staging 欄位與清理、Outbox／Audit 保存期限、資料根目錄實際設定等。
