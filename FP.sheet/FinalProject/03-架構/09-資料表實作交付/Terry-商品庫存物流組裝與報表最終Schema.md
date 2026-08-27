---
文件狀態: 已確認
最後更新: 2026-08-18
負責人: terry
追蹤項目:
  - DES-18
依據決策:
  - DEC-P250
  - DEC-P252
  - DEC-P253
  - DEC-P254
---

# Terry｜商品、購物車、庫存、物流、組裝、評價與報表最終 Schema 實作交付

工作包：M-03 商品／SKU／目錄、M-04 商品批次操作與 Excel、M-05 一般商品搜尋與篩選、M-06 購物車、M-10 庫存保留／併發／逾時取消、M-11 物流／運費／門市／批次出貨、M-15 營運報表、M-16 自由組裝電腦、M-17 零件相容性引擎與後台、S-02 評價與審核、INT-04 售後與報表核心 E2E，共 42 張可寫資料表；M-15 使用查詢投影，不另建第二套真實來源。

> 權威順序：本文件是 Owner 欄位級實作交付；若與 [[03-架構/03-資料與一致性/資料字典索引]]、三份領域資料字典、[[02-領域需求/05-AI與報表/報表與展示資料]] 或 API 正式目錄衝突，以正式文件為準。本文件完成不代表核准建立或套用 Migration。


共通說明：`Id` 一律 `bigint identity` 叢集 PK、內部使用；`PublicId` 一律 `uniqueidentifier`，由應用層產生 UUID v7、無資料庫預設值；`CreatedAtUtc`／`UpdatedAtUtc` 由應用層寫入 UTC，無資料庫預設值；`RowVersion` 為 SQL Server `rowversion`，由資料庫自動維護；所有 FK 預設 `Restrict`，僅 `CartItems`、`BuildListItems`、`ImportRows` 屬 Cascade 白名單。

---

## M-03｜商品、SKU 與目錄（19 張表）

**型錄主資料**

### 1. Brands

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵，不對外暴露 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生 UUID v7） |  | `UX_Brands_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無（應用層寫入） |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無（應用層寫入） |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `Code` | nvarchar(64) | 否 | 無 |  | `UX_Brands_Code` | Trim＋NFKC＋大寫，不分大小寫唯一 |
| `NameZhTw` | nvarchar(160) | 否 | 無 |  |  | 品牌顯示名稱（繁中） |
| `IsActive` | bit | 否 | 1 |  |  | 停用代替刪除 |
| `SortOrder` | int | 否 | 0 |  |  | 人工顯示排序 |
| `Description` | nvarchar(1000) | 是 | 無 |  |  | 品牌介紹 |
| `WebsiteUrl` | nvarchar(2048) | 是 | 無 |  |  | 官網連結 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Brands—Products | 1:N | `Products.BrandId` | Restrict | 品牌被商品引用時不可刪除；以停用取代 |
| Brands—BrandTranslations | 1:N | `BrandTranslations.BrandId` | Restrict | 同一 Brand＋Locale 只能有一筆翻譯 |

---

### 2. Categories

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_Categories_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `Code` | nvarchar(64) | 否 | 無 |  | `UX_Categories_Code` | Trim＋NFKC＋大寫唯一 |
| `NameZhTw` | nvarchar(160) | 否 | 無 |  |  | 分類顯示名稱 |
| `IsActive` | bit | 否 | 1 |  |  | 停用代替刪除 |
| `SortOrder` | int | 否 | 0 |  |  | 人工顯示排序 |
| `ParentCategoryId` | bigint | 是 | 無 | FK→`Categories.Id` | `IX_Categories_ParentCategoryId` | 不可指向自己；需檢查防止循環 |
| `Slug` | nvarchar(120) | 否 | 無 |  | `UX_Categories_Slug` | 前台網址用 |
| `Description` | nvarchar(1000) | 是 | 無 |  |  | 分類說明 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Categories—Categories（自參照） | 1:N | `Categories.ParentCategoryId` | Restrict | 不可自己作 Parent；有子項不可刪除／不可形成循環 |
| Categories—Products | 1:N | `Products.CategoryId` | Restrict | 分類被商品引用時不可刪除 |
| Categories—SpecificationDefinitions | 1:N | `SpecificationDefinitions.CategoryId` | Restrict | 分類被規格定義引用時不可刪除 |

---

### 3. Products

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_Products_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖（後台編輯覆寫保護） |
| `ProductCode` | nvarchar(64) | 否 | 無 |  | `UX_Products_ProductCode` | 商品不可變代碼；匯入以此判斷新增／更新 |
| `BrandId` | bigint | 否 | 無 | FK→`Brands.Id` | `IX_Products_BrandId_Status` | Restrict |
| `CategoryId` | bigint | 否 | 無 | FK→`Categories.Id` | `IX_Products_CategoryId_Status` | Restrict |
| `NameZhTw` | nvarchar(160) | 否 | 無 |  |  | 商品顯示名稱 |
| `DescriptionZhTw` | nvarchar(4000) | 是 | 無 |  |  | 商品描述 |
| `WarrantyMonths` | int | 是 | 無 |  |  | 0～120 |
| `Status` | varchar(24) | 否 | 無 |  | `IX_Products_CategoryId_Status` | `Draft/Published/Unpublished/Discontinued` |
| `IsFeatured` | bit | 否 | 0 |  |  | 是否精選 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Brands—Products | N:1 | `Products.BrandId` | Restrict | 品牌必須為有效 Brand |
| Categories—Products | N:1 | `Products.CategoryId` | Restrict | 分類必須為有效 Category |
| Products—Skus | 1:N | `Skus.ProductId` | Restrict | 停用 Product 不刪除既有 SKU |
| Products—ProductTags | 1:N | `ProductTags.ProductId` | Restrict | 標籤關聯不隨 Product 停用而消失 |
| Products—ProductTranslations | 1:N | `ProductTranslations.ProductId` | Restrict | 同 Product＋Locale 唯一 |
| Products—OrderItems（唯讀關聯） | 1:N | `OrderItems.SkuId`（間接） | Restrict | 商品停用不影響既有訂單快照 |

**M-05 搜尋支援**：`IX_Products_CategoryId_Status`、`IX_Products_BrandId_Status` 供分類／品牌篩選；`NameZhTw`、`ProductCode` 為關鍵字查詢欄位（見 M-05 節詳細索引清單）。

---

### 4. Skus

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_Skus_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `SkuCode` | nvarchar(64) | 否 | 無 |  | `UX_Skus_SkuCode` | 不可變代碼；空值由系統產碼 |
| `ProductId` | bigint | 否 | 無 | FK→`Products.Id` | `IX_Skus_ProductId_Status` | Restrict |
| `NameZhTw` | nvarchar(160) | 否 | 無 |  |  | 規格／變體顯示名稱 |
| `ListPrice` | decimal(18,2) | 否 | 無 |  |  | ≥0 |
| `UnitCost` | decimal(18,2) | 否 | 無 |  |  | ≥0；只供授權後台使用 |
| `WeightKg` | decimal(10,3) | 是 | 無 |  |  | Null 或 >0 |
| `LengthCm` | decimal(10,2) | 是 | 無 |  |  | Null 或 >0 |
| `WidthCm` | decimal(10,2) | 是 | 無 |  |  | Null 或 >0 |
| `HeightCm` | decimal(10,2) | 是 | 無 |  |  | Null 或 >0 |
| `Status` | varchar(24) | 否 | 無 |  | `IX_Skus_ProductId_Status` | `Draft/Published/Unpublished` |
| `IsDefault` | bit | 否 | 0 |  | `UX_Skus_ProductId_IsDefault`（Filtered, `IsDefault=1`） | 每 Product 最多一筆有效 Default |
| `RequiresPrepayment` | bit | 否 | 0 |  |  | 此 SKU 是否強制預付；結帳 COD Eligibility 必須逐 SKU 檢查 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Products—Skus | N:1 | `Skus.ProductId` | Restrict | SKU 必須屬於有效 Product |
| Skus—InventoryBalances | 1:1 | `InventoryBalances.SkuId` | Restrict | 每個已發布 SKU 對應一筆餘額 |
| Skus—CartItems | 1:N | `CartItems.SkuId` | Restrict | SKU 停用不刪除既有購物車列（前端提示失效） |
| Skus—BuildListItems | 1:N | `BuildListItems.SkuId` | Restrict | 停用 SKU 不刪除既有組裝清單項目 |
| Skus—SkuSpecificationValues | 1:N | `SkuSpecificationValues.SkuId` | Restrict | 規格值必須對應 SKU 所屬分類定義 |
| Skus—OrderItems（唯讀） | 1:N | `OrderItems.SkuId` | Restrict | 訂單以快照欄位保留歷史，不受 SKU 變動影響 |

**組長定版修正**：`Skus` 不再保存 `SalePrice`；`SalePrices` 是 SKU 特價唯一可寫真實來源。商品／SKU 匯入模板同步移除 `sale_price` 欄位，不得再接受此輸入。

---

### 5. Tags

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_Tags_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `Code` | nvarchar(64) | 否 | 無 |  | `UX_Tags_Code` | Trim＋NFKC＋大寫唯一 |
| `NameZhTw` | nvarchar(160) | 否 | 無 |  |  | 標籤顯示名稱 |
| `IsActive` | bit | 否 | 1 |  |  | 停用代替刪除 |
| `SortOrder` | int | 否 | 0 |  |  | 顯示排序 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Tags—ProductTags | 1:N | `ProductTags.TagId` | Restrict | 標籤被引用時不可刪除，以停用取代 |

---

### 6. ProductTags

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `ProductId` | bigint | 否 | 無 | PK（複合）／FK→`Products.Id` | 複合 PK `(ProductId,TagId)` | Restrict |
| `TagId` | bigint | 否 | 無 | PK（複合）／FK→`Tags.Id` | `IX_ProductTags_TagId_ProductId` | Restrict |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Products—ProductTags | 1:N | `ProductTags.ProductId` | Restrict | 商品刪除前需先處理關聯（第一版不刪商品） |
| Tags—ProductTags | 1:N | `ProductTags.TagId` | Restrict | 標籤刪除前需先處理關聯 |

**M-05 搜尋支援**：複合 PK `(ProductId,TagId)` 支援「商品→標籤」查詢；新增 `IX_ProductTags_TagId_ProductId` 反向索引支援「依標籤找商品」查詢。

---

**多語系內容**

共通說明：以下六表均為 `Id bigint identity` PK（無 PublicId，屬翻譯內容附屬資料）、父表 FK、`Locale varchar(10)`（僅接受 `zh-TW/ja-JP/ko-KR`）、`TranslationStatus varchar(16)`（`MachineDraft/Reviewed/Published`）、`ReviewedByAdminUserId nvarchar(450)` NULL、`ReviewedAtUtc datetime2(3)` NULL、`CreatedAtUtc/UpdatedAtUtc datetime2(3)`、`RowVersion rowversion`；每個父實體＋Locale 唯一。父資料停用不 Cascade 刪除翻譯。

### 7. BrandTranslations

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `BrandId` | bigint | 否 | 無 | FK→`Brands.Id` | `UX_BrandTranslations_BrandId_Locale` | Restrict |
| `Locale` | varchar(10) | 否 | 無 |  | `UX_BrandTranslations_BrandId_Locale` | `zh-TW/ja-JP/ko-KR` |
| `Name` | nvarchar(160) | 否 | 無 |  |  | 翻譯後品牌名稱 |
| `Description` | nvarchar(1000) | 是 | 無 |  |  | 翻譯後介紹 |
| `TranslationStatus` | varchar(16) | 否 | 無 |  |  | `MachineDraft/Reviewed/Published` |
| `ReviewedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 審核者 |
| `ReviewedAtUtc` | datetime2(3) | 是 | 無 |  |  | 審核時間 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Brands—BrandTranslations | 1:N | `BrandTranslations.BrandId` | Restrict | 同 Brand＋Locale 唯一；只有 `Published` 可作前台內容 |

---

### 8. CategoryTranslations

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `CategoryId` | bigint | 否 | 無 | FK→`Categories.Id` | `UX_CategoryTranslations_CategoryId_Locale` | Restrict |
| `Locale` | varchar(10) | 否 | 無 |  | `UX_CategoryTranslations_CategoryId_Locale` | `zh-TW/ja-JP/ko-KR` |
| `Name` | nvarchar(160) | 否 | 無 |  |  | 翻譯後分類名稱 |
| `Description` | nvarchar(1000) | 是 | 無 |  |  | 翻譯後說明 |
| `TranslationStatus` | varchar(16) | 否 | 無 |  |  | `MachineDraft/Reviewed/Published` |
| `ReviewedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 審核者 |
| `ReviewedAtUtc` | datetime2(3) | 是 | 無 |  |  | 審核時間 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Categories—CategoryTranslations | 1:N | `CategoryTranslations.CategoryId` | Restrict | 同 Category＋Locale 唯一 |

---

### 9. ProductTranslations

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `ProductId` | bigint | 否 | 無 | FK→`Products.Id` | `UX_ProductTranslations_ProductId_Locale` | Restrict |
| `Locale` | varchar(10) | 否 | 無 |  | `UX_ProductTranslations_ProductId_Locale` | `zh-TW/ja-JP/ko-KR` |
| `Name` | nvarchar(160) | 否 | 無 |  |  | 翻譯後商品名稱 |
| `Description` | nvarchar(4000) | 是 | 無 |  |  | 翻譯後描述 |
| `TranslationStatus` | varchar(16) | 否 | 無 |  |  | `MachineDraft/Reviewed/Published` |
| `ReviewedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 審核者 |
| `ReviewedAtUtc` | datetime2(3) | 是 | 無 |  |  | 審核時間 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Products—ProductTranslations | 1:N | `ProductTranslations.ProductId` | Restrict | 同 Product＋Locale 唯一；缺值依目前語言→繁中→穩定 Key 降級 |

---

### 10. SkuTranslations

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | `UX_SkuTranslations_SkuId_Locale` | Restrict |
| `Locale` | varchar(10) | 否 | 無 |  | `UX_SkuTranslations_SkuId_Locale` | `zh-TW/ja-JP/ko-KR` |
| `Name` | nvarchar(160) | 否 | 無 |  |  | 翻譯後 SKU 名稱 |
| `TranslationStatus` | varchar(16) | 否 | 無 |  |  | `MachineDraft/Reviewed/Published` |
| `ReviewedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 審核者 |
| `ReviewedAtUtc` | datetime2(3) | 是 | 無 |  |  | 審核時間 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Skus—SkuTranslations | 1:N | `SkuTranslations.SkuId` | Restrict | 同 SKU＋Locale 唯一 |

---

### 11. SpecificationDefinitionTranslations

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `SpecificationDefinitionId` | bigint | 否 | 無 | FK→`SpecificationDefinitions.Id` | `UX_SpecDefTranslations_DefId_Locale` | Restrict |
| `Locale` | varchar(10) | 否 | 無 |  | `UX_SpecDefTranslations_DefId_Locale` | `zh-TW/ja-JP/ko-KR` |
| `DisplayName` | nvarchar(160) | 否 | 無 |  |  | 翻譯後欄位顯示名稱 |
| `HelpText` | nvarchar(500) | 是 | 無 |  |  | 翻譯後說明文字 |
| `TranslationStatus` | varchar(16) | 否 | 無 |  |  | `MachineDraft/Reviewed/Published` |
| `ReviewedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 審核者 |
| `ReviewedAtUtc` | datetime2(3) | 是 | 無 |  |  | 審核時間 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| SpecificationDefinitions—SpecificationDefinitionTranslations | 1:N | `SpecificationDefinitionTranslations.SpecificationDefinitionId` | Restrict | 同定義＋Locale 唯一 |

---

### 12. SpecificationOptionTranslations

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `SpecificationOptionId` | bigint | 否 | 無 | FK→`SpecificationOptions.Id` | `UX_SpecOptTranslations_OptId_Locale` | Restrict |
| `Locale` | varchar(10) | 否 | 無 |  | `UX_SpecOptTranslations_OptId_Locale` | `zh-TW/ja-JP/ko-KR` |
| `DisplayName` | nvarchar(160) | 否 | 無 |  |  | 翻譯後選項顯示名稱 |
| `TranslationStatus` | varchar(16) | 否 | 無 |  |  | `MachineDraft/Reviewed/Published` |
| `ReviewedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 審核者 |
| `ReviewedAtUtc` | datetime2(3) | 是 | 無 |  |  | 審核時間 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| SpecificationOptions—SpecificationOptionTranslations | 1:N | `SpecificationOptionTranslations.SpecificationOptionId` | Restrict | 同選項＋Locale 唯一 |

---

**圖片與授權來源**

### 13. ProductImages

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_ProductImages_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `ProductId` | bigint | 否 | 無 | FK→`Products.Id` | `IX_ProductImages_ProductId_Status_SortOrder` | Restrict |
| `SkuId` | bigint | 是 | 無 | FK→`Skus.Id` | | 若有值，SKU 必須屬於同一 Product |
| `StorageKey` | nvarchar(500) | 否 | 無 |  | `UX_ProductImages_StorageKey` | 不可由原始檔名組成 |
| `OriginalFileName` | nvarchar(255) | 否 | 無 |  |  | 只供安全顯示 |
| `MediaType` | varchar(100) | 否 | 無 |  |  | 掃描後 MIME |
| `FileSizeBytes` | bigint | 否 | 無 |  |  | 1～10 MB |
| `Width` | int | 否 | 無 |  |  | >0 |
| `Height` | int | 否 | 無 |  |  | >0 |
| `Sha256` | binary(32) | 否 | 無 |  |  | 原圖 Hash |
| `AltTextZhTw` | nvarchar(160) | 否 | 無 |  |  | 不得只填檔名 |
| `SourceUrl` | nvarchar(2048) | 是 | 無 |  |  | 外部圖片發布前必填 |
| `LicenseUrl` | nvarchar(2048) | 是 | 無 |  |  | 外部圖片發布前必填 |
| `AuthorName` | nvarchar(160) | 是 | 無 |  |  | 外部圖片發布前必填 |
| `LicenseName` | nvarchar(160) | 是 | 無 |  |  | 外部圖片發布前必填 |
| `DownloadedAtUtc` | datetime2(3) | 是 | 無 |  |  | 外部圖片發布前必填 |
| `Status` | varchar(24) | 否 | 無 |  | `IX_ProductImages_Status_DeletedAtUtc` | `Processing/Ready/Published/Rejected/PendingDelete/Deleted` |
| `SortOrder` | int | 否 | 0 |  |  | 顯示排序 |
| `PublishedAtUtc` | datetime2(3) | 是 | 無 |  |  | 依狀態限制 |
| `DeletedAtUtc` | datetime2(3) | 是 | 無 |  |  | 依狀態限制 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Products—ProductImages | 1:N | `ProductImages.ProductId` | Restrict | 圖片不隨商品刪除而消失（走生命週期狀態） |
| Skus—ProductImages | 1:N（可選） | `ProductImages.SkuId` | Restrict | `SkuId` 非 Null 時必須屬於同一 `ProductId` |

---

**動態規格**

### 14. MeasurementUnits

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_MeasurementUnits_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `Code` | nvarchar(64) | 否 | 無 |  | `UX_MeasurementUnits_Code` | 唯一 |
| `NameZhTw` | nvarchar(160) | 否 | 無 |  |  | 單位顯示名稱 |
| `IsActive` | bit | 否 | 1 |  |  | 停用代替刪除 |
| `SortOrder` | int | 否 | 0 |  |  | 顯示排序 |
| `Symbol` | nvarchar(24) | 否 | 無 |  |  | 可重複（如 W、mm） |
| `Dimension` | varchar(32) | 否 | 無 |  |  | 物理量維度分類 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| MeasurementUnits—SpecificationDefinitions | 1:N | `SpecificationDefinitions.MeasurementUnitId` | Restrict | 非 `Decimal` 型定義不得指定 Unit |

---

### 15. SpecificationDefinitions

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_SpecificationDefinitions_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `CategoryId` | bigint | 否 | 無 | FK→`Categories.Id` | `UX_SpecificationDefinitions_CategoryId_SemanticKey` | Restrict |
| `SemanticKey` | nvarchar(64) | 否 | 無 |  | `UX_SpecificationDefinitions_CategoryId_SemanticKey` | 分類內唯一，使用後不可改 |
| `DisplayNameZhTw` | nvarchar(160) | 否 | 無 |  |  | 欄位顯示名稱 |
| `ValueType` | varchar(16) | 否 | 無 |  |  | `String/Decimal/Boolean/Option`，使用後不可改 |
| `MeasurementUnitId` | bigint | 是 | 無 | FK→`MeasurementUnits.Id` | | 僅 `Decimal` 型可指定 |
| `IsRequired` | bit | 否 | 無 |  |  | 是否必填 |
| `AllowsMultiple` | bit | 否 | `0` |  |  | 僅 `ValueType=Option` 可為 1；被使用後不可改 |
| `IsProtected` | bit | 否 | 無 |  |  | 是否保護欄位（不可任意刪除） |
| `IsActive` | bit | 否 | 無 |  |  | 停用代替刪除 |
| `SortOrder` | int | 否 | 無 |  |  | 顯示排序 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Categories—SpecificationDefinitions | N:1 | `SpecificationDefinitions.CategoryId` | Restrict | SKU 所屬 Product Category 需與 Definition Category 相同（Application／整合測試保證） |
| MeasurementUnits—SpecificationDefinitions | N:1（可選） | `SpecificationDefinitions.MeasurementUnitId` | Restrict | 非 Decimal 型不得有 Unit |
| SpecificationDefinitions—SpecificationOptions | 1:N | `SpecificationOptions.SpecificationDefinitionId` | Restrict | 僅 `Option` 型定義擁有選項 |
| SpecificationDefinitions—SkuSpecificationValues | 1:N | `SkuSpecificationValues.SpecificationDefinitionId` | Restrict | 被使用後 SemanticKey／ValueType／Category／Unit／AllowsMultiple 不可變更 |
| SpecificationDefinitions—SkuSpecificationOptionSelections | 1:N（透過 Option） | `SkuSpecificationOptionSelections.SpecificationOptionId` | Restrict | 只用於 Option 多選；受保護語意契約不可漂移 |

---

### 16. SpecificationOptions

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_SpecificationOptions_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `SpecificationDefinitionId` | bigint | 否 | 無 | FK→`SpecificationDefinitions.Id` | `UX_SpecificationOptions_DefinitionId_Code` | Restrict |
| `Code` | nvarchar(64) | 否 | 無 |  | `UX_SpecificationOptions_DefinitionId_Code` | 被使用後不可改 |
| `DisplayNameZhTw` | nvarchar(160) | 否 | 無 |  |  | 選項顯示名稱 |
| `IsActive` | bit | 否 | 無 |  |  | 停用代替刪除 |
| `SortOrder` | int | 否 | 無 |  |  | 顯示排序 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| SpecificationDefinitions—SpecificationOptions | N:1 | `SpecificationOptions.SpecificationDefinitionId` | Restrict | 被使用後不可刪除或改 Code |
| SpecificationOptions—SkuSpecificationValues | 1:N | `SkuSpecificationValues.OptionId` | Restrict | `OptionId` 必須指向同一 Definition |

---

### 17. SpecificationSources

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_SpecificationSources_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `SourceType` | varchar(24) | 否 | 無 |  |  | `Manufacturer/CuratedReference/SystemEstimate` |
| `ProviderName` | nvarchar(160) | 否 | 無 |  |  | 資料提供者 |
| `SourceUrl` | nvarchar(2048) | 否 | 無 |  | `IX_SpecificationSources_Url_Provider_Version` | 來源網址 |
| `OriginalFieldName` | nvarchar(160) | 是 | 無 |  |  | 原始欄位名稱 |
| `RetrievedAtUtc` | datetime2(3) | 否 | 無 |  |  | 擷取時間 |
| `ReviewedAtUtc` | datetime2(3) | 否 | 無 |  |  | 審核時間 |
| `ReviewedByAdminUserId` | nvarchar(450) | 否 | 無 |  |  | 人工備援必須有 Reviewer |
| `Note` | nvarchar(1000) | 是 | 無 |  |  | 備註 |
| `SourceVersion` | nvarchar(64) | 否 | 無 |  | `IX_SpecificationSources_Url_Provider_Version` | 來源版本 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| SpecificationSources—SkuSpecificationValues | 1:N | `SkuSpecificationValues.SpecificationSourceId` | Restrict | 參與功耗／PSU 等硬性相容規則之值必須有來源；核心來源缺失不得以 Null 當 0 或由 AI 猜值 |

---

### 18. SkuSpecificationValues

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵（此表不配置 PublicId，無獨立 Route） |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | `UX_SkuSpecificationValues_SkuId_SpecificationDefinitionId` | Restrict |
| `SpecificationDefinitionId` | bigint | 否 | 無 | FK→`SpecificationDefinitions.Id` | `UX_SkuSpecificationValues_SkuId_SpecificationDefinitionId` | Restrict |
| `StringValue` | nvarchar(500) | 是 | 無 |  |  | `String` 型時唯一非 Null 值欄 |
| `DecimalValue` | decimal(18,4) | 是 | 無 |  | `IX_SkuSpecificationValues_DefinitionId_DecimalValue` | `Decimal` 型時唯一非 Null 值欄；索引支援規格數值範圍篩選（如功耗、容量區間） |
| `BooleanValue` | bit | 是 | 無 |  |  | `Boolean` 型時唯一非 Null 值欄 |
| `OptionId` | bigint | 是 | 無 | FK→`SpecificationOptions.Id` | `IX_SkuSpecificationValues_DefinitionId_OptionId` | `Option` 型時唯一非 Null 值欄；須指向同一 Definition |
| `SpecificationSourceId` | bigint | 是 | 無 | FK→`SpecificationSources.Id` | | 硬性相容規則所需值必填 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Skus—SkuSpecificationValues | 1:N | `SkuSpecificationValues.SkuId` | Restrict | 每 SKU＋定義只有一筆值 |
| SpecificationDefinitions—SkuSpecificationValues | 1:N | `SkuSpecificationValues.SpecificationDefinitionId` | Restrict | SKU 所屬 Category 須與 Definition Category 相同 |
| SpecificationOptions—SkuSpecificationValues | 1:N（可選） | `SkuSpecificationValues.OptionId` | Restrict | 四個值欄恰有一個非 Null（Check Constraint） |
| SpecificationSources—SkuSpecificationValues | 1:N（可選） | `SkuSpecificationValues.SpecificationSourceId` | Restrict | 硬性相容規則值缺來源時不得由 AI／預設值補上 |

---

### 18A. SkuSpecificationOptionSelections

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `SkuId` | bigint | 否 | 無 | PK、FK→`Skus.Id` | `UX_SkuSpecificationOptionSelections_SkuId_OptionId` | Restrict |
| `SpecificationOptionId` | bigint | 否 | 無 | PK、FK→`SpecificationOptions.Id` | `UX_SkuSpecificationOptionSelections_SkuId_OptionId` | 同 SKU 不可重複選同一 Option |
| `SpecificationSourceId` | bigint | 是 | 無 | FK→`SpecificationSources.Id` | `IX_SkuSpecificationOptionSelections_SourceId` | 硬性相容規則使用的每個選項必填已覆核來源 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |

此 Join Entity 只表達 `ValueType=Option && AllowsMultiple=1`。Option 必須屬於 SKU Category 的 Definition；多選資料不得保存於 `SkuSpecificationValues.OptionId`、逗號字串或 JSON。刪除行為均為 Restrict，移除選項必須走明確管理 Use Case。

---

**特價**

### 19. SalePrices

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_SalePrices_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | `IX_SalePrices_SkuId_StartsAtUtc_EndsAtUtc` | Restrict |
| `Price` | decimal(18,2) | 否 | 無 |  |  | ≥0 |
| `StartsAtUtc` | datetime2(3) | 否 | 無 |  | `IX_SalePrices_SkuId_StartsAtUtc_EndsAtUtc` | 起始時間 |
| `EndsAtUtc` | datetime2(3) | 否 | 無 |  | `IX_SalePrices_SkuId_StartsAtUtc_EndsAtUtc` | 須大於 `StartsAtUtc` |
| `Status` | varchar(16) | 否 | 無 |  |  | `Draft/Scheduled/Active/Cancelled/Expired` |
| `CreatedByAdminUserId` | nvarchar(450) | 否 | 無 |  |  | 建立人員 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Skus—SalePrices | 1:N | `SalePrices.SkuId` | Restrict | 同 SKU 有效期間不可重疊（Serializable Transaction／整合測試證明只接受一筆） |

**組長定版修正**：`SalePrices` 是 SKU 特價「唯一」可寫真實來源，`Skus` 不再重複保存特價；第一版不建立 `Promotions.SpecialPrice` 等第二個價格來源。訂單成交後由 `OrderItems`（haru 負責）保存原價、特價與最終成交價快照，本表不回溯提供歷史價格。

---

## M-04｜商品批次操作與 Excel（2 張表）

### 20. ImportBatches

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生 UUID v7） |  | `UX_ImportBatches_PublicId` | 對外識別碼 |
| `ImportType` | varchar(24) | 否 | 無 |  | `UX_ImportBatches_CreatedByAdminUserId_ImportType`（Filtered） | `Product/InventoryAdjustment` |
| `TemplateVersion` | int | 否 | 無 |  |  | 模板版本 |
| `Status` | varchar(24) | 否 | 無 |  | `IX_ImportBatches_Status_ExpiresAtUtc` | `Uploaded/Validating/Ready/Invalid/Committing/Committed/Failed/Expired` |
| `CreatedByAdminUserId` | nvarchar(450) | 否 | 無 |  | `UX_ImportBatches_CreatedByAdminUserId_ImportType`（Filtered） | 建立管理員 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `ExpiresAtUtc` | datetime2(3) | 否 | 無 |  | `IX_ImportBatches_Status_ExpiresAtUtc` | 預覽資料保存 24 小時 |
| `SourceFileHash1` | binary(32) | 是 | 無 |  |  | 來源檔案一 Hash（商品匯入 Products；庫存調整使用此組） |
| `SourceFileHash2` | binary(32) | 是 | 無 |  |  | 來源檔案二 Hash（Skus 表，庫存匯入為 Null） |
| `SourceFileHash3` | binary(32) | 是 | 無 |  |  | 來源檔案三 Hash（Specifications 表，庫存匯入為 Null） |
| `SourceFileNameDisplay1` | nvarchar(255) | 是 | 無 |  |  | 安全顯示檔名 |
| `SourceFileNameDisplay2` | nvarchar(255) | 是 | 無 |  |  | 安全顯示檔名 |
| `SourceFileNameDisplay3` | nvarchar(255) | 是 | 無 |  |  | 安全顯示檔名 |
| `RowCount` | int | 否 | 0 |  |  | 商品匯入：`Products`＋`Skus`＋`Specifications` 三份資料集**合計**最多 5,000 列（非各自 5,000）；庫存調整：單一資料集最多 5,000 列 |
| `NewCount` | int | 否 | 0 |  |  | 新增統計 |
| `UpdatedCount` | int | 否 | 0 |  |  | 更新統計 |
| `UnchangedCount` | int | 否 | 0 |  |  | 無變更統計 |
| `ErrorCount` | int | 否 | 0 |  |  | 錯誤統計 |
| `NormalizedContentVersion` | int | 否 | 0 |  |  | 正規化後內容版本 |
| `ConfirmedAtUtc` | datetime2(3) | 是 | 無 |  |  | 提交完成時間 |
| `ResultSummaryJson` | nvarchar(4000) | 是 | 無 |  |  | 結果摘要，具 Schema Version |
| `CorrelationId` | uniqueidentifier | 否 | 無（應用層產生） |  |  | 追蹤關聯 ID |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | Confirm 前需驗證 RowVersion 未變更 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ImportBatches—ImportRows | 1:N | `ImportRows.ImportBatchId` | Cascade（白名單） | Committed 批次不可重送；Failed 需以新 Batch 重建 |
| ImportBatches—Products／Skus／SpecificationDefinitions（間接） | N:1 | Application 層引用穩定匯入鍵 | 不建立正式 FK | 提交時重新驗證 Brand／Category／Definition／SKU 目前狀態 |
| ImportBatches—InventoryBalances（庫存匯入間接） | N:1 | Application 層以 SkuCode 對應 | 不建立正式 FK | Commit 需檢查 Preview 時 Balance 的 RowVersion，變動則整批拒絕 |

**Import Staging 流程說明**：

- **Preview**：上傳時計算並保存 `SourceFileHash1~3`（每份來源檔）、`TemplateVersion`、`NormalizedContentVersion`；三份 Hash 任一在 Confirm 時與 Preview 不同即整批拒絕，要求重新 Preview。
- **Confirm 驗證**：需同時核對 (1) 呼叫者為建立者本人或具 `CatalogImport.ReadAll`／`InventoryAdjust.Execute` 權限、(2) 傳入 `RowVersion` 與目前列一致（樂觀鎖）、(3) `ExpiresAtUtc` 尚未到期、(4) `Status` 仍為 `Ready`。任一條件不成立即拒絕，不部分執行。
- **Committed 不可重送**：`Status=Committed` 後再次 Confirm 一律回 `409 import_already_committed`；不允許以相同 Batch 疊加提交。
- **Failed 需建立新 Batch**：提交失敗（含回滾）的 Batch 標記 `Failed` 並保留錯誤摘要 90 天；修正後的資料必須以全新 `ImportBatches` 列重新走 Preview，不可修改並重試同一 Batch。
- **整批回滾**：Commit 在單一 SQL Transaction 內執行全部列的寫入；任一列違反約束（唯一鍵、FK、CHECK）立即中止並回滾整個 Transaction，不產生部分成功的 Product／SKU／Inventory 異動。
- **清理排程**：`ImportRows`（含 `RawJson`／`NormalizedPayloadJson`）於 Confirm 後最多保存 24 小時，由背景 `maintenance` Queue 清理；`ImportBatches` 統計摘要保存 90 天供稽核查詢。
- **商品匯入與庫存調整分屬不同 Use Case**：兩者驗證規則、Policy（`CatalogImport.Execute` vs `InventoryAdjust.Execute`）、影響的資料表與回滾邊界完全不同，不可合併為單一 Use Case 或共用同一組 Preview／Confirm Command。

---

### 21. ImportRows

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵（無獨立 PublicId，隨 Batch 生命週期） |
| `ImportBatchId` | bigint | 否 | 無 | FK→`ImportBatches.Id` | `UX_ImportRows_ImportBatchId_Dataset_SourceRowNumber` | Cascade（白名單） |
| `Dataset` | varchar(32) | 否 | 無 |  | 同上索引 | `Products/Skus/Specifications/InventoryAdjustments` |
| `SourceRowNumber` | int | 否 | 無 |  | 同上索引 | 來源列號，供錯誤定位 |
| `ImportKey` | nvarchar(64) | 否 | 無 |  | `UX_ImportRows_ImportBatchId_Dataset_ImportKey` | 穩定匯入鍵（`product_key`／`sku_key`等），批次＋Dataset 內唯一（實際 Unique Constraint，非僅說明） |
| `Action` | varchar(16) | 否 | 無 |  |  | `Insert/Update/NoChange/Error` |
| `NormalizedPayloadJson` | nvarchar(max) | 否 | 無 |  |  | 正規化後最小 Payload，具 Schema Version；Application 限制 32 KB／列 |
| `ErrorCodes` | nvarchar(2000) | 是 | 無 |  |  | 穩定錯誤碼清單 |
| `RowHash` | binary(32) | 否 | 無 |  |  | 供 Hash 比對變更 |
| `RawJson` | nvarchar(max) | 是 | 無 |  |  | Application 限制 32 KB／列；只供短期預覽及除錯 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ImportBatches—ImportRows | 1:N | `ImportRows.ImportBatchId` | Cascade（白名單） | Batch 刪除／過期清理時明細一併清理 |
| ImportRows—Products／Skus（提交後，間接） | N:1 | Application 層以 `ImportKey` 對應 | 不建立正式 FK | 提交在單一 SQL Transaction 內完成，任一列失敗整批回滾 |

**組長定版修正**：欄位正式定名為 `Dataset varchar(32)`（非 `DatasetType`）、`RawJson`（非 `RawPayloadJson`）；`ErrorCodes` 為 `nvarchar(2000)`；`NormalizedPayloadJson` 為 `nvarchar(max)`（與 `RawJson` 一樣由 Application 各自限制 32 KB／列）。`(ImportBatchId, Dataset, SourceRowNumber)` 與 `(ImportBatchId, Dataset, ImportKey)` 均為實際 Unique Constraint。

---

## M-05｜一般商品搜尋與篩選（0 張表）

> 無獨立資料表。直接查詢 M-03 的 `Products`、`Skus`、`SkuSpecificationValues`、`ProductTags`、`InventoryBalances`、`SalePrices` 等表，依下列候選索引與查詢方式建立；不建立獨立搜尋快取或彙總表。

**候選索引與對應查詢**：

| # | 查詢情境 | 索引 | 所在表 |
|---:|---|---|---|
| 1 | 分類＋上架狀態列表 | `IX_Products_CategoryId_Status` | `Products` |
| 2 | 品牌＋上架狀態列表 | `IX_Products_BrandId_Status` | `Products` |
| 3 | 依商品找 SKU＋狀態 | `IX_Skus_ProductId_Status` | `Skus` |
| 4 | 有效售價範圍篩選 | `IX_SalePrices_SkuId_StartsAtUtc_EndsAtUtc`（篩出 `Status=Active` 期間內特價）＋ `Skus.ListPrice` 範圍比較 | `SalePrices`、`Skus` |
| 5 | 規格數值範圍篩選（如功耗、容量） | `IX_SkuSpecificationValues_DefinitionId_DecimalValue` | `SkuSpecificationValues` |
| 6 | 規格選項篩選（如顏色、介面） | `IX_SkuSpecificationValues_DefinitionId_OptionId` | `SkuSpecificationValues` |
| 7 | 依標籤找商品 | `IX_ProductTags_TagId_ProductId` | `ProductTags` |
| 8 | 可購買量／低庫存篩選 | `IX_InventoryBalances_AvailableQuantity` | `InventoryBalances` |
| 9 | 關鍵字查詢 | `NameZhTw`、`ProductCode`（`Products`）／`SkuCode`（`Skus`）以 `LIKE 'prefix%'` 或全文檢索比對；不對 `DescriptionZhTw` 做即時模糊比對 | `Products`、`Skus` |
| 10 | 排序穩定性 | 所有分頁查詢排序鍵末端加入 `Id`（或 `PublicId`）作穩定 Tie-breaker，避免同值分頁重複或漏資料 | 全部 |

**規模與反正規化原則**：

- 第一版不建立搜尋快取或報表彙總表，一律查詢上述正規化來源＋索引。
- 以 10,000 筆商品／SKU 展示資料量測查詢效能；量測仍超過 P95 3 秒才提出反正規化（如搜尋投影表）核准申請。
- 關鍵字查詢若後續改用全文檢索（Full-Text Index）或外部搜尋引擎，須另立設計決策，不在本版範圍。

---

## M-06｜購物車（2 張表）

### 22. Carts

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_Carts_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `OwnerUserId` | nvarchar(450) | 是 | 無 |  | `UX_Carts_OwnerUserId_Active`（Filtered，`Status='Active'`） | 與 `GuestCartKeyHash` 恰一非 Null |
| `GuestCartKeyHash` | binary(32) | 是 | 無 |  | `UX_Carts_GuestCartKeyHash_Active`（Filtered，`Status='Active'`） | 訪客購物車雜湊金鑰 |
| `Status` | varchar(16) | 否 | 無 |  |  | `Active/Converted/Expired/Abandoned` |
| `ExpiresAtUtc` | datetime2(3) | 否 | 無 |  |  | 到期時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Carts—CartItems | 1:N | `CartItems.CartId` | Cascade（白名單） | 會員／訪客同時最多一筆 `Active` 購物車 |

---

### 23. CartItems

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_CartItems_PublicId` | 對外識別碼 |
| `CartId` | bigint | 否 | 無 | FK→`Carts.Id` | `UX_CartItems_CartId_SkuId_AssemblyGroupKey` | Cascade（白名單） |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | 同上 | Restrict |
| `Quantity` | int | 否 | 無 |  |  | 1～99 |
| `AssemblyGroupKey` | uniqueidentifier | 是 | 無 |  | 同上 | 同一組裝群組的多個 SKU 共用此鍵 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Carts—CartItems | N:1 | `CartItems.CartId` | Cascade（白名單） | Cart 刪除時項目一併刪除 |
| Skus—CartItems | N:1 | `CartItems.SkuId` | Restrict | 停用 SKU 不刪除既有項目，前端提示失效 |

---

## M-10｜庫存保留、併發與逾時取消（4 張表）

### 24. InventoryBalances

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_InventoryBalances_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖（高併發保留必用） |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | `UX_InventoryBalances_SkuId` | Restrict；每 SKU 一筆 |
| `OnHandQuantity` | int | 否 | 0 |  |  | ≥0 |
| `ReservedQuantity` | int | 否 | 0 |  |  | ≥0；≤`OnHandQuantity` |
| `AvailableQuantity` | int（計算欄，`AS OnHandQuantity-ReservedQuantity PERSISTED`） | 否 | 計算產生 |  | `IX_InventoryBalances_AvailableQuantity` | 不可直接寫入 |
| `ReorderLevel` | int | 否 | 0 |  |  | ≥0 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Skus—InventoryBalances | 1:1 | `InventoryBalances.SkuId` | Restrict | `OnHand≥0`、`Reserved≥0`、`Reserved≤OnHand`、`Available=OnHand-Reserved` |
| InventoryBalances—InventoryMovements | 1:N（邏輯） | `InventoryMovements.SkuId` | Restrict | 餘額變動必須有對應 Movement 記錄（Before＋Delta＝After） |
| InventoryBalances—InventoryReservations | 1:N（邏輯） | `InventoryReservations.SkuId` | Restrict | `ReservedQuantity` 需與有效 `Active` 保留總量一致，每日核對 |

---

### 25. InventoryReservations

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_InventoryReservations_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | `IX_InventoryReservations_OrderId_SkuId` | Restrict |
| `OrderId` | bigint | 否 | 無 | FK→`Orders.Id` | `IX_InventoryReservations_OrderId_SkuId` | V1 只允許 Order-bound 保留 |
| `Quantity` | int | 否 | 無 |  |  | >0 |
| `Status` | varchar(16) | 否 | 無 |  | `IX_InventoryReservations_Status_ExpiresAtUtc` | `Active/Consumed/Released/Expired` |
| `ExpiresAtUtc` | datetime2(3) | 是 | 無 |  | 同上索引 | 付款期限到期時間 |
| `ReleasedAtUtc` | datetime2(3) | 是 | 無 |  |  | 釋放時間 |
| `ReleaseReason` | varchar(32) | 是 | 無 |  |  | 釋放原因 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Skus—InventoryReservations | N:1 | `InventoryReservations.SkuId` | Restrict | 保留對應之 SKU 必須有效 |
| Orders—InventoryReservations | N:1 | `InventoryReservations.OrderId` | Restrict | Order 刪除不 Cascade（訂單本不實體刪除） |
| InventoryReservations—InventoryMovements | 1:N | `InventoryMovements.ReservationId` | Restrict | 每次狀態轉移須產生對應 Movement |

---

### 26. InventoryMovements

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_InventoryMovements_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | `IX_InventoryMovements_SkuId_OccurredAtUtc` | Restrict |
| `ReservationId` | bigint | 是 | 無 | FK→`InventoryReservations.Id` | | 對應保留（若有） |
| `MovementType` | varchar(32) | 否 | 無 |  |  | 如 `Reserve/Release/Ship/Adjustment` 等 |
| `OnHandDelta` | int | 否 | 0 |  |  | 可為負 |
| `ReservedDelta` | int | 否 | 0 |  |  | 可為負 |
| `BeforeOnHand` | int | 否 | 無 |  |  | 異動前在庫 |
| `AfterOnHand` | int | 否 | 無 |  |  | `Before+Delta=After` |
| `BeforeReserved` | int | 否 | 無 |  |  | 異動前保留 |
| `AfterReserved` | int | 否 | 無 |  |  | `Before+Delta=After` |
| `ReasonCode` | varchar(32) | 否 | 無 |  |  | 異動原因碼 |
| `ReferenceType` | varchar(32) | 否 | 無 |  |  | 來源類型（Order／ImportBatch 等）；V1 保留不可引用 Cart |
| `ReferencePublicId` | uniqueidentifier | 是 | 無 |  |  | 來源對外識別碼 |
| `ActorUserId` | nvarchar(450) | 是 | 無 |  |  | 操作者（系統自動時為 Null） |
| `OccurredAtUtc` | datetime2(3) | 否 | 無 |  | `IX_InventoryMovements_SkuId_OccurredAtUtc` | 發生時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Skus—InventoryMovements | N:1 | `InventoryMovements.SkuId` | Restrict | 不可 Update／Delete |
| InventoryReservations—InventoryMovements | N:1（可選） | `InventoryMovements.ReservationId` | Restrict | `Before+Delta=After`（Check Constraint／Application 驗證） |
| InventoryMovements—InventoryReconciliationCases | 1:N（間接） | `InventoryReconciliationCases.ResolutionMovementId` | Restrict | 核對差異修正須建立新 Movement 並回連案件 |

**必須修正｜Inventory 三類同交易流程說明**：

三個流程都在**單一 SQL Transaction**內完成，且對 `InventoryBalances` 的更新一律使用**條件式 `UPDATE`**（`WHERE SkuId=@id AND RowVersion=@rv`），不得先 `SELECT` 讀出再無條件寫回；受影響列數為 0 視為併發衝突，整個 Transaction 回滾並回報衝突供上層重試。

**建立保留**（Checkout 建立 Order 後，在同一交易建立保留）：

1. 以 `InventoryBalances.SkuId` 上鎖或條件更新方式檢查 `AvailableQuantity ≥ 需求量`。
2. 建立 `InventoryReservation`（`Status=Active`）。
3. 條件更新 `InventoryBalances.ReservedQuantity += 數量`（帶 `RowVersion` 檢查）。
4. 建立對應 `InventoryMovement`（`MovementType=Reserve`，`Before/After` 完整記錄）。
5. 提交；若步驟 1 或 3 的條件不成立，整個 Transaction 回滾並回報「庫存不足」或「併發衝突」。

**釋放保留**（取消、逾時、換購等）：

1. 條件更新 `InventoryReservation.Status`：`Active→Released` 或 `Active→Expired`（帶 `RowVersion` 檢查，非 `Active` 不得再轉移）。
2. 條件更新 `InventoryBalances.ReservedQuantity -= 數量`。
3. 建立對應 `InventoryMovement`（`MovementType=Release`）。
4. 寫入 `ReleasedAtUtc`、`ReleaseReason`。
5. 提交。

**出貨扣庫存**（批次出貨／單筆出貨建立 `Shipment` 時）：

1. 條件更新 `InventoryReservation.Status`：`Active→Consumed`（非 `Active` 不得出貨）。
2. 同一交易內條件更新 `InventoryBalances`：`OnHandQuantity -= 數量`、`ReservedQuantity -= 數量`。
3. 建立對應 `InventoryMovement`（`MovementType=Ship`）。
4. 建立／更新 `Shipment` 與訂單、出貨狀態所需事件（依 M-11 批次出貨流程）。
5. 提交；任一步驟條件不成立（保留非 Active、RowVersion 衝突）則整批該筆訂單回滾，不影響同批次其他訂單。

---

### 27. InventoryReconciliationCases

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_InventoryReconciliationCases_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | `IX_InventoryReconciliationCases_Status_DetectedAtUtc` | Restrict |
| `Status` | varchar(24) | 否 | 無 |  | `UX_InventoryReconciliationCases_SkuId_Open`（Filtered，`Status='Open'`） | `Open/Acknowledged/Resolved/Dismissed`；同 SKU 最多一個 Open |
| `ExpectedOnHand` | int | 否 | 無 |  |  | 期望在庫 |
| `ActualOnHand` | int | 否 | 無 |  |  | 實際在庫 |
| `ExpectedReserved` | int | 否 | 無 |  |  | 期望保留 |
| `ActualReserved` | int | 否 | 無 |  |  | 實際保留 |
| `DetectedAtUtc` | datetime2(3) | 否 | 無 |  | `IX_InventoryReconciliationCases_Status_DetectedAtUtc` | 發現時間 |
| `AcknowledgedBy` | nvarchar(450) | 是 | 無 |  |  | 確認人員 |
| `ResolvedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 結案人員 |
| `ResolutionMovementId` | bigint | 是 | 無 | FK→`InventoryMovements.Id` | | 結案對應之修正 Movement |
| `ResolutionReason` | nvarchar(1000) | 是 | 無 |  |  | `Dismissed` 須說明核對基準錯誤原因 |
| `ResolvedAtUtc` | datetime2(3) | 是 | 無 |  |  | 結案時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Skus—InventoryReconciliationCases | N:1 | `InventoryReconciliationCases.SkuId` | Restrict | 同 SKU 同時最多一筆 `Open` 案件 |
| InventoryMovements—InventoryReconciliationCases | N:1（可選） | `InventoryReconciliationCases.ResolutionMovementId` | Restrict | `Resolved` 需連結修正 Movement；`Dismissed` 須有說明理由 |

---

## M-11｜物流、運費、門市與批次出貨（6 張表）

### 28. ShippingMethods

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_ShippingMethods_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `Code` | nvarchar(64) | 否 | 無 |  | `UX_ShippingMethods_Code` | 唯一 |
| `NameZhTw` | nvarchar(160) | 否 | 無 |  |  | 顯示名稱 |
| `IsActive` | bit | 否 | 1 |  |  | 停用代替刪除 |
| `SortOrder` | int | 否 | 0 |  |  | 顯示排序 |
| `Kind` | varchar(24) | 否 | 無 |  |  | 宅配／超取類型 |
| `BaseFee` | decimal(18,2) | 否 | 無 |  |  | ≥0 |
| `FreeShippingThreshold` | decimal(18,2) | 是 | 無 |  |  | 免運門檻 |
| `AllowsCod` | bit | 否 | 無 |  |  | 配送方式的 COD **能力上限**，非最終授權；組裝電腦宅配方式固定為 0 |
| `RequiresPrepayment` | bit | 否 | 無 |  |  | 配送方式是否強制預付；組裝電腦宅配方式固定為 1 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ShippingMethods—Shipments | 1:N | `Shipments.ShippingMethodId` | Restrict | 組裝電腦宅配方式固定 `RequiresPrepayment=1`、`AllowsCod=0`；一般宅配／超取方式可設 `AllowsCod=1` |

**組長定版修正｜COD 規則**：

- `AllowsCod`／`RequiresPrepayment` 只描述配送**方式**的能力上限，不是單筆訂單的最終授權。一般宅配（`Code=HomeDeliveryStandard` 等）與超商取貨方式具備 COD 基礎能力（`AllowsCod=1`），不得再固定為 `RequiresPrepayment=1`。
- 組裝電腦訂單使用的宅配方式須為獨立 Code（如 `HomeDeliveryAssembly`），固定 `AllowsCod=0`、`RequiresPrepayment=1`。
- 是否實際允許 COD 由 **Application 層**在結帳當下依三個條件重新判斷，缺一即拒絕 COD 並要求預付：
  1. 使用一般宅配或超取方式（非組裝電腦專屬方式）。
  2. 訂單最終應付金額（含運費、扣除折扣）**不超過 NT$20,000**。
  3. 訂單不含組裝電腦品項，且不含限制品（限制品判定依商品／促銷政策模組提供的旗標，非本模組欄位）。
- 本表的 `AllowsCod`／`RequiresPrepayment` 僅作為 UI 初步篩選與後台維護用途，實際結帳授權以 Application 判斷結果為準，不得只讀本表欄位放行 COD。

---

### 29. ShippingProviderProfiles

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_ShippingProviderProfiles_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `ProviderCode` | nvarchar(64) | 否 | 無 |  | `UX_ProviderProfiles_ProviderCode_Version` | 物流商代碼 |
| `Version` | int | 否 | 無 |  | 同上索引 | 版本號 |
| `Status` | varchar(16) | 否 | 無 |  |  | 同一 ProviderCode 同時最多一個 `Published` |
| `EffectiveFromUtc` | datetime2(3) | 是 | 無 |  |  | 生效起始 |
| `EffectiveToUtc` | datetime2(3) | 是 | 無 |  |  | 生效結束 |
| `ConfigurationJson` | nvarchar(4000) | 否 | 無 |  |  | 設定內容，具 `SchemaVersion` |
| `SchemaVersion` | int | 否 | 無 |  |  | Schema 版本 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ShippingProviderProfiles—PackageLimitVersions | 1:N | `PackageLimitVersions.ProviderProfileId` | Restrict | 版本期間不重疊 |
| ShippingProviderProfiles—Shipments | 1:N | `Shipments.ProviderProfileVersionId` | Restrict | 出貨快照鎖定當下版本，不受後續設定變更影響 |

---

### 30. PackageLimitVersions

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_PackageLimitVersions_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `ProviderProfileId` | bigint | 否 | 無 | FK→`ShippingProviderProfiles.Id` | `IX_PackageLimitVersions_ProviderProfileId_Version` | Restrict |
| `Version` | int | 否 | 無 |  | 同上索引 | 版本號 |
| `MaxWeightKg` | decimal(10,3) | 否 | 無 |  |  | >0 |
| `MaxLengthCm` | decimal(10,2) | 否 | 無 |  |  | >0 |
| `MaxWidthCm` | decimal(10,2) | 否 | 無 |  |  | >0 |
| `MaxHeightCm` | decimal(10,2) | 否 | 無 |  |  | >0 |
| `MaxTotalCm` | decimal(10,2) | 否 | 無 |  |  | >0 |
| `MaxDeclaredValue` | decimal(18,2) | 否 | 無 |  |  | >0 |
| `EffectiveFromUtc` | datetime2(3) | 是 | 無 |  |  | 生效起始 |
| `EffectiveToUtc` | datetime2(3) | 是 | 無 |  |  | 生效結束；期間不重疊 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ShippingProviderProfiles—PackageLimitVersions | N:1 | `PackageLimitVersions.ProviderProfileId` | Restrict | 同 Provider 版本期間不重疊 |

---

### 31. ConvenienceStores

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_ConvenienceStores_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `ProviderCode` | nvarchar(64) | 否 | 無 |  | `UX_ConvenienceStores_ProviderCode_StoreCode` | 物流商代碼 |
| `StoreCode` | nvarchar(64) | 否 | 無 |  | 同上索引 | 門市代碼 |
| `StoreName` | nvarchar(160) | 否 | 無 |  |  | 門市名稱 |
| `Address` | nvarchar(500) | 否 | 無 |  |  | 門市完整地址（顯示用，不作查詢欄位） |
| `City` | nvarchar(60) | 否 | 無 |  | `IX_ConvenienceStores_City_District_IsActive` | 縣市，匯入時正規化寫入，不由 Address 解析 |
| `District` | nvarchar(60) | 否 | 無 |  | `IX_ConvenienceStores_City_District_IsActive` | 行政區，匯入時正規化寫入，不由 Address 解析 |
| `IsDemoData` | bit | 否 | 0 |  |  | 展示／測試資料標記，正式資料匯入時為 0 |
| `IsActive` | bit | 否 | 無 |  | `IX_ConvenienceStores_City_District_IsActive` | 被引用後不可刪，以此停用 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ConvenienceStores—Shipments | 1:N | `Shipments.ConvenienceStoreId` | Restrict | 超取方式必須指定門市；宅配門市欄位須為 Null |

**必須修正**：新增 `City`／`District`／`IsDemoData` 三欄，支援後台依縣市／行政區篩選門市；`City`／`District` 由物流商資料來源或匯入時正規化寫入，查詢時**不得**即時解析 `Address` 自由文字判斷縣市及行政區。

---

### 32. Shipments

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_Shipments_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `OrderId` | bigint | 否 | 無 | FK→`Orders.Id` | `IX_Shipments_OrderId` | Restrict |
| `ShippingMethodId` | bigint | 否 | 無 | FK→`ShippingMethods.Id` | | Restrict |
| `ProviderProfileVersionId` | bigint | 否 | 無 | FK→`ShippingProviderProfiles.Id` | | Restrict；快照鎖定版本 |
| `ConvenienceStoreId` | bigint | 是 | 無 | FK→`ConvenienceStores.Id` | | 超取需 Store；宅配須 Null |
| `ShipmentNumber` | nvarchar(64) | 否 | 無 |  | `UX_Shipments_ShipmentNumber` | 唯一 |
| `Status` | varchar(24) | 否 | 無 |  |  | `Pending/Preparing/Shipped/InTransit/PickupReady/PickedUp/Delivered/DeliveryFailed/Returned`（定版，不可自訂） |
| `TrackingNumber` | nvarchar(128) | 是 | 無 |  |  | 追蹤號碼 |
| `FeeSnapshot` | decimal(18,2) | 否 | 無 |  |  | 運費快照 |
| `ShippedAtUtc` | datetime2(3) | 是 | 無 |  |  | 出貨時間 |
| `DeliveredAtUtc` | datetime2(3) | 是 | 無 |  |  | 送達時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Orders—Shipments | N:1 | `Shipments.OrderId` | Restrict | 出貨扣庫需保留庫存狀態為 `Active` |
| ShippingMethods—Shipments | N:1 | `Shipments.ShippingMethodId` | Restrict | 依方式決定是否需要 Store |
| ShippingProviderProfiles—Shipments | N:1 | `Shipments.ProviderProfileVersionId` | Restrict | 版本快照不受後續設定變更影響 |
| ConvenienceStores—Shipments | N:1（可選） | `Shipments.ConvenienceStoreId` | Restrict | 超取必填、宅配必空 |
| Shipments—ShipmentStatusHistories | 1:N | `ShipmentStatusHistories.ShipmentId` | Restrict | 狀態變化需 append-only 記錄 |

**組長定版修正｜狀態列舉**：`Status` 正式清單為 `Pending`、`Preparing`、`Shipped`、`InTransit`、`PickupReady`、`PickedUp`、`Delivered`、`DeliveryFailed`、`Returned` 九個值，不可再寫「等」或自行擴充；合法轉移完全依狀態機文件，Migration 前逐項核對 Check Constraint。

**必須修正｜批次出貨流程說明**：

- 每批次最多處理 100 筆訂單；超過須分批送出。
- 每張訂單使用**獨立** SQL Transaction 建立 `Shipment`；不得把整批包在單一交易。
- 批次執行結果逐筆回報成功／失敗與原因碼，不得只回傳整批成功或失敗。
- 建立前逐筆驗證：付款狀態允許出貨、（如有）`AssemblyJob` 已完成、對應 `InventoryReservation` 為 `Active`、物流方式與門市／地址資料完整。
- 驗證通過後於同一交易內：建立 `Shipment`、寫入首筆 `ShipmentStatusHistory`（`Pending→Preparing` 或 `→Shipped`）、建立出貨用 `InventoryMovement` 並同步扣減 `InventoryBalance`。
- 物流商回呼／同一出貨事件以 `ExternalEventId` 去重，重複事件不得重複扣庫或重複寫入歷程。
- 批次中任一筆訂單失敗（驗證不過或交易異常）**不得**回滾同批次其他已成功的訂單；失敗訂單原因碼需可回溯供人工重試。

---

### 33. ShipmentStatusHistories

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_ShipmentStatusHistories_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間（即記錄時間） |
| `ShipmentId` | bigint | 否 | 無 | FK→`Shipments.Id` | `IX_ShipmentStatusHistories_ShipmentId_OccurredAtUtc` | Restrict |
| `FromStatus` | varchar(24) | 是 | 無 |  |  | 前一狀態，初始可為 Null |
| `ToStatus` | varchar(24) | 否 | 無 |  |  | 新狀態 |
| `ExternalEventId` | nvarchar(128) | 是 | 無 |  | `UX_ShipmentStatusHistories_ExternalEventId`（Filtered） | 物流商回呼事件冪等鍵 |
| `OccurredAtUtc` | datetime2(3) | 否 | 無 |  | 同上索引 | 發生時間 |
| `ActorUserId` | nvarchar(450) | 是 | 無 |  |  | 操作者（系統事件為 Null） |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Shipments—ShipmentStatusHistories | N:1 | `ShipmentStatusHistories.ShipmentId` | Restrict | Append-only，不可 Update／Delete；`ExternalEventId` 具唯一性避免重複回呼 |

---

## M-16｜自由組裝電腦（3 張表）

**必須修正｜組裝清單跨模組銜接說明**：

```text
BuildList
→ BuildListItems（零件與數量）
→ 加入 Cart：為本次加入的所有 BuildListItem 產生同一把 AssemblyGroupKey
→ CartItems 以該 AssemblyGroupKey 共用同一群組（見 M-06 CartItems）
→ Checkout：重新查詢價格、庫存與相容性後才允許送出
→ OrderItems 保存相同 AssemblyGroupKey（haru 負責，快照當下零件與價格）
→ 每個 AssemblyGroupKey 於訂單成立後建立一筆 AssemblyJob（haru／M-08 負責）
```

- **數量倍增**：同一 `BuildList` 一次購買多台時，`CartItems` 內該 `AssemblyGroupKey` 下每個零件的 `Quantity` 需依「單台零件數量 × 購買台數」計算，而非固定為 1；`AssemblyGroupKey` 仍以「一台主機」為單位分組，購買 N 台即產生 N 組不同的 `AssemblyGroupKey`。
- **組裝費**：每台組裝完成的主機（每組 `AssemblyGroupKey`）收取 NT$300 組裝費，於訂單金額計算時按群組數累加，不是按零件數累加。
- **獨立 AssemblyJob**：每個 `AssemblyGroupKey` 於訂單成立後建立一筆獨立 `AssemblyJob`，彼此進度與狀態互不影響。
- **加入購物車前重新檢查**：從 `BuildList` 匯入 `Cart` 的當下，須重新查詢每個零件目前的價格（`Skus.ListPrice`／`SalePrices`）、可購買庫存（`InventoryBalances.AvailableQuantity`）與相容性結果（`CompatibilityCheckRuns`），不得直接沿用 `BuildList.CompatibilityStatus` 快取值；`BuildLists.LastCheckedAtUtc` 過舊時需強制重新檢查。
- **Order 建立後不回推**：訂單成立後，`OrderItems` 的快照即為交易真實內容；即使來源 `BuildList` 之後被編輯或刪除，也不得依目前 `BuildList` 內容回推或修改已成立訂單的組裝明細。

### 34. BuildLists

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_BuildLists_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `OwnerUserId` | nvarchar(450) | 否 | 無 |  | `IX_BuildLists_OwnerUserId_UpdatedAtUtc` | 會員每人最多 50 份有效清單（Application 驗證） |
| `Name` | nvarchar(160) | 否 | 無 |  |  | 清單名稱 |
| `Status` | varchar(16) | 否 | 無 |  |  | 有效／停用狀態 |
| `LastCheckedAtUtc` | datetime2(3) | 是 | 無 |  |  | 最後相容性檢查時間 |
| `CompatibilityStatus` | varchar(24) | 是 | 無 |  |  | 最後檢查結果摘要 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| BuildLists—BuildListItems | 1:N | `BuildListItems.BuildListId` | Cascade（白名單） | 會員每人最多 50 份有效清單 |
| BuildLists—BuildShareTokens | 1:N | `BuildShareTokens.BuildListId` | Restrict | Token 撤銷／到期不影響 BuildList 本身 |
| BuildLists—CompatibilityCheckRuns | 1:N | `CompatibilityCheckRuns.BuildListId` | Restrict | 檢查結果為不可變快照，可依 BuildList 重建 |

---

### 35. BuildListItems

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生 UUID v7） |  | `UX_BuildListItems_PublicId` | 對外識別碼 |
| `BuildListId` | bigint | 否 | 無 | FK→`BuildLists.Id` | `UX_BuildListItems_BuildListId_SkuId` | Cascade（白名單） |
| `SkuId` | bigint | 否 | 無 | FK→`Skus.Id` | 同上 | Restrict |
| `Quantity` | int | 否 | 無 |  |  | 1～8 |
| `SortOrder` | int | 否 | 無 |  |  | 顯示排序 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| BuildLists—BuildListItems | N:1 | `BuildListItems.BuildListId` | Cascade（白名單） | 同清單同 SKU 只有一列（以 Quantity 累計） |
| Skus—BuildListItems | N:1 | `BuildListItems.SkuId` | Restrict | 停用 SKU 不刪除既有項目 |

---

### 36. BuildShareTokens

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_BuildShareTokens_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `BuildListId` | bigint | 否 | 無 | FK→`BuildLists.Id` | | Restrict |
| `TokenHash` | binary(32) | 否 | 無 |  | `UX_BuildShareTokens_TokenHash` | Token 雜湊值，不存明文 |
| `ExpiresAtUtc` | datetime2(3) | 是 | 無 |  | Filtered `IX_BuildShareTokens_ExpiresAtUtc`（`WHERE ExpiresAtUtc IS NOT NULL`） | Null 表示預設不自動到期 |
| `RevokedAtUtc` | datetime2(3) | 是 | 無 |  |  | 撤銷時間，建立者可主動撤銷 |
| `LastAccessedAtUtc` | datetime2(3) | 是 | 無 |  |  | 最後存取時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| BuildLists—BuildShareTokens | N:1 | `BuildShareTokens.BuildListId` | Restrict | 撤銷／到期即失效；清單刪除或建立者帳號停權時連結必須一併失效 |

**組長定版修正**：分享連結預設不自動到期（`ExpiresAtUtc=NULL`）；`RevokedAtUtc` 仍保留供建立者主動撤銷；`BuildLists` 軟刪或 `OwnerUserId` 帳號停權時，所有關聯 Token 必須視為已撤銷。

---

## M-17｜零件相容性引擎與後台（3 張表）

### 37. CompatibilityRuleSettings

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_CompatibilityRuleSettings_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `RuleCode` | nvarchar(64) | 否 | 無 |  | `UX_CompatibilityRuleSettings_RuleCode_SettingCode_SettingsVersion` | 規則代碼 |
| `SettingCode` | nvarchar(64) | 否 | 無 |  | 同上索引 | 設定項代碼 |
| `DecimalValue` | decimal(18,4) | 是 | 無 |  |  | 兩值欄恰一非 Null |
| `BooleanValue` | bit | 是 | 無 |  |  | 兩值欄恰一非 Null |
| `SettingsVersion` | int | 否 | 無 |  | 同上索引 | 版本號 |
| `Reason` | nvarchar(500) | 否 | 無 |  |  | 變更理由 |
| `ChangedByAdminUserId` | nvarchar(450) | 否 | 無 |  |  | 變更人員 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| CompatibilityRuleSettings—CompatibilityCheckRuns | 1:N（邏輯） | `CompatibilityCheckRuns.SettingsVersion` | Restrict | 檢查執行需鎖定當下 `SettingsVersion`，不受後續設定變更影響 |

---

### 38. CompatibilityCheckRuns

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_CompatibilityCheckRuns_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `BuildListId` | bigint | 是 | 無 | FK→`BuildLists.Id` | `IX_CompatibilityCheckRuns_BuildListId_EvaluatedAtUtc` | 訪客暫存清單檢查時可為 Null |
| `RuleSetVersion` | int | 否 | 無 |  |  | 規則版本 |
| `SettingsVersion` | int | 否 | 無 |  |  | 設定版本 |
| `Overall` | varchar(24) | 否 | 無 |  |  | 整體結果（如 `Pass/Warning/Fail`） |
| `InputHash` | binary(32) | 否 | 無 |  |  | 輸入內容 Hash，供重跑比較 |
| `EvaluatedAtUtc` | datetime2(3) | 否 | 無 |  | `IX_CompatibilityCheckRuns_BuildListId_EvaluatedAtUtc` | 檢查時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| BuildLists—CompatibilityCheckRuns | N:1（可選） | `CompatibilityCheckRuns.BuildListId` | Restrict | 訪客暫存清單無 BuildList 時仍可執行檢查 |
| CompatibilityCheckRuns—CompatibilityCheckResults | 1:N | `CompatibilityCheckResults.CompatibilityCheckRunId` | Restrict | 結果為不可變快照，可用 `InputHash`＋版本重跑比較 |

---

### 39. CompatibilityCheckResults

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵（無獨立 PublicId） |
| `CompatibilityCheckRunId` | bigint | 否 | 無 | FK→`CompatibilityCheckRuns.Id` | `IX_CompatibilityCheckResults_RunId_Severity` | Restrict |
| `RuleCode` | nvarchar(64) | 否 | 無 |  |  | 觸發的規則代碼 |
| `Severity` | varchar(24) | 否 | 無 |  | 同上索引 | 嚴重程度（如 `Info/Warning/Error`） |
| `MessageKey` | nvarchar(160) | 否 | 無 |  |  | 訊息多語系 Key |
| `FactsJson` | nvarchar(4000) | 是 | 無 |  |  | 具 Schema Version；不得含成本／個資 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| CompatibilityCheckRuns—CompatibilityCheckResults | N:1 | `CompatibilityCheckResults.CompatibilityCheckRunId` | Restrict | 結果不可變；`FactsJson` 內容受 Schema Version 與內容限制約束 |

---

## 跨模組｜AI Query／DTO 邊界（M-18 AI 商品搜尋、M-19／相容性 AI 輔助共用）

**必須修正**：本模組（商品、SKU、規格、庫存、相容性）不因 AI 功能需求而放寬存取邊界，明確聲明如下。

- AI 模組（alex 負責）**不可**直接使用本模組任何 Repository 或直接查詢底層資料表；一律透過 Application 層提供的**受控 Query／DTO**存取。
- 受控 DTO 只包含：已發布（`Status=Published`）的商品／SKU 基本資訊、公開規格值、目前可售狀態（依 `InventoryBalances.AvailableQuantity` 衍生的布林值，不回傳確切數量）、相容性檢查結果（`CompatibilityCheckResults` 摘要）及其必要引用來源。
- 不回傳：`Skus.UnitCost`、任何內部管理備註／欄位、未發布或已停用商品、未授權（如僅限特定角色）的資料。
- 相容性核心資料缺失（如 `SkuSpecificationValues` 缺少參與判斷的硬性規格值、缺 `SpecificationSourceId`）時，DTO／Query 回傳明確的 `InsufficientData` 狀態，**不得**由 AI 端臆測或補值。
- AI 推薦、AI 客服引用商品／SKU／組裝清單時一律使用 `PublicId`，不得暴露或傳遞內部 `bigint Id`。
- 本模組不因 AI 查詢頻率調整正規化設計；AI 查詢造成的效能瓶頸依 M-05 反正規化申請流程處理，不得為 AI 另開快取表。

---

## S-02｜評價與審核（3 張表）

**必須修正**：原提案完全缺失，依組長驗收報告新增以下三表。

### 40. ProductReviews

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵 |
| `PublicId` | uniqueidentifier | 否 | 無（應用層產生） |  | `UX_ProductReviews_PublicId` | 對外識別碼 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 建立時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |
| `MemberUserId` | nvarchar(450) | 否 | 無 |  | `IX_ProductReviews_MemberUserId_CreatedAtUtc` | 訪客不可評價，必填 |
| `OrderItemId` | bigint | 否 | 無 | FK→`OrderItems.Id`（haru 負責） | `UX_ProductReviews_OrderItemId` | 同一 OrderItem 只能有一筆有效評價 |
| `ProductId` | bigint | 否 | 無 | FK→`Products.Id` | `IX_ProductReviews_ProductId_Status` | Restrict |
| `Rating` | tinyint | 否 | 無 |  |  | 1～5（Check Constraint） |
| `Title` | nvarchar(160) | 是 | 無 |  |  | 評價標題 |
| `Content` | nvarchar(2000) | 否 | 無 |  |  | 評價內容 |
| `Status` | varchar(24) | 否 | `Draft` |  | `IX_ProductReviews_ProductId_Status` | `Draft/PendingReview/Approved/Rejected/Hidden`；只有 Approved 公開 |
| `ReviewedByAdminUserId` | nvarchar(450) | 是 | 無 |  |  | 審核人員 |
| `ReviewedAtUtc` | datetime2(3) | 是 | 無 |  |  | 審核時間 |
| `RejectionReason` | nvarchar(500) | 是 | 無 |  |  | `Rejected` 時必填 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| Products—ProductReviews | 1:N | `ProductReviews.ProductId` | Restrict | 商品停用不影響既有評價顯示歷史 |
| OrderItems—ProductReviews（外部） | 1:1 | `ProductReviews.OrderItemId` | Restrict | 每個 OrderItem 最多一筆有效評價 |
| ProductReviews—ReviewImages | 1:N | `ReviewImages.ProductReviewId` | Restrict | 每筆評價最多 3 張圖片 |
| ProductReviews—ProductReviewRevisions | 1:N | `ProductReviewRevisions.ProductReviewId` | Restrict | 已公開內容修改前須先存檔快照，append-only |

**業務規則**：

- `OrderItemId` 對應訂單須屬於該 `MemberUserId` 本人，且訂單狀態已完成（依 haru 提供的 Order／OrderItem 唯讀 Query 驗證，本模組不直接讀 Orders 表）。
- 已 `Approved` 的評價被會員修改後，`Status` 必須退回 `PendingReview` 重新送審；修改前的公開內容須先寫入 `ProductReviewRevisions`（見下表），不得直接覆寫遺失。`Rejected` 可修正後重送 `PendingReview`。
- `Rating` 僅接受 1～5 整數（Check Constraint）。

---

### 41. ReviewImages

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵（無獨立 PublicId） |
| `ProductReviewId` | bigint | 否 | 無 | FK→`ProductReviews.Id` | `IX_ReviewImages_ProductReviewId` | Restrict |
| `StorageKey` | nvarchar(500) | 否 | 無 |  | `UX_ReviewImages_StorageKey` | 私有儲存定位鍵，不可由原始檔名組成 |
| `OriginalFileName` | nvarchar(255) | 否 | 無 |  |  | 僅供安全顯示 |
| `MediaType` | varchar(100) | 否 | 無 |  |  | 掃描後 MIME，僅 PNG／JPG |
| `FileSizeBytes` | bigint | 否 | 無 |  |  | 1～5 MB |
| `Sha256` | binary(32) | 否 | 無 |  |  | 原圖 Hash |
| `ScanStatus` | varchar(20) | 否 | `Pending` |  |  | `Pending/Clean/Rejected/Failed`；非 `Clean` 禁止公開 |
| `SortOrder` | int | 否 | 0 |  |  | 顯示排序 |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 上傳時間 |
| `UpdatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 修改時間 |
| `DeletedAtUtc` | datetime2(3) | 是 | 無 |  |  | 清除時間 |
| `RowVersion` | rowversion | 否 | 系統自動 |  |  | 併發權杖 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ProductReviews—ReviewImages | N:1 | `ReviewImages.ProductReviewId` | Restrict | 每筆評價最多 3 張有效圖片（Application 驗證＋Filtered 計數檢查） |

**業務規則**：每張最大 5 MB，僅 PNG／JPG；`ScanStatus≠Clean` 或所屬 `ProductReviews.Status≠Approved` 時不得對外顯示；核准前一律視為私有。

---

### 42. ProductReviewRevisions

**欄位定義**

| 欄位 | SQL Server 型別 | Null | 預設值 | PK／FK | Unique／Index | 限制與說明 |
|---|---|:---:|---|---|---|---|
| `Id` | bigint | 否 | identity(1,1) | PK | 叢集 PK | 內部主鍵（無獨立 PublicId） |
| `ProductReviewId` | bigint | 否 | 無 | FK→`ProductReviews.Id` | `IX_ProductReviewRevisions_ProductReviewId_SupersededAtUtc` | Restrict |
| `Rating` | tinyint | 否 | 無 |  |  | 該版本公開時的評分快照 |
| `Title` | nvarchar(160) | 是 | 無 |  |  | 該版本公開時的標題快照 |
| `Content` | nvarchar(2000) | 否 | 無 |  |  | 該版本公開時的內容快照 |
| `PublishedAtUtc` | datetime2(3) | 否 | 無 |  |  | 此版本原本公開的時間 |
| `SupersededAtUtc` | datetime2(3) | 否 | 無 |  | `IX_ProductReviewRevisions_ProductReviewId_SupersededAtUtc` | 被下一版取代（含被下架）的時間 |
| `SupersededReason` | varchar(24) | 否 | 無 |  |  | `MemberEdited/AdminHidden/AdminRejectedAfterPublish` |
| `CreatedAtUtc` | datetime2(3) | 否 | 無 |  |  | 快照寫入時間 |

**關聯與完整性**

| 關聯 | 基數 | FK 位置 | Delete Behavior | 必須維持的不變量 |
|---|---|---|---|---|
| ProductReviews—ProductReviewRevisions | 1:N | `ProductReviewRevisions.ProductReviewId` | Restrict | Append-only，不可 Update／Delete；已公開版本被覆寫前必須先產生一筆快照 |

**版本歷程保存方式**：已公開評價修改後，舊版不得直接被覆寫遺失。流程為：會員／管理員觸發變更（編輯、下架、審核後駁回）前，先將 `ProductReviews` 目前公開內容（`Rating`／`Title`／`Content`／原發布時間）寫入一筆 `ProductReviewRevisions`（Append-only），再更新 `ProductReviews` 本身為新內容並視情況將 `Status` 退回 `PendingReview` 或 `Hidden`。前台僅顯示 `ProductReviews` 目前 `Approved` 內容；`ProductReviewRevisions` 只供後台稽核與爭議查證使用，不對外公開。

---

## INT-04｜售後與報表核心 E2E（0 張表）

> 無獨立資料表。負責跨模組驗證與測試協調（售後 `ReturnItems`／`Refunds` 與本模組庫存、出貨、商品資料的一致性核對），不因此取得 `Returns`、`Refunds` 或 `Invoice` 資料表的直接寫入權，也不在本模組新增報表彙總表。

---

## 修正紀錄

依「terry｜資料表設計缺失報告與修正建議」（2026-08-15 組長定版）逐項修正：

- [x] 第五節 4 項已定版指示：`BuildShareTokens.ExpiresAtUtc` 改 Null、COD 規則改為 Application 層依金額／組裝電腦／限制品判斷、`Skus` 移除 `SalePrice`、Import Schema 欄位改名與長度修正。
- [x] 補上 `ProductReviews`、`ReviewImages`、`ProductReviewRevisions`（S-02，第 40～42 張表）。
- [x] `ImportRows` 索引改為實際 Unique Constraint，5,000 列上限修正為三資料集合計。
- [x] 補上 Import Staging 完整流程說明（Preview／Confirm／回滾／清理／兩種 Use Case 分離）。
- [x] 補上 M-05 具體候選索引清單與量測原則。
- [x] 補上 Inventory 建立保留／釋放保留／出貨扣庫存三流程逐步驟說明，含 RowVersion 條件更新規則。
- [x] 補上組裝清單→購物車→訂單→AssemblyJob 跨模組銜接說明。
- [x] 補上 AI Query／DTO 邊界聲明。
- [x] `ConvenienceStores` 補 `City`／`District`／`IsDemoData` 及查詢索引。
- [x] 補上批次出貨流程說明（M-11／Shipments）。
- [x] `Shipments.Status` 改為正式九值列舉，不再待確認。
- [x] 修正 `BuildListItems.PublicId` 的誤植索引與說明：正式名稱為 `UX_BuildListItems_PublicId`，用途為對外識別碼，不再錯用 `UX_ReviewImages_PublicId`／附件描述。

## M-15｜營運報表唯讀契約

- 正式 Endpoint：`GET /api/v1/admin/reports/{reportKey}` 與 `/export`；只接受 `sales-overview/product-abc/period-comparison/inventory-turnover/gross-margin/product-associations/forecast-anomalies`。
- Request／Response 固定使用 `ReportQuery`、`ReportResultDto`、`ReportRowDto`；明細列採 Cursor。不得建立第二套 Route、DTO 或報表專用可寫真實來源。
- 角色只使用正式 `MarketingAnalyst`、`FinanceManager`、`CustomerServiceSupervisor`、`SuperAdmin` 及其 Policy；不得建立 `ReportViewer`／`OperationsManager`。
- 庫存周轉率＝期間銷貨成本 ÷ 平均庫存成本；平均庫存成本＝（期初 OnHand 成本＋期末 OnHand 成本）÷2。
- 商品關聯至少 5 筆共同完成訂單，且 `Support ≥ 1%`、`Confidence ≥ 20%`、`Lift > 1` 才顯示。
- 預測以最近 30 天簡單線性迴歸預測未來 7 天；有效資料少於 14 天不輸出預測；異常門檻固定 `|z-score| > 2`。
- 跨模組資料只能經各 Owner 提供的去識別化 Application Query／DTO 取得，不得直接使用其他模組 Repository／DbContext。

## 尚待實作驗證（不是產品決策）

- 匯入原子交易、Order-only Reservation／Movement 併發、相容性快照重建與評價 Revision 流程，需由整合測試驗證。
- COD 最終授權必須由 Application Use Case 同時檢查付款方式、配送方式、NT$20,000 門檻、組裝電腦及任一 SKU `RequiresPrepayment`；不得只依 `ShippingMethods` 欄位放行。
- 本文件完成只關閉 DES-18 的「Schema 文件」缺口；建立 Migration 前仍須完成 Entity、Configuration、跨模組 FK、交易／冪等測試清單及獨立 Migration Review。
