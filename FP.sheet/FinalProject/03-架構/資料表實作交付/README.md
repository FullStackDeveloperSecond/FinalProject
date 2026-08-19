---
文件狀態: 已確認
最後更新: 2026-08-19
追蹤項目:
  - DES-17
  - DES-18
  - DES-19
  - DES-20
  - DES-21
---

# 資料表實作交付索引

本資料夾收束 Haru、Kafen、Terry、Yinyin 的資料表上繳稿，套用 DEC-BATCH-012～014 與既有正式規格後，作為 Owner 建立 Entity／Fluent Configuration 的欄位級交付文件。

| Owner | 最終交付 | 範圍 | 狀態 |
|---|---|---|---|
| Haru | [[03-架構/資料表實作交付/Haru-會員登入訂單與訪客存取最終Schema]] | Identity Profile、地址、收藏、訂單、Guest 存取、組裝工作與歷程 | 已確認 |
| Kafen | [[03-架構/資料表實作交付/Kafen-客服售後與檢舉最終Schema]] | 客服、SLA、退貨、退貨寄回、檢舉、統一案件工作台來源 | 已確認 |
| Terry | [[03-架構/資料表實作交付/Terry-商品庫存物流組裝與報表最終Schema]] | 商品、匯入、購物車、庫存、出貨、組裝、相容性、評價、報表 | 已確認 |
| Yinyin | [[03-架構/資料表實作交付/Yinyin-優惠券付款退款與發票最終Schema]] | 優惠券、付款事件、部分退款、模擬發票與折讓 | 已確認 |

## 文件權威順序

1. [[03-架構/資料字典索引]] 與三份領域資料字典。
2. [[03-架構/狀態機設計]]、API Endpoint／DTO／錯誤碼正式目錄及領域規格。
3. 本資料夾的 Owner 欄位級實作交付。
4. 組員原始上繳稿與歷史缺失報告。

本資料夾不是另一套資料字典。若交付稿和前兩層文件衝突，Owner 必須依正式文件修正 Entity／Configuration，不能反向以交付稿覆蓋正式規格。

## 使用方式

- 全案採單一 `DoSelectDbContext`／單一 Migration 歷程；Entity 與 Fluent Configuration 依 Owner 模組分資料夾，不建立每模組獨立 DbContext。
- Domain Entity 統一採公開 getter／private setter；建立、狀態轉移與可變欄位更新只能經具名方法，Configuration 不得把公開 setter 當成實作捷徑。
- Owner 逐表建立 Domain Entity 與 Infrastructure Fluent Configuration。
- 跨模組只建立已定版 FK 或 Application Query／DTO，不直接使用他人 Repository／DbContext。
- 對 MutableEntity、Append-only Entity、Join、CodeLookup 及私有附件套用對應 Profile。
- 完成後先由固定備援者核對欄位、狀態、索引、Constraint、Delete Behavior 與交易不變量。
- 通過跨模組 Review 後，才可由獨立 Migration 流程產生待審 Migration；不得因 DES-17～DES-20 的 Schema 文件完成就直接更新資料庫。

## 仍未被本批完成的工作

- EF Core Entity 與 Fluent Configuration 實作：Haru、Terry、Yinyin、Kafen 四個 Owner 範圍均已完成並通過模型測試。
- 跨模組 FK 與 Application Query／DTO 實作。
- 交易、併發、冪等、授權與資料完整性整合測試。
- Migration 已產生、完成靜態 SQL 審閱並套用至本機 `DoSelectDb`；93 張表、315 個索引、View 12 欄及 Migration History 已驗證。仍待 Down／空庫重新建立演練與專用 Provider-backed 整合測試資料庫。
- DEC-BATCH-014 在 Initial Migration 後新增 `OrderCoupons.MinimumSpendAmount`、`OrderItems.IsCouponEligible`、`ShippingClawback` 與 Coupon 狀態方法要求；DES-21 完成前，現有 Model／Migration 仍不具完整退貨優惠重算能力。

## EF Core 實作進度

| Owner | Entity／Configuration | 驗證 | 尚待事項 |
|---|---|---|---|
| Haru | 已完成 Identity 擴充與 11 張自有資料表 | 模型、Migration 結構與本機 SQL 套用驗證通過 | 初始 Migration 已納入；DES-21 待補 `OrderItems.IsCouponEligible`、測試與後續 Migration |
| Terry | 已完成 42 張資料表 | 模型、Migration 結構與本機 SQL 套用驗證通過 | 初始 Migration 已納入；待交易／併發整合測試 |
| Yinyin | 已完成 14 張優惠券、付款、退款與模擬發票資料表 | 模型、Migration 結構與本機 SQL 套用驗證通過 | 初始 Migration 已納入；DES-21 待補 `MinimumSpendAmount`、退款方向、Coupon 狀態、SQL Server 查詢測試與後續 Migration |
| Kafen | 已完成 19 張客服、退貨、檢舉資料表與 1 個唯讀工作台 View | 模型、Migration 結構、View 12 欄與本機 SQL 套用驗證通過 | Return Priority、View SQL 與初始 Migration 已納入；待工作台資料列與 SLA 情境整合測試 |
