---
title: CPU 與晶片組相容性官方證據
status: 已採用
updated: 2026-08-27
scope: DoSelect 第一版確定性相容性引擎
---

# CPU 與晶片組相容性官方證據

## 研究問題

DoSelect 第一版應將哪些桌上型 CPU 世代與主機板晶片組組合寫入固定程式映射，哪些組合需要顯示 BIOS 警告？本研究只處理平台層級的 Demo 判斷；最終仍須提醒使用者依主機板廠商的 CPU Support List 確認特定型號與 BIOS 版本。

## 執行摘要

- AMD 官方提供 AM4 晶片組與 Ryzen 世代對照表，可直接建立平台層級的允許／阻擋規則。
- AMD 官方表示 AM5 的 600、800 系列主機板支援 Ryzen 7000、8000、9000；600 系列搭配 Ryzen 8000／9000 可能需要 BIOS 更新。
- Intel 官方表示第 12、13、14 代 Core Desktop 使用 LGA1700 並支援 600／700 系列；升級到第 13／14 代可能需要 BIOS 更新。
- Intel Core Ultra Desktop Series 2 使用 LGA1851 與 800 系列，不能搭配 600／700 系列。
- 平台層級映射不能取代單張主機板的官方 CPU Support List，因此 BIOS 結果只能是警告，不能宣稱某個最低 BIOS 版本。

## 官方證據

| 來源 | 日期／版本 | 可支持的結論 | 專案適用性 | 限制 |
|---|---|---|---|---|
| [AMD Socket AM4 Chipsets](https://www.amd.com/en/products/processors/chipsets/am4.html) | 2026-08-27 查閱 | 官方表格列出 X570、B550、A520、B450 等晶片組與 Ryzen 2000～5000 世代相容性，並註明部分組合可能需 BIOS 更新 | 可建立 AM4 世代允許清單 | 特定主機板仍須查廠商支援頁 |
| [AMD Socket AM5 Chipsets](https://www.amd.com/en/products/processors/chipsets/am5.html) | 2026-08-27 查閱 | 600／800 系列 AM5 主機板支援 Ryzen 7000、8000、9000；600 系列搭配 8000／9000 可能需 BIOS 更新 | 可建立 AM5 允許與 BIOS 警告規則 | 未提供每張主機板最低 BIOS 版本 |
| [Intel 12th／13th／14th Gen Desktop Compatibility](https://www.intel.com/content/www/us/en/support/articles/000092149/processors.html) | Last reviewed 2025-02-06 | 第 12、13、14 代桌上型 Core 使用 LGA1700 與 600／700 系列；平台升級可能需要 BIOS／FW／CSME 更新 | 可建立 Intel 600／700 系列允許與 BIOS 警告規則 | 系列層級，不是單板型號清單 |
| [Intel Required BIOS Updates](https://www.intel.com/content/www/us/en/support/articles/000092294/processors.html) | Last reviewed 2026-02-05 | Intel 建議 600／700 系列搭配第 13、14 代時更新 BIOS | 支持保守地顯示 BIOS 警告 | 不代表每張新出廠主機板都一定需要更新 |
| [Intel Core Ultra Desktop Series 2 Compatibility](https://www.intel.com/content/www/us/en/support/articles/000099798/processors.html) | Last reviewed 2024-11-22 | Core Ultra Desktop Series 2 使用 LGA1851 與 800 系列 | 可阻擋其與 LGA1700／600／700 系列混搭 | 後續世代需另行更新規則 |
| [Intel 600 Series PCH SKUs](https://edc.intel.com/content/www/us/en/design/ipla/software-development-platforms/client/platforms/alder-lake-desktop/intel-600-series-chipset-family-platform-controller-hub-pch-datasheet-volume/004/pch-skus/) | Intel 官方資料表 | 消費／桌上型代碼包含 H610、B660、H670、Z690、Q670、W680 | 支持精確的晶片組 Code | DoSelect 第一版可只收錄一般消費 H／B／Z 型號 |
| [Intel 700 Series PCH SKUs](https://edc.intel.com/content/www/us/en/design/products-and-solutions/processors-and-chipsets/700-series-chipset-family-platform-controller-hub-datasheet-volume-1-of/002/pch-skus/) | Intel 官方資料表 | 一般桌上型代碼包含 B760、H770、Z790 | 支持精確的晶片組 Code | 不涵蓋所有商用／工作站變體 |
| [Intel 800 Series Desktop Chipsets](https://www.intel.com/content/www/us/en/ark/products/series/237776/intel-800-series-desktop-chipsets.html) | 2026-08-27 查閱 | 官方列出 H810、B860、Z890 等 800 系列晶片組 | 可建立 Core Ultra 200 的精確代碼清單 | DoSelect 第一版可排除 Q／W 商用與工作站型號 |

## 建議的第一版映射

以下是根據官方系列資料做出的專案範圍建議；「收錄哪些型號」屬產品決策，不是官方要求。

### AMD AM4

| 晶片組 Code | 允許的 CPU 世代 Code | BIOS 警告 |
|---|---|---|
| `X570` | `RYZEN_2000`、`RYZEN_3000_G`、`RYZEN_3000`、`RYZEN_4000`、`RYZEN_5000` | 無固定警告；仍顯示查閱單板清單提示 |
| `B550`、`A520` | `RYZEN_3000`、`RYZEN_4000`、`RYZEN_5000` | 無固定警告；仍顯示查閱單板清單提示 |
| `B450` | `RYZEN_2000`、`RYZEN_3000_G`、`RYZEN_3000`、`RYZEN_4000`、`RYZEN_5000` | `RYZEN_4000`、`RYZEN_5000` 顯示保守 BIOS 警告（專案推論） |

### AMD AM5

| 晶片組 Code | 允許的 CPU 世代 Code | BIOS 警告 |
|---|---|---|
| `A620`、`B650`、`B650E`、`X670`、`X670E` | `RYZEN_7000`、`RYZEN_8000`、`RYZEN_9000` | `RYZEN_8000`、`RYZEN_9000` |
| `B840`、`B850`、`X870`、`X870E` | `RYZEN_7000`、`RYZEN_8000`、`RYZEN_9000` | 無固定警告 |

### Intel LGA1700

| 晶片組 Code | 允許的 CPU 世代 Code | BIOS 警告 |
|---|---|---|
| `H610`、`B660`、`H670`、`Z690` | `INTEL_CORE_12`、`INTEL_CORE_13`、`INTEL_CORE_14` | `INTEL_CORE_13`、`INTEL_CORE_14` |
| `B760`、`H770`、`Z790` | `INTEL_CORE_12`、`INTEL_CORE_13`、`INTEL_CORE_14` | `INTEL_CORE_14`（保守專案規則） |

### Intel LGA1851

| 晶片組 Code | 允許的 CPU 世代 Code | BIOS 警告 |
|---|---|---|
| `H810`、`B860`、`Z890` | `INTEL_CORE_ULTRA_200` | 無固定警告 |

## 不採用的替代方案

- 不建立映射：無法完成已列為 M 的晶片組／CPU 世代檢查。
- 只比對 Socket：同 Socket 不代表晶片組必然支援該世代，會產生錯誤相容結果。
- 維護每張主機板最低 BIOS：準確度較高，但 40 天專題範圍與資料維護成本不合理；第一版應保留「可能需更新 BIOS」警告並連回廠商確認。

## 尚待裁定

已於 2026-08-27 採用上方「建議的第一版映射」。程式中的 `CompatibilityRuleCatalog` 依此固定，未知晶片組回 `InsufficientData`，已知晶片組但不在允許清單的 CPU 世代回 `Blocked`。
