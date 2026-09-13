# DoSelect 跨電腦建置包規劃（待審核）

狀態：**待使用者審核，尚未建立壓縮檔**

規劃日期：2026-09-13（Asia/Taipei）
目前遠端基準：`origin/dev` / `2bbeb144d203dd7e54326669fd88b3de0819fcf8`

本文件規劃如何把 DoSelect 搬到另一台 Windows 電腦，建立可重現的 Development／Demo 環境。它不是正式環境部署手冊，也不授權搬移本機 Secret、真實個資或目前資料庫內容。

## 1. 建議交付方式

建議採用「**乾淨原始碼包＋新電腦線上還原相依套件＋全新資料庫重建**」：

1. 先將目前要交付的程式與文件整理、提交並推送到 `origin/dev`。
2. 從核准的遠端 commit 建立新的 shallow clone，不從目前髒的工作目錄直接壓縮。
3. 壓縮該乾淨 clone，保留最小 Git metadata，使既有的 revision 與 clean-worktree 驗證可直接執行。
4. 新電腦只從官方來源安裝必要軟體，NuGet 與 npm 依鎖定檔重新下載套件。
5. SQL Server 以 EF Core 完整 Migration chain 與 Seed 建立新資料庫，不複製目前電腦的 MDF／LDF。
6. 每台電腦各自建立 .NET User Secrets；AI、Email 仍預設停用。

這個方案的壓縮檔較小、來源清楚，也不會把目前的本機產物或 Secret 帶到另一台電腦。若新電腦完全不能連網，需改採第 10 節的離線方案，不能沿用此預設。

## 2. 壓縮前硬性關卡

目前程式與文件已整理成獨立本機 commits，但尚未推送至 `origin/dev`，也尚未取得新遠端 head 的 CI 結果。因此目前**不可建立最終壓縮檔**：

- 直接對舊遠端 commit 做 `git archive` 會漏掉本機新 commits。
- 直接壓縮目前資料夾會混入 `.run`、套件快取、測試產物、本機設定或其他不應交付的內容。

壓縮前必須完成：

- [ ] 確認所有要交付的程式與文件已提交。
- [ ] 確認交付 commit 已推送到 `origin/dev`。
- [ ] 記錄並由使用者核准完整 40 字元 commit SHA。
- [ ] 從遠端該 commit 建立全新的 shallow clone。
- [ ] 在乾淨 clone 執行 Secret／產物掃描與完整 CI 標準驗證。
- [ ] 以乾淨 clone 建立壓縮檔，不使用目前工作目錄作為來源。
- [ ] 產生壓縮檔 SHA-256，記錄檔名、大小、commit SHA 與建立時間。
- [ ] 解壓到新的暫存路徑，依手冊做至少一次套件還原與結構檢查。

## 3. 預定壓縮內容

建議檔名：`DoSelect-Windows-Transfer-<短SHA>-20260913.zip`

預定內容：

```text
DoSelect-Windows-Transfer/
├─ README-FIRST.md                         # 搬機入口、版本與安全提醒
├─ PACKAGE-MANIFEST.txt                    # commit、檔案範圍、工具版本、建立時間
├─ SHA256SUMS.txt                          # 壓縮檔或交付檔案雜湊
└─ FinalProject/                           # 從核准遠端 commit 建立的乾淨 shallow clone
   ├─ .git/                                # 最小歷史，供 revision／clean 驗證與後續更新
   ├─ .github/                             # CI 定義
   ├─ FP.dev/                              # API、兩個 Vue 前端、Migration、Seed、腳本與建置手冊
   ├─ FP.sheet/                            # 已核准並提交的專案／操作文件
   ├─ .gitignore
   └─ README.md
```

`FP.dev/FRESH-CLONE-WINDOWS.md` 繼續作為新電腦的逐步建置手冊；壓縮前會再檢查其中版本、路徑、Demo 流程及命令是否與核准 commit 一致。

## 4. 明確排除的內容

下列內容不進壓縮檔，也不應透過聊天或文件搬移：

- `.NET User Secrets`、OpenAI API Key、Brevo SMTP Key、HMAC Key、Pepper、密碼、TOTP Seed／QR Code、Recovery Code。
- `.env`、`.env.local`、`appsettings.Development.json`、實際 Connection String。
- `.run`、Log、PID／狀態檔、Crash dump、截圖、測試錄影與 Trace。
- `node_modules`、NuGet 全域快取、`bin`、`obj`、`dist`、coverage、Playwright report／test-results。
- `.pnpm-store`、`.worktrees`、臨時檔、IDE 個人設定與目前工作目錄的額外 worktree。
- SQL Server MDF／LDF、目前 `DoSelectDb`、目前 Demo／測試資料庫及備份檔。
- Repository 外的 Storage DataRoot、附件、客服檔案或其他可能含個資的檔案。
- 不必要的完整 Git 歷史；只保留交付所需的 shallow clone。

若明確要求保留目前 Demo 資料，必須另案確認資料分類、去識別、附件範圍、備份密碼、傳輸方式與還原測試；不可把它默認塞進原始碼包。

## 5. 新電腦必要條件

預設目標是 Windows x64、可連外、具有本機系統管理員權限，且可以安裝本機 SQL Server named instance。必要工具：

| 工具 | 專案固定條件 | 用途 |
|---|---|---|
| Git for Windows | 可執行 `git` | 驗證版本、工作目錄與後續更新 |
| .NET SDK | 精確 `10.0.303`；`global.json` 禁止 roll forward | API restore、build、test、EF Migration 與執行 |
| Node.js | Major 24 LTS | 兩個 Vue 前端 |
| npm | Major 11 | 依 `package-lock.json` 執行 `npm ci` |
| SQL Server | SQL Server 2025 Developer、Instance `SQL2025`、Windows Authentication | 本機 Development／Demo 資料庫 |
| ODBC Driver／sqlcmd | Microsoft ODBC Driver 18 與 ODBC `sqlcmd` | 資料庫檢查、Seed 與驗證腳本 |

官方下載與安裝連結由 `FRESH-CLONE-WINDOWS.md` 統一維護。Visual Studio、VS Code、SSMS、GitHub CLI 只屬方便工具，不是系統啟動必要條件。

## 6. 新電腦建置與啟動流程

解壓後，在系統 PowerShell 依下列階段執行；詳細命令以 `FP.dev/FRESH-CLONE-WINDOWS.md` 為準。

### A. 驗證交付物

1. 比對壓縮檔 SHA-256。
2. 解壓至一般工作資料夾，不放在 OneDrive 同步目錄、系統目錄或 SQL 資料目錄。
3. 進入 `FinalProject`，比對 `git rev-parse HEAD` 與 `PACKAGE-MANIFEST.txt`。
4. `git status --short` 必須無輸出。

### B. 安裝並驗證必要工具

1. 安裝 Git、.NET SDK、Node/npm、SQL Server 2025 Developer、ODBC 18 與 `sqlcmd`。
2. SQL Instance 使用 `SQL2025`，Windows Authentication，將目前 Windows 使用者加入本機 SQL 管理者。
3. 驗證 .NET、Node、npm 版本，以及 `sqlcmd -S .\SQL2025 -E -C` 能連線。

### C. 建立本機設定與安全資料

1. 將 `appsettings.Development.example.json` 複製為未追蹤的 `appsettings.Development.json`。
2. 若新電腦沒有 `E:`，把 `Storage:DataRoot` 改至 Repository 外的本機資料夾並建立該資料夾。
3. 執行 `configure-local-security-secrets.ps1`，由新電腦產生獨立 Pepper／HMAC 值。
4. 執行 `configure-seed-secrets.ps1`，以隱藏輸入設定測試帳號密碼。
5. 不輸出或保存 `dotnet user-secrets list`。

### D. 還原套件與完整驗證

1. 停止受管服務。
2. 執行 `verify-clean-environment.ps1 -RunVerification`。
3. 此流程驗證套件來源、local .NET tool、NuGet restore、.NET build／format／tests／弱點檢查，以及兩個前端的 `npm ci`、typecheck、零警告 lint、coverage、production build 與 production dependency audit。

### E. 建立資料庫

1. 執行 `initialize-development-database.ps1`。
2. 腳本連到 `.\SQL2025`，建立／更新 `DoSelectDb`。
3. 套用目前完整 EF Core Migration chain，而不是只跑 `InitialCreate`。
4. 執行最小 Seed、Schema／Seed SQL 驗證與 API database smoke test。

### F. 啟動並驗收

1. 執行 `start-all.ps1`、`status.ps1`、`health-check.ps1`。
2. 驗證 API `http://localhost:5126`。
3. 驗證 Customer Web `http://localhost:5173`。
4. 驗證 Admin Web `http://localhost:5174/admin/`。
5. Development 驗收完成後，以 `stop-all.ps1` 停止，確認三個 Port 已釋放且 tracked worktree 仍乾淨。

### G. 選用 Demo／外部 Provider

1. Demo 僅在 Development 通過後，以 `reset-demo-data.ps1` 建立新的隔離 `DoSelectDemo_<32-hex>`。
2. 需要登入時才執行 `activate-demo-accounts.ps1`；管理員第一次登入依正常流程設定 TOTP。
3. AI 與 Email 預設停用。只有在新電腦重新設定各自 Secret、確認網路／費用／寄件設定後，才使用 `start-all.ps1 -Environment Demo -EnableAi -EnableEmail` 明確啟用。

## 7. 建置完成定義

另一台電腦必須同時符合以下條件才算搬移完成：

- [ ] 工具版本與 SQL Windows Authentication 前置檢查通過。
- [ ] 解壓後的 commit SHA 與交付 manifest 相同，tracked worktree 乾淨。
- [ ] 完整 Restore、Build、Format、Tests、Lint、Coverage、Production Build 與 dependency audit 通過。
- [ ] `DoSelectDb` 完整 Migration、Seed、SQL 驗證與 API database smoke 通過。
- [ ] API、Customer Web、Admin Web 均啟動且健康檢查通過。
- [ ] Customer 與 Admin 頁面可開啟；需要的 Demo 帳號可登入並完成管理員 TOTP 首次設定。
- [ ] 停止腳本只停止本專案服務，三個 Port 可釋放。
- [ ] 未在 Repository、終端輸出、截圖或驗收文件留下 Secret／完整個資。

## 8. 搬機時容易遺漏的事項

1. **原始碼版本**：目前本機新 commits 尚未推送；必須先固定已推送且 CI 通過的交付 commit，不能以「現在資料夾看起來能跑」代替版本固定。
2. **資料與程式不同**：Migration／Seed 可重建結構與測試資料，但不會搬走目前訂單、會員、附件或操作紀錄。
3. **Secret 綁定使用者／機器**：.NET User Secrets 不隨 Repository 移動；換 Windows 帳號也視為新環境。
4. **TOTP**：不應打包 Seed。Demo 管理員在新環境第一次登入重新綁定；正式管理員帳號搬移則需另外設計安全移轉流程。
5. **Windows Authentication**：SQL 權限屬於新電腦的 Windows 身分，不能沿用舊電腦 SID／權限。
6. **Storage DataRoot**：新電腦可能沒有 `E:`；路徑必須在 Repository 外且不能直接指向磁碟根目錄。
7. **固定 Port**：`5126`、`5173`、`5174` 若被其他程式占用，啟動與完整驗證會停止。
8. **外部連線**：NuGet、npm、GitHub、OpenAI、Brevo 可能受 Proxy、防火牆或組織政策限制；AI／Email 不影響預設 Fresh Build，但套件還原需要網路。
9. **外部服務成本與資料傳輸**：OpenAI／Brevo 必須另行核准金鑰、寄件者、額度與資料邊界，不能因搬機自動啟用。
10. **Development 不等於正式部署**：SQL Server Developer 與 localhost HTTP 只用於開發／展示；若目標是正式上線，需另做 Production hosting、TLS、Secret store、備份、監控與資料保護規劃。
11. **時區與地區設定**：系統邏輯應使用既有時間標準，但驗收仍需確認新電腦 Windows 時區為 Asia/Taipei，避免人工判讀時間不同。
12. **磁碟與防毒**：SQL、NuGet/npm cache、build 與測試會需要額外空間；企業防毒可能鎖定前端 native module 或暫存檔，需保留原始錯誤再處理，不能跳過驗證。

## 9. 審核時需要確認的決策

請在建立壓縮檔前確認以下四點：

1. **套件型態**：採建議的「可連網安裝／還原」；或新電腦完全離線，需要大型離線包。
2. **資料範圍**：採建議的「全新 Development／Demo 資料庫」；或需要另行搬移一份經核准、去識別的既有資料。
3. **原始碼版本**：先完成目前變更的 review、commit、push，再以屆時 `origin/dev` 的完整 SHA 建包。
4. **目標電腦**：確認 Windows x64、可取得本機管理員權限、可安裝 `SQL2025` named instance，且沒有必須沿用的既有 SQL Instance／Port 限制。

未特別指定時，壓縮作業採每項的第一個建議值。

## 10. 若目標電腦完全離線

離線建置不是只把 `node_modules` 與 `bin` 一起壓縮。它至少需要另外準備並驗證：

- 符合目標架構的 Git、.NET SDK 10.0.303、Node 24、SQL Server 2025 Developer、ODBC 18／sqlcmd 完整離線安裝媒體。
- 依鎖定檔建立的 NuGet 與 npm 離線來源，以及 package integrity／hash 清單。
- SQL Server 安裝媒體與授權適用性。
- 在一台同樣無網路的乾淨 Windows 電腦做完整還原、建置、Migration 與啟動驗證。

此方案體積、維護與供應鏈風險都較高，且安裝媒體會過期；只有確認目標機無法連官方套件來源時才採用，並另行產出離線包規格與驗收紀錄。

## 11. 審核後才執行的作業

收到核准後才會：

1. 固定最終遠端 commit SHA。
2. 建立乾淨 shallow clone staging 目錄。
3. 補齊／校正 `README-FIRST.md`、manifest 與 Fresh Build 手冊。
4. 執行完整驗證與敏感檔案／不必要產物檢查。
5. 建立 ZIP 與 SHA-256。
6. 解壓回驗壓縮內容，確認可讀、版本正確且未包含排除項目。

在使用者核准本規劃前，不建立最終 ZIP、不複製資料庫、不輸出 Secret，也不清理目前工作目錄。
