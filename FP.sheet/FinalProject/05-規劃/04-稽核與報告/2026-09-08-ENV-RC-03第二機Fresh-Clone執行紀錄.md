---
文件狀態: 待執行
建立日期: 2026-09-08
追蹤項目: [ENV-RC-03, DEV-02]
執行人: 待填
---

# ENV-RC-03｜第二機 Fresh Clone 執行紀錄

本紀錄只保存去識別結果。不得填入使用者名稱、SID、機器名稱、IP、Secret、密碼、完整 Connection String、資料列或 `.run` 原始日誌。

## 執行基準

| 欄位 | 值 |
|---|---|
| 預期 `origin/dev` 完整 SHA | `<待填>` |
| 實際 `git rev-parse HEAD` | `<待填>` |
| 開始 UTC | `<待填>` |
| 結束 UTC | `<待填>` |
| Windows 版本（不含裝置名稱） | `<待填>` |
| PowerShell 版本 | `<待填>` |
| Git 版本 | `<待填>` |
| .NET SDK | `<待填>` |
| Node.js | `<待填>` |
| npm | `<待填>` |
| SQL Server ProductVersion／Edition | `<待填>` |
| ODBC Driver／sqlcmd | `<待填>` |

## 驗收結果

| 完成 | 步驟 | 結果／去識別證據 |
|---|---|---|
| - [ ] | Fresh Clone、`dev`、exact SHA 與初始 tracked worktree clean | `<PASS／FAIL；摘要>` |
| - [ ] | `verify-clean-environment.ps1` 前置檢查 | `<PASS／FAIL；摘要>` |
| - [ ] | 非機密 Development 設定與 Repository 外 DataRoot | `<PASS／FAIL；只記路徑類型，不記使用者路徑>` |
| - [ ] | 本機安全 Secrets 與 Seed 密碼已設定 | `<PASS／FAIL；只記 Key 是否完成，不記值>` |
| - [ ] | `verify-clean-environment.ps1 -RunVerification` | `<PASS／FAIL；測試數與結束碼>` |
| - [ ] | 完整 Migration chain 套用至 `DoSelectDb` | `<PASS／FAIL；最後 Migration ID>` |
| - [ ] | 最小 Seed 與兩支 SQL 驗證 | `<PASS／FAIL；摘要>` |
| - [ ] | API database smoke | `<PASS／FAIL；摘要>` |
| - [ ] | `start-all.ps1` 與 `status.ps1` | `<PASS／FAIL；三服務狀態>` |
| - [ ] | `health-check.ps1` | `<PASS／FAIL；SQL、live、ready、customer、admin 共 5 項>` |
| - [ ] | 瀏覽器基本登入、商品列表與管理入口 | `<PASS／FAIL；不記帳號>` |
| - [ ] | `stop-all.ps1`、固定 Port 釋放 | `<PASS／FAIL；摘要>` |
| - [ ] | 最終 tracked worktree clean | `<PASS／FAIL>` |

## 選用 Demo 驗證

| 完成 | 步驟 | 結果／去識別證據 |
|---|---|---|
| - [ ] | 核准的 Demo runtime exact SHA 讀回 | `<SHA／未執行>` |
| - [ ] | 新 `DoSelectDemo_<32-hex>` Seed 與 11／11 Gate | `<PASS／FAIL／未執行；不得記完整資料庫名稱>` |
| - [ ] | Demo 三服務與五項健康檢查 | `<PASS／FAIL／未執行>` |
| - [ ] | Demo 停止及本輪隔離資源清理 | `<PASS／FAIL／未執行>` |

## 失敗與裁定

| UTC | 步驟 | 去識別錯誤摘要 | 是否阻擋 ENV-RC-03 | 處理／待決策 |
|---|---|---|---|---|
| `<待填>` | `<待填>` | `<待填>` | `<是／否>` | `<待填>` |

## 證據完整性

不要對 Secret 設定階段開啟 Transcript。若另存已去識別的驗證日誌，只記錄：

| 檔案代號 | Bytes | SHA-256 | 敏感樣式檢查 |
|---|---:|---|---|
| `<代號；不填使用者路徑>` | `<待填>` | `<待填>` | `<0／待處理>` |

## 最終判定

- [ ] 所有 ENV-RC-03 必要列均為 PASS。
- [ ] 沒有 Secret／身分／主機資訊／資料列進入本紀錄或 Repository。
- [ ] 沒有修改安全門檻、Branch Protection、Migration、Schema 或共用資料庫來使驗證通過。
- [ ] ENV-RC-03 可由文件負責人改為完成。

目前判定：`未測試`。只有第二台電腦實際完成上述必要列後才可更新。
