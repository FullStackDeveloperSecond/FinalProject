---
type: knowledge
title: Z-score（標準分數）
aliases:
  - Z-Score
  - Z Score
  - Z-Source
  - 標準分數
tags:
  - 知識點
  - 報表
  - 統計
  - 異常偵測
  - Z-score
created_at: 2026-08-09
related:
  - "[[02-領域需求/報表與展示資料]]"
  - "[[知識點/30天線性迴歸]]"
---

# Z-score（標準分數）

> [!note] 名稱
> 正確統計名稱是 **Z-score**，不是 Z-Source。本頁保留 `Z-Source` 作為搜尋別名，避免原始筆記找不到。

## 什麼是 Z-score

Z-score 表示某個觀察值距離平均數多少個標準差：

```text
zi = (yi - ȳ) / s
```

- `yi`：第 i 天的觀察值。
- `ȳ`：同一基準窗口的平均值。
- `s`：同一基準窗口的標準差。

`z = 2.4` 代表該值高於平均約 2.4 個標準差；`z = -2.4` 代表低於平均約 2.4 個標準差。

## 本專案已確認規則

第二批決策已確認：

```text
最近 30 天資料
→ 以標準差計算 Z-score
→ |z| > 2 標記異常
```

計算時必須明確選擇母體標準差或樣本標準差並固定實作；測試也要使用同一公式。原附件曾以變異數直接作除數，會造成單位不一致；Z-score 的分母應是標準差。

## 原始值或迴歸殘差

有兩種常見用法，意義不同：

### 每日銷量 Z-score

```text
z = (當日銷量 - 30 日平均銷量) / 30 日標準差
```

回答「今天相對近期一般水準是否異常」，但上升或下降趨勢本身可能被誤判。

### 迴歸殘差 Z-score

```text
residual = 實際銷量 - 線性模型預測銷量
z = (residual - 殘差平均) / 殘差標準差
```

回答「今天相對既有趨勢是否異常」，較能排除趨勢影響，但模型與解釋更複雜。

目前正式規則已確認使用 Z-score 與 `|z| > 2` 門檻，但尚未指定對原始銷量或迴歸殘差計算。實作前仍須補足此計算口徑，不能讓前端與後端各自解讀。

## 邊界條件

- 有效資料不足時回傳 `InsufficientData`，不要硬算。
- 標準差為 `0` 時 Z-score 無法相除；若新值仍等於平均，可視為非異常，否則應以明確替代規則處理。
- 缺貨日、停賣日、資料遺失與真正零銷量不可混為一談。
- `|z| > 2` 是警示門檻，不代表已證明有錯誤、詐欺或商業事件。
- 多個異常值可能拉動平均與標準差，讓其他異常被遮蔽。
- 小樣本下以 Z-score 判斷離群值可能誤導；本系統至少搭配資料量門檻與人工解讀。

## 回傳與呈現

```text
AnomalyPoint
├─ Date
├─ ActualValue
├─ BaselineMean
├─ StandardDeviation
├─ ZScore
├─ Direction: High | Low
├─ IsAnomaly
└─ ModelVersion
```

圖表應同時顯示實際值、基準線與門檻，並讓使用者看到「為何被標記」，不能只顯示紅點。

## 測試案例

- 平均數及標準差已知的固定資料。
- 恰好 `z = 2` 與略大於 `2`，確認 `>` 或 `>=` 邊界。
- 正向與負向異常。
- 標準差為零。
- 少於最低資料天數。
- 缺日期、退款回補及跨時區日界線。

## 參考資料

- [NIST：Detection of Outliers](https://itl.nist.gov/div898/handbook/eda/section3/eda35h.htm)
- [NIST：ISO 13528 ZSCORE](https://www.itl.nist.gov/div898/software/dataplot/refman2/auxillar/zscore.htm)
- [[05-規劃/決策/02-已寫回/DEC-BATCH-002-第二批核心決策]]
