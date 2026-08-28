---
文件狀態: 已寫回
最後更新: 2026-08-29
decision_type: reviewed
status: applied
applied_at: 2026-08-29
裁定人: alex
提出人: yinyin
相關 PR: "#16"
---

# DEC-B1｜退款執行的跨模組具名例外

## 背景

工程包第 7 節（`Yinyin-優惠付款退款與發票工程包.md` 第 135 行）規定：

> 不得讀取其他模組 Repository／DbContext 或底層表。跨模組同步交易由 Application Use Case 協調同一 Unit of Work。

M-13 退款執行實作違反了這條：可信快照、分攤寫入與 `RefundDto` 投影都直接讀了
Orders／Returns 模組的表。這件事由 yinyin 於 PR #16 主動回報，alex 於
2026-08-28 完成完整掃描並正式裁定。

同時發現既有守門測試 `TheReaderOnlyTouchesTablesThisModuleOwns` 只掃單一檔案
`RefundExecutionReader.cs`，因此**新增任何 Reader 都能繞過**。

## 定案

採用**具名、窄範圍**的例外，**不是**其他模組可自行類推的通則。
退款執行可以使用既有 `DoSelectDbContext` 完成下列需求：

| # | 元件 | 獲准存取的資料表 | 用途 |
|---|---|---|---|
| B1-1 | `RefundTrustedInputsReader` | `ReturnRequests`、`ReturnItems`、`Orders`、`OrderItems`、`OrderCoupons` | 只讀取退款計算所需的可信快照欄位 |
| B1-2 | `RefundExecutor.WriteAllocationsAsync` | `OrderItems` | 只解析分攤寫入所需的內部主鍵 |
| B1-3 | `RefundReader` | `Orders`、`ReturnRequests`、`OrderItems`、`Users` | 正式 `RefundDto` 的唯讀投影，只取回應契約需要的公開識別與遮蔽標籤 |
| B1-4 | `RefundExecutor` 的管理員讀取 | `Users`、`UserRoles`、`Roles`、`AdminProfiles` | 冪等 Actor Scope、執行當下授權重查與中央 Audit Actor |

本模組自有的表（`Refunds`、`RefundAllocations`、`PaymentAttempts`）不在例外範圍內，
本來就可以存取。

### 欄位範圍

| 元件 | 欄位 |
|---|---|
| B1-1 `ReturnRequests` | `ReasonCode`、`AssemblyFeeDisposition`、`ReturnShippingCost` |
| B1-1 `ReturnItems` | `OrderItemId`、`Quantity` |
| B1-1 `Orders` | `ShippingFee`、`AssemblyFee`、`ShippingFreeThresholdSnapshot`、`ShippingMethodBaseFeeSnapshot` |
| B1-1 `OrderItems` | `Id`、`PublicId`、`Quantity`、`FinalUnitPrice`、`DiscountAllocation`、`IsCouponEligible` |
| B1-1 `OrderCoupons` | `AppliedAmount`、`EligibleSubtotal`、`MinimumSpendAmount` |
| B1-2 `OrderItems` | `Id`、`PublicId`、`OrderId` |
| B1-3 | 回應契約所需的 `PublicId`、`OrderNumber`、遮蔽標籤欄位 |
| B1-4 | `Id`、`PublicId`、`AccountType`、`AccountStatus`、`IsActive`、角色名稱 |

## 落地要求

1. **文件逐一明列元件、資料表、欄位與用途** —— 即本文件。
2. **Gateway／Reader 不得自行 `BeginTransaction`／`Commit`**；交易仍由
   `IIdempotencyExecutor` 擁有。
3. **守門測試改掃完整 Refund Infrastructure，採逐元件白名單**，不得再只掃單一檔名；
   新增元件若未列進白名單必須直接失敗。
4. **白名單以目前核准欄位為上限。** 未來新增跨模組欄位仍需重新 review，
   不得把 B1 擴張成任意直接存取。

## 實作對應

| 要求 | 對應 |
|---|---|
| 1 | 本文件 |
| 2 | `NoRefundInfrastructureComponentOwnsItsOwnTransaction` |
| 3 | `EveryRefundInfrastructureComponentStaysInsideItsNamedException` |
| 4 | 同上；白名單即該測試內的 `allowed` 字典 |

守門測試已實測會擋下兩種繞過：新增未列名的元件、以及既有元件存取白名單外的表。

## 與 GATE-TX-01 的關係

`核心交易整合協調.md` 的 GATE-TX-01 對 Checkout Gateway 也給過類似的具名許可
（DEC-BATCH-027／PR #52）。兩者都是**個別裁定**，不是通則；工程包第 7 節的禁止
條款對其他模組仍然有效。

## 相關連結

- Pull Request：#16
- 工程包：`05-規劃/02-分工與交接/工程包/Yinyin-優惠付款退款與發票工程包.md` 第 7 節
- 相關：GATE-TX-01（`05-規劃/01-時程與進度/核心交易整合協調.md`）
