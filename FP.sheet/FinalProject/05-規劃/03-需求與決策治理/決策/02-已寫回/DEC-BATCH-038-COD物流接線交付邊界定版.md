---
batch_id: DEC-BATCH-038
status: applied
decision_date: 2026-08-31
decision_ids:
  - DEC-P347
---

# DEC-BATCH-038｜COD 物流接線交付邊界定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P347 | 採用最低重複成本方案：目前不新增平行的管理端履約狀態 Endpoint，也不接管物流模組範圍。模擬付款完成端點一律拒絕 COD；本次交付可供物流 writer 使用的 `CashOnDeliveryCompletionService` 與 Delivered／PickedUp 規則測試。真正的 COD 付款投影、通知、稽核及發票 Outbox，必須由未來正式物流狀態命令在進入 `Delivered`／`PickedUp` 的同一交易邊界內接線，完成前不得把 COD 垂直流程標為完成。 |

## Lowest-Cost Analysis

1. 只在文件說明 COD 不可提前付款：沒有可重用規則，未來物流實作容易再次分歧，未採用。
2. 沿用現有付款與物流模型，先交付內部付款決策服務及測試，待正式物流命令接線：不新增公開 API、不重複 Terry 的物流範圍，且可鎖定收款時點，採用。
3. 現在新增管理端 Delivered／PickedUp Endpoint 並接管物流狀態寫入：會形成新的公開合約並與既有物流分工重疊，建置、整合與回復成本較高，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 顧客、訂單／物流管理員、alex、物流模組開發者 |
| 現況風險 | `dev` 目前沒有 Delivered／PickedUp 的正式寫入命令；若為了 COD 另開平行 API，可能產生兩套履約真實來源 |
| 預期可量測結果 | Demo 完成端點無法替 COD 提前入帳；Delivered／PickedUp 規則有自動測試；正式物流接線前 COD 垂直切片維持未完成 |
| 建置／持續成本 | 重用既有 Domain／Application 狀態與錯誤碼；不新增套件、Schema、外部服務或公開 Endpoint |
| 風險成本 | 若未來物流 writer 漏接服務，COD 送達後仍不會自動收款與開票，因此必須保留明確追蹤 Gate |
| 信心 | 高；目前程式與物流 PR 範圍已核對，接線依賴清楚 |
| 成功指標 | 非 Delivered／PickedUp 永不收款；重播同一履約事件不重複付款、通知、稽核或開票；Provider-backed 與 E2E 通過 |
| 停止／回復條件 | 出現 COD 提前入帳、平行履約 API、重複付款或重複發票時停止合併；Demo 入口可由既有設定關閉 |

## 影響文件與追蹤

- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/01-時程與進度/M功能實作矩陣]]
- [[05-規劃/01-時程與進度/核心交易整合協調]]
- [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-BATCH-037-S02例外授權與模擬付款完成邊界定版]]

