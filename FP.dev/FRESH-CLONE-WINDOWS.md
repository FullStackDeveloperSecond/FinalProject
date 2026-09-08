# DoSelect 第二台 Windows 電腦完整建置手冊

本手冊用於從未建置過 DoSelect 的 Windows 電腦，完成 Fresh Clone、相依套件驗證、本機 Development 資料庫、三個服務啟動，以及選用的隔離 Demo 環境。完成 Definition of Done 後，才可把 ENV-RC-03 從「未測試」改為「完成」。

## 1. 安全邊界與完成定義

全程使用同一個已登入的 Windows 使用者與系統 PowerShell。SQL Server 採 Windows Authentication；不要啟用 Mixed Mode、不要使用或共用 `sa` 密碼，也不要為了解決 SSPI／TLS 問題改成明碼連線。

不得把下列資料貼入聊天、Issue、PR、截圖、建置紀錄或 Repository：

- `.NET User Secrets` 的值、OpenAI API Key、Brevo SMTP Key、Pepper、HMAC Key、密碼及完整 Connection String。
- Windows 使用者名稱、SID、機器名稱、IP、實際資料列及 `.run` 原始日誌。
- `dotnet user-secrets list` 的輸出。

Fresh Clone 完成必須同時符合：

1. Git、.NET、Node、npm、SQL Server、ODBC 18／`sqlcmd` 前置檢查通過。
2. 指定的 `origin/dev` revision 與 tracked worktree 乾淨。
3. 套件來源 Policy、.NET Restore、Build、Format、Tests 與 NuGet 弱點稽核通過。
4. 雙前端 `npm ci`、Typecheck、零警告 Lint、Coverage、Production Build 與 Production Audit 通過。
5. `DoSelectDb` 套用目前完整 Migration chain；不得只套用過時的 `InitialCreate`。
6. 最小 Seed、兩支 SQL 驗證與 API database smoke 通過。
7. API、Customer Web、Admin Web 啟動，`status.ps1` 與五項健康檢查通過。
8. `stop-all.ps1` 後三個固定 Port 已釋放，tracked worktree 仍乾淨。
9. 以去識別方式填寫 `2026-09-08-ENV-RC-03第二機Fresh-Clone執行紀錄.md`。

任一步失敗時停止在該步驟，不以放寬 SQL 權限、停用 TLS、改用共用密碼、跳過 Secret 驗證或修改 `global.json` 解決。

## 2. 安裝必要工具

只從官方來源下載並選擇符合電腦架構的 Windows 安裝程式：

| 工具 | 專案要求 | 官方來源 |
|---|---|---|
| Git for Windows | 可執行 `git` | <https://git-scm.com/download/win> |
| .NET SDK | **精確 `10.0.303` x64**；`global.json` 禁止 roll forward | <https://learn.microsoft.com/dotnet/core/install/windows>；release note：<https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.11/10.0.303.md> |
| Node.js | Node 24 LTS | <https://nodejs.org/en/download>；版本封存：<https://nodejs.org/en/download/archive/v24> |
| npm | Major 11；使用 Node 24 所附 npm，實際 patch 記入驗收紀錄 | 隨 Node.js 安裝 |
| SQL Server | SQL Server 2025 Developer Edition，具名 Instance **`SQL2025`** | <https://www.microsoft.com/sql-server/sql-server-downloads> |
| ODBC／sqlcmd | Microsoft ODBC Driver 18 與 ODBC `sqlcmd` | <https://learn.microsoft.com/sql/connect/odbc/download-odbc-driver-for-sql-server>；<https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-download-install> |

GitHub CLI、Visual Studio、VS Code 與 SSMS 是方便工具，不是應用啟動必要條件。若安裝 SQL Server：

1. 選擇 Developer Edition。
2. Instance 名稱輸入 `SQL2025`，完成後服務名稱應為 `MSSQL$SQL2025`。
3. Authentication 選 Windows Authentication。
4. 將目前 Windows 使用者加入本機 SQL 管理者，使其能建立 `DoSelectDb` 與隔離測試／Demo 資料庫。
5. 不開放公網、不建立共用 SQL Login；此基線只適用本機 Development／Demo。

重新開啟系統 PowerShell，先做不修改專案的檢查：

```powershell
git --version
dotnet --version
node --version
npm --version
Get-Service -Name 'MSSQL$SQL2025'
sqlcmd -S .\SQL2025 -E -C -b -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion, CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Edition;"
```

`dotnet --version` 必須是 `10.0.303`、Node Major 必須是 24、npm Major 必須是 11，SQL 服務必須為 Running，且 `sqlcmd` 必須以目前 Windows 使用者成功連線。

## 3. Fresh Clone 與 revision 固定

選擇不會和資料根目錄重疊的程式碼目錄；以下以 `C:\Work` 為例：

```powershell
$workspaceRoot = 'C:\Work'
New-Item -ItemType Directory -Force -Path $workspaceRoot | Out-Null
Set-Location $workspaceRoot
git clone https://github.com/FullStackDeveloperSecond/FinalProject.git
Set-Location .\FinalProject
git switch dev
git pull --ff-only origin dev
git rev-parse HEAD
git status --short
```

把 `git rev-parse HEAD` 的完整 40 字元 SHA 與交付者提供的預期 `origin/dev` SHA 比對。`git status --short` 必須沒有輸出。若 SHA 不符，停止並確認交付版本；不要自行 merge、rebase 或 checkout 未核准分支。

進入程式根目錄並執行快速前置檢查：

```powershell
Set-Location .\FP.dev
.\scripts\verify-clean-environment.ps1
```

## 4. 建立本機非機密設定

Development 設定檔不進版控。複製範本：

```powershell
Copy-Item `
  .\src\backend\DoSelect.Api\appsettings.Development.example.json `
  .\src\backend\DoSelect.Api\appsettings.Development.json
```

若電腦沒有 `E:`，編輯剛建立的 `appsettings.Development.json`，只把 `Storage:DataRoot` 改成 Repository 外、非磁碟根目錄的絕對路徑，例如：

```json
"Storage": {
  "DataRoot": "C:\\DoSelectData"
}
```

然後建立相同目錄：

```powershell
New-Item -ItemType Directory -Force -Path 'C:\DoSelectData' | Out-Null
```

本機預設 API 是 `http://localhost:5126`，兩個前端現有 `.env.example` 已對應此網址，因此不必建立 `.env.local`。只有確定要改 API URL 時，才分別把 `.env.example` 複製成未追蹤的 `.env.local`；URL 不得含帳密、Query 或 Fragment。

## 5. 建立每台電腦獨立的安全 Secrets

執行下列腳本。它會為目前 Windows 使用者產生獨立高熵值，直接寫入 API 專案的 .NET User Secrets，不顯示值，也不寫入 Repository：

```powershell
.\scripts\configure-local-security-secrets.ps1
```

此步會設定：

- `GuestOrderAccess:Pepper`：API 啟動必要。
- `Idempotency:ActorScopePepper`：購物車合併、建立訂單、退款等冪等操作必要。
- `Security:CouponGuestUsageHmacKeyV1`：訪客使用優惠券時必要。

再以隱藏輸入設定本機 Seed 帳號密碼：

```powershell
.\scripts\configure-seed-secrets.ps1
```

不得執行或保存 `dotnet user-secrets list`。需要確認設定時，以後續 API fail-closed 啟動與健康檢查作為證據。

## 6. 完整 Restore、Build 與測試

這一步可能花較長時間並需要連線官方 NuGet／npm registry。它會使用鎖定檔、執行完整 .NET 與前端驗證，並可能建立後刪除名稱受限的隔離測試資料庫；不會刪除或清空共用 `DoSelectDb`：

```powershell
.\scripts\stop-all.ps1
.\scripts\verify-clean-environment.ps1 -RunVerification
```

Fresh Clone 第一次執行時 `stop-all.ps1` 可安全回報沒有受管程序；若曾啟動同一 Clone，它只停止狀態檔中 PID＋啟動時間吻合的本專案程序。完整驗證會先確認固定 Port 5126／5173／5174 空閒，再開始耗時工作，避免前端 native module 被執行中服務鎖住。

成功訊息應為 `Clean-environment verification passed.`。若失敗，保留去識別後的命令名稱、結束碼與錯誤摘要；不要貼出環境變數、完整 Connection String、User Secrets 或原始資料列。

## 7. 建立 Development 資料庫

先確認本專案服務已停止，再執行單一初始化入口：

```powershell
.\scripts\stop-all.ps1
.\scripts\initialize-development-database.ps1
```

此入口固定使用本機 `.\SQL2025`、資料庫 `DoSelectDb` 與 Windows Authentication，且不接受資料庫名稱或 Connection String 參數。它會依序完成：

1. 確認固定 Port 5126／5173／5174 均未被占用。
2. 還原 Repository-local `dotnet-ef`，套用目前完整 Migration chain。
3. 建立可重複執行的最小資料。
4. 執行 Schema 與最小 Seed SQL 驗證。
5. 短暫啟動 API 完成資料庫 Readiness smoke test，再清理程序。

成功訊息應為 `Development database migration, seed and verification passed.`。個別 Migration、Seed、SQL 或 smoke 命令只供失敗診斷，不作為 Fresh Clone 的標準建置流程。

Migration／Seed 失敗時不得手動改 Migration History、不得重新 scaffold Migration，也不得刪除既有資料來掩蓋錯誤。Fresh Clone 若第一次建立 `DoSelectDb` 仍失敗，記錄去識別錯誤後停止。

## 8. 啟動完整 Development 系統

固定 Port 為 API `5126`、Customer Web `5173`、Admin Web `5174`。啟動前若曾執行過服務，先安全停止本專案記錄的程序：

```powershell
.\scripts\stop-all.ps1
.\scripts\start-all.ps1
.\scripts\status.ps1
.\scripts\health-check.ps1
```

瀏覽器確認：

- Customer Web：<http://localhost:5173>
- Admin Web：<http://localhost:5174/admin/>
- API Liveness：<http://localhost:5126/health/live>
- API Readiness：<http://localhost:5126/health/ready>

Development 才提供 OpenAPI：<http://localhost:5126/openapi/v1.json>。健康回應只應包含整體 `status`，不得出現路徑、Connection String、例外或 Secret。

完成檢查後停止服務：

```powershell
.\scripts\stop-all.ps1
Get-NetTCPConnection -State Listen -LocalPort 5126,5173,5174 -ErrorAction SilentlyContinue
git status --short
```

前一個命令應顯示成功停止；Port 查詢與 `git status --short` 均應沒有輸出。不要以 `taskkill /F /IM node.exe` 或批次終止全部 .NET／Node 程序。

## 9. 選用：建立正式隔離 Demo 環境

只有完成 Development 驗收，並取得目前核准的 Demo runtime exact SHA 後才做。SHA 以 `FP.sheet/FinalProject/04-展示/01-Demo與彩排/Demo前檢查表.md` 為準；文件 commit 可以較新，但 runtime、Prompt、Schema 或 Seed 變更時必須重新固定。

```powershell
Set-Location ..
git checkout --detach <核准的-runtime-40字元-SHA>
git rev-parse HEAD
Set-Location .\FP.dev
.\scripts\verify-clean-environment.ps1
.\scripts\stop-all.ps1
.\scripts\reset-demo-data.ps1
.\scripts\start-all.ps1 -Environment Demo
.\scripts\status.ps1
.\scripts\health-check.ps1
```

`reset-demo-data.ps1` 每次預設建立新的 `DoSelectDemo_<32-hex>`，完成 Seed 與 11 項唯讀 Gate 後才發布 `.run\demo-database.json`。不得把名稱改成 `DoSelectDb` 或共用 `DoSelectDemo`，也不得刪除非本輪建立的資料庫。

Demo 驗收後：

```powershell
.\scripts\stop-all.ps1
```

真人 15～20 分鐘操作彩排與備援影片是不同 Gate；只完成本手冊不得把真人彩排或 DEMO-RC-03 改寫為通過。

## 10. 選用 Provider

Fresh Clone／ENV-RC-03 不需要啟用 OpenAI 或 Email。基本系統應先在兩者停用時完成驗收。

- 需要 OpenAI 時，使用 `.\scripts\configure-openai-eval-secrets.ps1` 隱藏輸入 API Key；只有確認模型、價格、預算與核准對象後才設定 `Features:AiEnabled=true`。若需要匿名 AI 身分，再執行 `.\scripts\configure-local-security-secrets.ps1 -IncludeAiAnonymousIdentity`。不得把未知價格填成 `0`。
- 需要 Brevo 時，執行 `.\scripts\configure-brevo-secrets.ps1`，再以 `.\scripts\test-brevo-smtp.ps1` 寄送不含會員資料的測試信。不要經聊天或 Repository 搬移 SMTP Key。

## 11. 交付驗收證據

複製並填寫：

`FP.sheet/FinalProject/05-規劃/04-稽核與報告/2026-09-08-ENV-RC-03第二機Fresh-Clone執行紀錄.md`

只記錄 revision、工具版本、命令、Pass／Fail、UTC 時間、去識別錯誤摘要，以及經去識別日誌的 SHA-256。不要開啟涵蓋 Secret 輸入的全程 Transcript。ENV-RC-03 只有在所有必要列均通過後才能勾選完成。

## 12. 常見阻擋

| 症狀 | 正確處理 | 禁止處理 |
|---|---|---|
| `dotnet --version` 不是 `10.0.303` | 安裝精確 SDK，重新開 PowerShell | 修改 `global.json` 或允許 roll forward |
| 無法連線 `.\SQL2025` | 確認 Instance／服務名稱、目前 Windows 使用者權限與 ODBC 18 | 開 Mixed Mode、使用 `sa`、停用 TLS或硬編密碼 |
| API 提示 Pepper 缺少 | 重跑安全 Secret 腳本 | 在 JSON、環境日誌或聊天填入值 |
| 初始化或啟動顯示 Port 5126／5173／5174 被占用 | 依訊息中的 PID 判斷是否為本專案；本專案用 `stop-all.ps1`，其他程序由其原本工具停止 | 自動換 Port，或批次終止所有 Node／.NET 程序 |
| `npm ci` 改動 lockfile 或失敗 | 確認 Node 24／npm 11、官方 registry 與乾淨 clone | 改用 `npm install` 後提交未知 lockfile |
| Migration 失敗 | 保留錯誤並停止，確認完整 chain 與 SQL 權限 | 指定舊 `InitialCreate`、手改 History、重建 Migration |
| AI／Email 無憑證 | 保持功能停用，完成基本系統驗收 | 假 Key、共享 Key、把錯誤宣稱為 Live 成功 |
