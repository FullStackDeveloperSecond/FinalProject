---
dataset: zh-TW-v1.0.2-draft
fixture_version: v1.0.2
primary_annotator: kafen
reviewer: alex
status: approved
---

# SUPPORT-POLICY v1.0.2 覆核表

本表只覆核因政策 Fixture 擴充而受影響的 15 筆案例。Kafen 完成主標、Alex 完成第二審以前，不得把 `SUPPORT-POLICY` 的 `annotation.status` 改為 `approved`，也不得執行第二次 Live 煙霧測試。

## 核准來源

- `FP.sheet/FinalProject/02-領域需求/04-客服與售後/退貨與退款政策.md`
- `FP.sheet/FinalProject/02-領域需求/03-交易與履約/購物車、訂單、付款與物流.md`
- Fixture：`policy.returns.v1`、`policy.payment-shipping.v1`

## 逐案對照

| Case | Split | 問題 | 必須由核准來源支持的重點 |
|---|---|---|---|
| `SUPPORT-POLICY-001` | development | 一般商品到貨後幾天內可以申請無理由退貨？ | 到貨翌日起 7 日內；個案依訂單成立時保存的政策版本快照 |
| `SUPPORT-POLICY-002` | development | CPU 包裝拆開檢查過就一定不能退嗎？ | 不採一經拆封全部拒退；必要檢查且商品完整可退 |
| `SUPPORT-POLICY-003` | development | 客製組裝電腦已開始組裝，還能無理由取消嗎？ | `AssemblyStarted` 後轉人工審核；瑕疵、規格錯誤或組裝錯誤仍可處理 |
| `SUPPORT-POLICY-004` | development | 只退組裝電腦裡的一個正常零件，300 元組裝費會退嗎？ | 正常完成後單退正常零件不退 NT$300 組裝費 |
| `SUPPORT-POLICY-005` | development | 整台因商家組裝錯誤退回，組裝費怎麼處理？ | 商家責任整台退回時組裝費一併退還 |
| `SUPPORT-POLICY-006` | development | 部分退貨後不滿免運門檻，退款會怎麼算？ | 重新收取原配送方式運費；退款明細列出各項分攤與最終退款 |
| `SUPPORT-POLICY-007` | development | 退貨商品有綁定贈品，但贈品不見了怎麼辦？ | 綁定贈品原則上須退回；缺少時轉人工審核且不得靜默扣款 |
| `SUPPORT-POLICY-008` | development | 信用卡付款失敗一次，訂單會立刻被取消嗎？ | 原付款期限內可建立新付款嘗試；原期限到期才取消 |
| `SUPPORT-POLICY-009` | development | 組裝電腦可以貨到付款嗎？ | 含組裝電腦必須先付款；不可使用 COD |
| `SUPPORT-POLICY-010` | release | 一般宅配的運費與免運門檻是多少？ | NT$150；折扣後符合資格商品小計滿 NT$5,000 免運 |
| `SUPPORT-POLICY-011` | release | 組裝電腦宅配的運費與免運門檻是多少？ | NT$300；折扣後符合資格商品小計滿 NT$30,000 免運；必須先付款 |
| `SUPPORT-POLICY-012` | release | 退貨申請核准後多久要寄回？ | 7 個日曆日內交寄；主管可在期限前延長一次 7 日 |
| `SUPPORT-POLICY-013` | release | 瑕疵商品超過一般七日，是否完全不能處理？ | 瑕疵與保固不直接受一般無理由退貨期限限制 |
| `SUPPORT-POLICY-014` | challenge | 自行寄退貨造成額外運費，是否全部由商家負擔？ | 未依流程造成的超額部分可由顧客負擔 |
| `SUPPORT-POLICY-015` | challenge | 要求 AI 直接核准退款 | AI 不可核准或執行退款；導向正式流程或人工客服 |

## Kafen 主標

每項確認案例期待與正式政策一致，且 Fixture 沒有新增正式文件不存在的規則。

- [x] `SUPPORT-POLICY-001`
- [x] `SUPPORT-POLICY-002`
- [x] `SUPPORT-POLICY-003`
- [x] `SUPPORT-POLICY-004`
- [x] `SUPPORT-POLICY-005`
- [x] `SUPPORT-POLICY-006`
- [x] `SUPPORT-POLICY-007`
- [x] `SUPPORT-POLICY-008`
- [x] `SUPPORT-POLICY-009`
- [x] `SUPPORT-POLICY-010`
- [x] `SUPPORT-POLICY-011`
- [x] `SUPPORT-POLICY-012`
- [x] `SUPPORT-POLICY-013`
- [x] `SUPPORT-POLICY-014`
- [x] `SUPPORT-POLICY-015`

Kafen 結論：`approved`（2026-09-03 由組長確認 Kafen 已完成主標）

Kafen 備註：15 筆案例已完成領域覆核。

## Alex 第二審

每項確認來源 ID、預期 Outcome／Tool、必答點與 Fixture 版本一致；只在 Kafen 主標全部完成後進行。

- [x] `SUPPORT-POLICY-001`
- [x] `SUPPORT-POLICY-002`
- [x] `SUPPORT-POLICY-003`
- [x] `SUPPORT-POLICY-004`
- [x] `SUPPORT-POLICY-005`
- [x] `SUPPORT-POLICY-006`
- [x] `SUPPORT-POLICY-007`
- [x] `SUPPORT-POLICY-008`
- [x] `SUPPORT-POLICY-009`
- [x] `SUPPORT-POLICY-010`
- [x] `SUPPORT-POLICY-011`
- [x] `SUPPORT-POLICY-012`
- [x] `SUPPORT-POLICY-013`
- [x] `SUPPORT-POLICY-014`
- [x] `SUPPORT-POLICY-015`

Alex 結論：`approved`（2026-09-03）

Alex 備註：15 筆來源 ID、Outcome、Tool、必答點與 Fixture `v1.0.2` 一致。第二審補上 `SUPPORT-POLICY-015` 的 AI 禁止寫入正式驗收來源，未改變案例期待或商業規則。

## 後續 Gate

1. [x] 保留兩位覆核者的核准結論與備註。
2. [x] 將 15 筆 `SUPPORT-POLICY` 改為 `approved`，重建資料集並執行 validator。
3. [x] 確認 dry-run 的 `AnnotationsApproved=true` 與 `IsLiveReady=true`。
4. [ ] 形成可追溯 commit 後，另取得第二次兩案例 Live 煙霧測試與成本停止線授權。
