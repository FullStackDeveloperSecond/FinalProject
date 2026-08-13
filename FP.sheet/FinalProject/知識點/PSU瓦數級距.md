---
type: knowledge
title: PSU 瓦數級距與選擇規則
aliases:
  - 電源供應器瓦數
  - PSU Wattage Tier
tags:
  - 知識點
  - 組裝
  - 相容性
  - PSU
  - 電源供應器
created_at: 2026-08-09
related:
  - "[[02-領域需求/商品、組裝與相容性]]"
  - "[[05-規劃/未完成項目追蹤表]]"
---

# PSU 瓦數級距與選擇規則

## 額定瓦數不是實際耗電

PSU 的 650 W、750 W 是可提供的額定輸出能力，不代表電腦會一直消耗該瓦數。實際耗電由 CPU、GPU、儲存裝置、風扇、主機板及負載狀況決定。

瓦數足夠也不等於完整相容，還必須檢查：

- GPU 與 CPU 所需供電接頭及數量。
- PSU 尺寸能否裝入機殼。
- 12 V 輸出能力與瞬間峰值。
- 線材規格；模組化 PSU 不可混用其他型號線材。
- 品質、保護機制與效率認證。

## 估算方式

本專案已確認：結構化加總各零件估計負載，保留 30% 安全餘裕，且不得低於 GPU 廠商建議瓦數。

```text
EstimatedLoad
= CpuPower
 + GpuPower
 + MotherboardAllowance
 + MemoryAllowance
 + StorageAllowance
 + CoolingAllowance
 + OtherAllowance

RawRequiredWattage
= max(EstimatedLoad × 1.30, GpuVendorRecommendedPsu)

RecommendedPsuWattage
= 向上取可供應的 PSU 瓦數級距(RawRequiredWattage)
```

30% 是專案候選規則，不是所有電腦都適用的工業標準。若廠商已將整機餘裕納入建議值，仍取兩者較高者。

## 瓦數級距

市售產品沒有唯一強制級距。系統可先使用下列展示用級距，再依實際商品目錄調整：

```text
450, 550, 650, 750, 850, 1000, 1200, 1500 W
```

例如：

```text
EstimatedLoad = 510 W
510 × 1.30 = 663 W
GPU 廠商建議 = 650 W
RawRequiredWattage = 663 W
向上取級距 → 750 W
```

若目錄實際銷售 700 W 或 800 W，應以「目前可售且符合其他條件的最小額定瓦數」推薦 SKU，而不是為了固定級距排除有效商品。

## 相容性結果分級

| 條件 | 建議結果 |
|---|---|
| PSU 額定瓦數低於最低需求 | `Error`，不可組裝 |
| 瓦數足夠但缺少必要接頭 | `Error`，不可組裝 |
| PSU 尺寸超過機殼限制 | `Error`，不可組裝 |
| 瓦數剛好達標、升級空間有限 | 可選 `Warning` |
| 功耗資料缺漏 | `Unknown`，不可假裝相容 |

## 資料欄位

零件至少應提供：

- CPU/GPU 的結構化估計功耗。
- GPU 廠商建議 PSU 瓦數。
- PSU 額定瓦數、尺寸與接頭清單。
- 機殼支援的 PSU 尺寸。
- 資料來源、查核日期及估算規則版本。

保存 `RuleVersion` 能讓日後調整 30% 餘裕或級距時，仍可解釋舊組裝清單當時的建議結果。

> [!note] 專案決策邊界
> 30% 公式與 450～1500 W 八級距已正式確認。功耗值採原廠公開資料優先、有來源且經人工覆核的維護值備援；核心資料缺失時回 `InsufficientData`、不宣稱相容並阻擋整套一鍵加車，但不禁止單獨購買 SKU。

## 參考資料

- [Intel：How to Choose a Power Supply for PC](https://www.intel.com/content/www/us/en/gaming/resources/power-supply.html)
- [[02-領域需求/商品、組裝與相容性]]
- [[05-規劃/決策/00-互動中/DEC-BATCH-002-第二批核心決策]]
