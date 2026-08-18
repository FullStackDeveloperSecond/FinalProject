---
文件狀態: 已確認
最後更新: 2026-08-18
追蹤項目:
  - DES-17
  - DES-18
  - DES-19
  - DES-20
---

# 資料表實作交付索引

本資料夾收束 Haru、Kafen、Terry、Yinyin 的資料表上繳稿，套用 DEC-BATCH-012／013 與既有正式規格後，作為 Owner 建立 Entity／Fluent Configuration 的欄位級交付文件。

| Owner | 最終交付 | 範圍 | 狀態 |
|---|---|---|---|
| Haru | [[03-架構/資料表實作交付/Haru-會員登入訂單與訪客存取最終Schema]] | Identity Profile、地址、收藏、訂單、Guest 存取、組裝工作與歷程 | 待 yinyin 交叉覆核 |
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

- Owner 逐表建立 Domain Entity 與 Infrastructure Fluent Configuration。
- 跨模組只建立已定版 FK 或 Application Query／DTO，不直接使用他人 Repository／DbContext。
- 對 MutableEntity、Append-only Entity、Join、CodeLookup 及私有附件套用對應 Profile。
- 完成後先由固定備援者核對欄位、狀態、索引、Constraint、Delete Behavior 與交易不變量。
- 通過跨模組 Review 後，才可由獨立 Migration 流程產生待審 Migration；不得因 DES-17～DES-20 的 Schema 文件完成就直接更新資料庫。

## 仍未被本批完成的工作

- EF Core Entity 與 Fluent Configuration 實作。
- 跨模組 FK 與 Application Query／DTO 實作。
- 交易、併發、冪等、授權與資料完整性整合測試。
- Migration 產生、SQL 審閱與資料庫套用。
