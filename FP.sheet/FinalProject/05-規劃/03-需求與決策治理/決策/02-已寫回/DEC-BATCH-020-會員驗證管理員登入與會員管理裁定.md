---
type: decision-record
batch_id: DEC-BATCH-020
title: 會員驗證、管理員登入與後台會員管理裁定
status: applied
created_at: 2026-08-24
submitted_at: 2026-08-24
applied_at: 2026-08-24
decision_count: 7
decision_range: DEC-P291～DEC-P297
source: alex 採用 A1、B1、C1、D1、E1、F1、G1 並授權寫回
---

# DEC-BATCH-020｜會員驗證、管理員登入與後台會員管理裁定

## 背景

PR #27 與 PR #38 同時碰觸會員驗證、管理員登入及後台會員管理。Review 發現驗證回應仍有帳號／Token 可區分性，PR #38 又把管理員登入與新的會員管理功能放在同一 PR，並在限流、中央 Audit、停權即時撤銷 Session、重新綁定原子性與自動化測試尚未完成前擴大完整個資存取。若直接合併，安全邊界、統計口徑與相依順序會由當前程式偶然決定。

## 正式決策

| ID | 決策 |
|---|---|
| DEC-P291 | V1 註冊帳號存在性時序驗收採 SQL Server Provider-backed 測試：存在／不存在帳號交錯取樣各 20 次，以中位數差異 `<= 20 ms` 為門檻。此門檻代表降低可區分性，不宣稱嚴格常數時間；註冊與驗證碼申請仍使用相同 `202` 回應，不公開帳號是否存在。 |
| DEC-P292 | 公開 Email 驗證與密碼重設 Token 的無效、已使用、已撤銷與已過期情形分別統一為 `email_token_invalid`、`password_reset_token_invalid`；移除公開 `email_token_expired`、`password_reset_token_expired`，避免用錯誤碼揭露 Token 狀態。內部仍可在不含 Token／完整個資的安全 Log 或 Audit 記錄原因分類。 |
| DEC-P293 | PR #38 只保留 `M-01B` 管理員登入、TOTP、Recovery Code、Enrollment／Rebind、Logout／Session 撤銷；全新的後台會員查詢、個資、停權／復權、重設密碼與統計功能另開獨立 PR。兩者不得以同一 PR 的相互依賴規避個別 Review、測試與回復邊界。 |
| DEC-P294 | 後台會員管理採分層最小權限：`CustomerService` 只可讀遮蔽 Email、電話與必要訂單資訊；`CustomerServiceSupervisor` 可停權／復權並觸發重設密碼信，但不得看完整個資、直接設定密碼或任意編輯會員資料；完整個資查看及會員資料編輯只允許 `PrivacyAdmin`、`SuperAdmin`。完整個資操作必填用途、寫入中央 `AuditLog` 且回應 `Cache-Control: no-store`；寫入另須 RowVersion 與理由。 |
| DEC-P295 | 後台會員退貨率固定為「已完成退貨的不同訂單數 ÷ 已完成訂單數」。分子同一訂單不論有幾筆退貨只計一次；分母為完成訂單；無完成訂單時回 `0`，不得以退貨申請筆數或全部訂單作分母。 |
| DEC-P296 | PR #38 必須等待 PR #27 與 `SH-11／DES-24` 中央 AuditLog 共用能力完成，之後 rebase 最新 `dev` 再執行完整驗證。停權／安全狀態變更必須能立即撤銷既有 Session；Audit 失敗時高風險狀態變更不得提交。相依未完成前，CI 綠燈不等於可合併。 |
| DEC-P297 | 核准以 NuGet Central Package Management 精確鎖定 `QRCoder 1.6.0` 產生 TOTP QR Code，並核准管理員 TOTP Enrollment、Rebind 與 Logout／Session 撤銷 Endpoint 納入 `M-01B`。套件必須來自正式 NuGet 來源並通過乾淨 Restore、Build、Test 與漏洞掃描；Recovery Code 只顯示一次，Enrollment／Rebind 使用短效 Challenge，未完成 MFA 不得取得完整管理員 Session。 |

## 最低成本分析

1. 維持 PR #38 現況：會把兩個安全邊界不同的功能綁在一起，且中央 Audit、即時撤銷與測試缺口無法由 CI 綠燈補足，不採用。
2. 只以 Review 留言提醒開發者：無法形成後續 API、Policy、測試與合併 Gate 的權威依據，不採用。
3. 沿用既有 Cookie／Identity、Policy、中央 Audit、SQL Server 測試與 Central Package Management，拆分 PR 並補齊契約：不新增服務或 Schema 即可滿足安全、資料完整性與可回復性，採用。
4. 另建會員管理服務、第二套認證或專用 Audit：會增加重複基礎設施與長期維護成本，現階段沒有必要，不採用。

## 商業影響

- 受影響者：登入或重設密碼的會員、管理員、客服、隱私管理員，以及負責稽核與事故處理的人員。
- 目前風險：公開回應可能洩漏帳號／Token 狀態；過寬權限可能暴露或修改完整個資；停權後舊 Session 可能繼續有效；Audit 缺失使高風險操作不可追查。
- 觸及頻率：涵蓋每次註冊、驗證、重設、管理員登入與後台會員操作；實際流量未提供，不建立虛假估算。
- 預期成果：公開驗證回應不可依狀態區分；管理員登入與會員管理可獨立審查／回復；完整個資與高風險操作具最小權限、用途與稽核；退貨率在前後端與測試中一致。
- 建置與持續成本：調整既有 DTO、Policy、Controller、測試與文件；新增一個已核准 NuGet 套件但不新增外部服務或持續費用。
- 主要風險成本：相依 PR 與中央 Audit 未完成前，PR #38 及後台會員管理 PR 必須等待，排程可能延後但可避免安全返工。
- 信心：高；決策沿用既有架構與最小權限原則。20 ms 時序門檻是 V1 工程驗收值，仍須用相同環境的交錯 SQL Server 樣本降低雜訊。
- 成功指標：統一 Token 錯誤碼；20＋20 交錯樣本中位數差符合門檻；PR 拆分；權限正反例、Session 撤銷、Audit rollback、Rebind 中斷及退貨率邊界測試通過；套件掃描為 0 已知漏洞。
- 停止／回復條件：時序門檻不穩定時不得宣稱通過；中央 Audit 或 Session 撤銷無法與狀態變更形成安全邊界時不得合併；QRCoder 來源、相容性或漏洞驗證失敗時移除套件並改回無 QR 的手動金鑰輸入流程。

## 寫回範圍

- [[01-需求/角色與權限]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/02-API與前端契約/API錯誤碼目錄]]
- [[03-架構/08-測試與驗收/M功能測試案例目錄]]
- [[03-架構/01-系統與環境/本機開發環境與版本基線]]
- [[05-規劃/02-分工與交接/工程包/Haru-會員與訂單工程包]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
- [[05-規劃/03-需求與決策治理/決策紀錄]]

## 實作與合併 Gate

- PR #27 先依 DEC-P291～DEC-P292 完成文件／測試對齊；PR #38 再依 DEC-P293、P296 rebase 最新 `dev`，只交付 `M-01B`。
- 後台會員管理另開 PR，依 DEC-P294～DEC-P295 完成 Policy、遮蔽 DTO、RowVersion、Audit、Session 撤銷、統計與 Actor A／B 負面測試。
- 管理員登入至少驗證登入、TOTP Enrollment／Verify、Recovery Code 單次使用、Rebind 中斷不破壞既有綁定、Logout／停權撤銷 Session、限流，以及 Audit 失敗 rollback。
- `QRCoder 1.6.0` 只代表套件方向已核准；實際加入基線前仍須通過正式來源、乾淨 Restore、Build、完整 Test 與 `dotnet list package --vulnerable --include-transitive`。
- 本決策不授權建立或套用 Migration，也不授權在測試 worktree 執行 Git 操作。

---
文件狀態: 已確認
