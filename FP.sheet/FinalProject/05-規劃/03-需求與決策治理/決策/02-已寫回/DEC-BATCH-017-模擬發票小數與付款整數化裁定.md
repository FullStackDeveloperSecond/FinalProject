---
type: decision-record
batch_id: DEC-BATCH-017
title: 模擬發票小數與付款整數化裁定
status: applied
created_at: 2026-08-21
submitted_at: 2026-08-21
applied_at: 2026-08-21
decision_count: 1
decision_range: DEC-P285
source: alex 依台灣現行電子發票規格與商家介接實務裁定
supersedes: DEC-P280 的發票明細整數元與明細表頭精確相等部分
---

# DEC-BATCH-017｜模擬發票小數與付款整數化裁定

## 背景

訂單商品、優惠券分攤、運費與組裝費均使用 `decimal(18,2)`，可合法產生含角分的交易明細。若發票每筆明細都強制為整數元，會使「發票等於實付」與「明細保留交易快照」無法同時成立；若在發票端才改寫金額，又會使訂單、金流與發票不一致。

本決策保留 DEC-P280 的 5% 稅率、`AwayFromZero` 與 1,000 → 952＋48 驗收案例，但覆寫其「發票每筆明細皆為整數元、明細三種金額精確等於表頭」部分。折讓金額規則不在本次覆寫範圍，仍依 DEC-P280，後續若需支援小數發票明細的部分折讓，必須另案裁定。

本專案第一版只實作清楚標示為 `DEMO` 的模擬發票；本決策定義的是 DoSelect 內部交易、模擬發票與測試不變式，不宣稱可直接作為財政部或任一加值中心的正式上傳 Payload。未來若改為真實串接，Provider Adapter 必須依當時有效的 MIG 與服務商欄位規格另行轉換及驗證，不得直接外送內部 DTO。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P285 | 訂單商品、優惠券分攤、運費、組裝費與發票明細金額維持 `decimal(18,2)`；最終應付金額在建立付款嘗試前以 `MidpointRounding.AwayFromZero` 四捨五入至整數新臺幣。`Order.GrandTotal`、`PaymentAttempt.Amount`、成功後的 `Order.PaidAmount` 與發票表頭 `IssuedAmount` 必須一致。發票表頭 Gross／Net／Tax 為整數元，發票明細 Gross／Net／Tax 可保留兩位小數；不得因單一明細含小數拒絕開立。明細加總四捨五入後若不等於既有訂單實付金額，視為交易快照不一致並拒絕，不得在發票端自行改寫任一金額。 |

## 計算與核對契約

```text
RawGrossAmount     = Sum(Line.GrossAmount)
IssuedAmount       = Round(RawGrossAmount, 0, AwayFromZero)
NetAmount          = Round(IssuedAmount / 1.05, 0, AwayFromZero)
TaxAmount          = IssuedAmount - NetAmount
RoundingAdjustment = IssuedAmount - RawGrossAmount
```

- `RoundingAdjustment` 由既有訂單總額與交易明細加總推導，不新增 Schema 欄位。
- 每筆發票明細必須滿足 `Gross = Net + Tax`，且 Gross／Net／Tax 均不得為負。
- 表頭與明細使用以下核對口徑：

```text
Round(Sum(Line.GrossAmount), 0, AwayFromZero) = Header.IssuedAmount
Round(Sum(Line.NetAmount),   0, AwayFromZero) = Header.NetAmount
Sum(Line.TaxAmount)                             = Header.TaxAmount
```

- 稅額依含稅金額比例分攤，最後一筆仍有合法調整空間的明細吸收稅額尾差。
- 含稅 1,000 的固定驗收案例仍為未稅 952、稅額 48、含稅 1,000。

## 未採方案

- 發票邊界拒絕所有小數明細：會拒絕目前可合法產生的優惠券與訂單金額，無法滿足交易快照與實付一致。
- 發票端自行把實付金額四捨五入：發票會與既有訂單或金流不一致，且掩蓋上游錯誤。
- 新增尾差欄位：本次尾差可由訂單整數總額減去明細加總推導，沒有新增 Schema 的必要。

## 影響與實作 Gate

- Checkout 必須在建立 `Order` 與 `PaymentAttempt` 前完成最終應付整數化；不能只在發票計算器中假設 `OrderPaidAmount` 已是整數。
- DES-22 完成前必須有跨層測試證明 `Order.GrandTotal = PaymentAttempt.Amount = Order.PaidAmount = Invoice.IssuedAmount`，並涵蓋含小數明細與不一致快照拒絕案例。
- PR #5 的發票計算可作為下游契約，但在上游訂單／付款整數化完成前，不得宣稱小數訂單已可端到端開票。
- 不修改既有 `decimal(18,2)` 欄位，也不新增 Migration。

## 參考依據

- [財政部電子發票 MIG 4.1](https://www.einvoice.nat.gov.tw/static/ptl/ein_upload/download/5380.pdf)
- [綠界電子發票介接技術文件](https://developers.ecpay.com.tw/53662/)

## 寫回範圍

- [[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]
- [[02-領域需求/04-客服與售後/評價收藏檢舉與模擬發票規格]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/03-資料與一致性/資料字典-購物交易與售後]]
- [[03-架構/08-測試與驗收/M功能測試案例目錄]]
- [[03-架構/09-資料表實作交付/Haru-會員登入訂單與訪客存取最終Schema]]
- [[03-架構/09-資料表實作交付/Yinyin-優惠券付款退款與發票最終Schema]]
- [[05-規劃/02-分工與交接/工程包/Yinyin-優惠付款退款與發票工程包]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]
