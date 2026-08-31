---
文件狀態: 草稿
最後更新: 2026-08-26
---

# DES-24 中央 Audit 移交清單（haru → alex）

本頁回應 PR #40 組長回覆：`haru/feature/guest-ordertracking` 分支上已有 Admin 2FA／訪客查單流程用的 AuditLog Entity、Configuration、Writer 與 Migration。DES-24（中央 Audit）正式 Owner 是 `alex`，且 `alex` 已在 `codex/des24-audit` 分支（commit `f27535c`）另外實作了同交易語意的 `AuditLog`／`EfAuditWriter`。本頁盤點兩邊差異，供雙方對齊抽取／重構方式，避免這支 feature branch 的 Migration／ModelSnapshot 直接變成中央 Audit 的正式來源。

## 結論先講：交易語意不相容，不能直接合併

`haru` 這邊的 [AuditLogWriter.cs](../../../FP.dev/src/backend/DoSelect.Infrastructure/Security/AuditLogWriter.cs) 在 `RecordAsync` 內自行呼叫 `SaveChangesAsync`——這是當初刻意的設計，因為只服務 Admin 登入鎖定、TOTP／Recovery Code 驗證這類認證流程，不涉及金流。

`alex` 的 `EfAuditWriter.Add()`（`codex/des24-audit`）只把 `AuditLog` 加進同一個 `DbContext`、不呼叫 `SaveChanges`，交易由呼叫端（例如退款執行 Use Case）的既有 `SaveChangesAsync` 一併提交。這才符合 [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]] 第 150 行「退款執行等高風險 Use Case...在中央實作完成前不得以局部 Audit、獨立交易或省略 Audit 合併」的要求。

兩者語意衝突，**haru 這支 Writer 不能被拿來當退款／折讓的正式 Audit 來源**。

## 可安全移交／可參考重用

| 檔案 | 可重用程度 | 備註 |
|---|---|---|
| [AuditLogEntry.cs](../../../FP.dev/src/backend/DoSelect.Domain/Security/AuditLogEntry.cs) | 中 | 欄位設計（Actor/Action/Resource/Outcome/CorrelationId）與 `alex` 的 `AuditLog.cs`（`DoSelect.Domain/Auditing`）高度相似，可對照決定是否合併成同一張表，或維持兩張表分開治理 |
| [AuditLogEntryConfiguration.cs](../../../FP.dev/src/backend/DoSelect.Infrastructure/Persistence/Configurations/Security/AuditLogEntryConfiguration.cs) | 中 | EF Configuration 寫法可參考；若表最終合併，這支 Configuration 本身不會被留用 |
| [AuditLogPorts.cs](../../../FP.dev/src/backend/DoSelect.Application/Security/AuditLogPorts.cs)（`AuditLogOutcomes`／`AuditLogEntryDraft`／`IAuditLogWriter`） | 低～中 | 介面設計可參考，但語意（非同步、自行 SaveChanges）跟 `alex` 的 `IAuditWriter.Add()`（同步、不 SaveChanges）不同，不能直接沿用 |

## 不該直接搬進中央 Audit

| 檔案 | 原因 |
|---|---|
| [AuditLogWriter.cs](../../../FP.dev/src/backend/DoSelect.Infrastructure/Security/AuditLogWriter.cs) | 自行 `SaveChangesAsync`，與退款／折讓要求的同交易語意衝突，見上方結論 |
| [20260825014135_AddAuditLogs.cs](../../../FP.dev/src/backend/DoSelect.Infrastructure/Persistence/Migrations/20260825014135_AddAuditLogs.cs) 及對應 `.Designer.cs`、`DoSelectDbContextModelSnapshot.cs` 的異動 | 這支 Migration 建的是 `AuditLogs`（haru 的表），`alex` 的 `AddCentralAuditLogs`（`codex/des24-audit`，commit `f27535c`）建的是另一張中央表。兩者若都合併進 `dev`，ModelSnapshot 會衝突，且會留下兩張語意重疊的表。**不得讓這支 Migration 成為正式來源**；需等對齊後，依 `alex` 的 Migration 為準，這支要嘛廢棄、要嘛改名／限縮成 auth 專用表 |

## 呼叫端（待對齊：維持 auth 專用 or 改接中央 Audit）

以下呼叫端目前都只記錄「認證／授權事件」，不涉及退款金流，可作為「或許維持獨立 auth audit、不必進中央表」的討論基礎，但最終是否合併由 `alex`／組長拍板：

- [AdminAuthController.cs](../../../FP.dev/src/backend/DoSelect.Api/Admin/Auth/AdminAuthController.cs)：4 處呼叫（登入鎖定、TOTP／Recovery Code 驗證失敗、重新綁定成功／失敗）
- [AdminTwoFactorUseCase.cs](../../../FP.dev/src/backend/DoSelect.Application/Security/AdminTwoFactorUseCase.cs)
- [GuestOrderAccessScopeAuthorizer.cs](../../../FP.dev/src/backend/DoSelect.Application/Orders/GuestOrderAccessScopeAuthorizer.cs)：訪客查單授權被拒時記錄
- DI 註冊：[AdminAuthServiceCollectionExtensions.cs](../../../FP.dev/src/backend/DoSelect.Infrastructure/Security/AdminAuthServiceCollectionExtensions.cs) 第 21 行 `services.AddScoped<IAuditLogWriter, AuditLogWriter>()`

未發現獨立測試檔直接覆蓋 `AuditLogWriter`／`AuditLogEntry`，僅在 Controller／UseCase 測試中間接覆蓋。

## 待與 alex 對齊的問題

1. Admin 2FA／訪客查單這類認證事件，是否併入中央 Audit 表，或維持獨立 auth-only 表？
2. 若併入，`AuditLogEntry` 的欄位要如何映射到 `AuditLog`（`Auditing` namespace）的欄位（例如 `ChangedFields`／`RetainUntil`／`IsLegalHold` 這些 haru 版本沒有的欄位）？
3. `haru` 的 `20260825014135_AddAuditLogs` Migration 如何處理——直接移除改由中央 Migration 涵蓋，或保留成獨立表但改名避免衝突？
4. 呼叫端切換時機：是否等中央 `IAuditWriter` 合入 `dev` 後，由 `haru` 這邊改接口，還是由 `alex` 一併處理？

## 相關文件

- [[03-架構/03-資料與一致性/資料一致性、Outbox與冪等設計]]
- [[知識點/04-資料模型與一致性/Audit Log]]
- [[05-規劃/02-分工與交接/開發分工與交接]]
