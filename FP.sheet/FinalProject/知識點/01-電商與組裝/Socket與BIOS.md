---
type: knowledge
title: CPU Socket 與 BIOS 相容性
aliases:
  - Socket 與 BIOS
  - CPU 腳位與 BIOS
tags:
  - 知識點
  - 組裝
  - 相容性
  - CPU
  - 主機板
  - BIOS
created_at: 2026-08-09
related:
  - "[[02-領域需求/02-商品庫存與組裝/商品、組裝與相容性]]"
  - "[[05-規劃/01-時程與進度/未完成項目追蹤表]]"
---

# CPU Socket 與 BIOS 相容性

## Socket 是什麼

Socket 是 CPU 與主機板之間的實體及電氣介面，例如 AMD AM4、AM5 或 Intel LGA1700。CPU 和主機板的 Socket 不同時，一定不能安裝。

```text
CPU Socket != 主機板 Socket
→ 硬性不相容
```

但 Socket 相同只是必要條件，不代表一定能開機。

## 為什麼同 Socket 仍可能不相容

還需要同時考慮：

- 主機板晶片組是否支援該 CPU 家族或世代。
- 主機板目前 BIOS 是否包含該 CPU 的微碼與初始化支援。
- 主機板廠商是否在該型號的 CPU Support List 中正式列出該處理器。
- 所需 BIOS 版本能否在尚未安裝新 CPU 的情況下完成更新。
- 主機板供電、功耗或其他廠商限制。

AMD 官方的 AM5 資料即說明，部分 600 系列主機板搭配較新的 Ryzen 處理器可能需要更新 BIOS。這正是「Socket 相同但仍需 BIOS」的典型案例。

## BIOS 版本相容性

完整資料模型可以記錄：

```text
MotherboardCpuSupport
├─ MotherboardModelId
├─ CpuModelId
├─ MinimumBiosVersion
├─ SupportStatus
├─ SourceUrl
└─ VerifiedAt
```

判斷流程：

```text
Socket 不同
→ Error：不可組裝

Socket 相同但晶片組／世代不支援
→ Error：不可組裝

已知最低 BIOS，主機板版本過舊
→ Warning 或 Error，依是否提供代更新服務決定

只知道「可能需更新 BIOS」
→ Warning：購買前確認或更新 BIOS
```

版本字串不一定能直接用一般字典順序比較，例如 `F9`、`F10`、`1.2.0.A` 可能採不同廠商規則。若要精確比較，應保存廠商、主機板型號及正規化排序值，不能假設所有 BIOS 共用同一格式。

## 第一版可採的簡化層級

| 層級 | 資料量 | 能回答的問題 |
|---|---:|---|
| Socket 比對 | 低 | 能否物理安裝 |
| Socket＋晶片組／CPU 世代 | 中 | 平台是否原則支援 |
| 型號＋CPU＋最低 BIOS | 高 | 特定主機板版本能否直接啟動 |

本專案第一版已確認採「Socket 與世代映射做阻擋，可能需要更新 BIOS 時顯示警告」，不維護所有主機板的最低版本。這能降低資料維護成本，但畫面必須誠實呈現不確定性。

## 商品資料要求

為了讓程式穩定判斷，至少需要結構化欄位：

- CPU：`SocketKey`、`CpuFamilyKey`、`CpuGenerationKey`。
- 主機板：`SocketKey`、`ChipsetKey`、支援的 CPU 家族／世代。
- 可選：`CurrentBiosVersion`、最低 BIOS 對照資料、官方來源連結與查核日期。

`AM5` 等語意鍵應使用受控值，顯示名稱可以翻譯，但不能靠自由文字模糊比對。

> [!warning] 資料時效
> CPU 支援清單和 BIOS 版本會更新。若系統顯示型號級結論，必須保存來源與查核日期，並以主機板廠商的 CPU Support List 為最終依據。

## 參考資料

- [AMD Socket AM5 Chipsets](https://www.amd.com/en/products/processors/chipsets/am5.html)
- [AMD Socket AM4 Chipsets](https://www.amd.com/en/products/processors/chipsets/am4.html)
- [[02-領域需求/02-商品庫存與組裝/商品、組裝與相容性]]
- [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-BATCH-002-第二批核心決策]]
