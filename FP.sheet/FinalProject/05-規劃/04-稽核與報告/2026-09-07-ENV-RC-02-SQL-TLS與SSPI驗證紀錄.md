# 2026-09-07 ENV-RC-02 SQL TLS 與 SSPI 驗證紀錄

## 結論

ENV-RC-02 已在 `dev@569369b51daa62e6174c0d8e74f5c5d4aaa49227` 完成。SQL Server 2025 `17.0.1125.2 Standard Developer Edition`、ODBC Driver 18、Windows Authentication 與加密連線均正常；系統 PowerShell 下的單一 Provider 測試 1／1、歷史受阻的完整 Infrastructure AI 範圍 122／122 通過，0 retry、0 skipped。本輪建立的隨機隔離資料庫全部由測試 `finally` 清除，共用 `DoSelectDb` 未修改或刪除。

過往失敗只出現在受限程序的 Windows 整合驗證權杖／程序環境，不能據此把 SQL Server 或 TLS 判為故障。最低成本充分修正是：本機 Windows Authentication 的 Provider-backed／E2E 驗證固定由已登入的系統 PowerShell 執行；不關閉加密、不改 SQL Server 權限、不新增 SQL Login，也不把連線字串或 Credential 寫入 Repository。

## Run contract

| 欄位 | 值 |
|---|---|
| Revision | `569369b51daa62e6174c0d8e74f5c5d4aaa49227`；tracked working tree clean |
| Calling Skill | `diagnose-vue-dotnet-incident`；T2 evidence 由 `capture-test-evidence` 保存 |
| Runner | .NET SDK `10.0.303`、VSTest／`Microsoft.NET.Test.Sdk 17.14.1` |
| Provider | SQL Server `17.0.1125.2 Standard Developer Edition`；ODBC 18 sqlcmd `17.0.1000.7` |
| Authentication／transport | Windows Authentication；實測 `encrypt_option=TRUE`、`auth_scheme=NTLM`、`net_transport=Shared memory` |
| 門檻 | 目標測試全部通過、0 skipped、0 retry；本輪隔離資料庫全部清除 |
| 資料邊界 | 僅隨機 `DoSelectAiSafety_*`／`DoSelectAiCustomBuild_*` 測試庫；不讀取、修改或刪除共用 `DoSelectDb` |

## 證據鏈與假設排除

| 假設 | 結果 | 證據 |
|---|---|---|
| SQL Server 服務或 Instance 不可用 | 排除 | `MSSQL$SQL2025` 為 Running／Automatic；ODBC 18 唯讀連線成功 |
| 本機不支援 SQL Server 所需加密 | 排除 | 同一連線回報 `encrypt_option=TRUE`，伺服器版本與 Edition 可讀回 |
| 專案 Provider 設定仍普遍失敗 | 排除 | 單一 migration／seed／query Provider 測試 1／1，Infrastructure AI 122／122 |
| 受限程序的 SSPI 身分等同使用者系統 CLI | 否 | 歷史受限程序在建庫前失敗；相同 revision、相同測試範圍改由系統 PowerShell 即全綠 |
| 必須停用 TLS、改 Mixed Mode 或新增帳密 | 排除 | 現有 Windows Authentication 與加密連線已充分通過；弱化設定沒有必要且增加 Secret／維運風險 |

## 測試結果

| Command scope | Executed | Passed | Failed | Skipped | Duration |
|---|---:|---:|---:|---:|---:|
| `AiCustomBuildSqlServerTests.ExistingCpu_IsIncludedButExcludedFromPurchaseBudget_AndBuildIsCompleteAndCompatible` | 1 | 1 | 0 | 0 | 測試 13 秒；程序 41.678 秒 |
| `FullyQualifiedName~DoSelect.Infrastructure.Tests.Ai.` | 122 | 122 | 0 | 0 | 測試 38 秒；程序 42.614 秒 |

兩次均以系統 PowerShell、0 retry 執行。第二次執行前已有一個 2026-08-31 建立的 `DoSelectAiSafety_*` 測試庫，執行後仍為同一個；它不是本輪產物，依資料安全邊界不刪除、不修改。本輪新增的兩種前綴資料庫殘留為 0。

## Artifacts 與資訊邊界

| Artifact | 保存 | Bytes | SHA-256 |
|---|---|---:|---|
| 單一 Provider TRX | 本機 `%LOCALAPPDATA%\Temp\env-rc-02-569369b5-20260907` | 3,274 | `5896f093af9631ed385594c8380ee8572a80981ac23c0416ba2078033fda310a` |
| Infrastructure AI TRX | 本機 `%LOCALAPPDATA%\Temp\env-rc-02-569369b5-20260907` | 203,047 | `2b1f20f7fe084395be3370021ff7f5b8e10c8a610b5bb949e9a313c8e45e6838` |
| 去識別 T2 manifest | [[2026-09-07-ENV-RC-02測試證據清單.json]] | 版本控制 | 由 Git blob 保存 |

TRX 不提交 Git；版本控制內容不含帳號、機器名、IP、Windows SID、連線字串、Credential、Secret 或資料列。暫存 Artifact 由本機使用者管理，保留期限由作業系統暫存清理政策決定。

## 結案與限制

- ENV-RC-02：✅ 完成。
- ENV-RC-01：PR #160 已通過 Security、Required CI 與 exact-head review，並 squash merge 為 `dev@569369b5`，可同步關閉。
- DEV-04：仍為進行中；尚須完成 `reset-demo-data.ps1` 的安全空庫重建流程。
- 本紀錄只證明本機 SQL／ODBC／Windows Authentication 與選定 Provider 範圍；不宣稱第二台乾淨電腦、展示環境 Restore 或全專案 Release Ready。
