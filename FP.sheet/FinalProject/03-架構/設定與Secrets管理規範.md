---
文件狀態: 已確認
最後更新: 2026-08-14
追蹤項目:
  - DEV-05
---

# 設定與 Secrets 管理規範

## 分類

| 類型 | 例子 | 儲存方式 |
|---|---|---|
| 公開設定 | API Base URL、語系、Feature Flag 顯示值 | `appsettings*.json` 或 Vue `VITE_*`；可進 Git |
| 環境設定 | SQL Instance、資料根目錄、Log Level | 未含憑證者可放未追蹤的本機設定；提供 `.example` |
| Secret | 含密碼的 Connection String、OpenAI API Key、Brevo SMTP Key、Cookie／Data Protection Key | .NET User Secrets（開發）或系統環境變數（Demo）；不得進 Git／Vue Bundle |
| 一次性 Token | Email 驗證、密碼重設、Guest Order OTP | 由後端短期產生／驗證；不寫設定檔或 Log |

## 設定來源與優先序

ASP.NET Core 採：

```text
appsettings.json
→ appsettings.{Environment}.json
→ .NET User Secrets（Development）
→ 系統環境變數
→ 命令列（只供非敏感臨時覆寫）
```

後順位覆蓋前順位。正式 Demo 使用 `Demo` Environment 與展示電腦「使用者層級」環境變數；不把 Secret 寫入 `start-all.ps1`、捷徑、簡報或 Repository。

## 固定 Key

| Key | Secret | 必填條件 |
|---|:---:|---|
| `ConnectionStrings__DefaultConnection` | 條件 | API 啟動必填；目前 Windows Authentication 範例不含 Secret，若改用 SQL Login 則整值為 Secret |
| `OpenAI__ApiKey` | ✓ | AI 功能啟用時必填 |
| `OpenAI__Model` |  | 有安全預設，可由設定覆寫 |
| `Email__SmtpHost` |  | Email 功能啟用時必填 |
| `Email__SmtpPort` |  | Email 功能啟用時必填 |
| `Email__UserName` | ✓ | Brevo SMTP 啟用時必填 |
| `Email__Password` | ✓ | Brevo SMTP 啟用時必填 |
| `Email__SenderAddress` |  | 必須是已驗證寄件者 |
| `Storage__DataRoot` |  | Demo 預設 `E:\FinalProjectData`，啟動時驗證絕對路徑 |
| `Security__DataProtectionKeyPath` | ✓ | Demo 固定登入／Token 跨重啟時必填 |
| `Features__AiEnabled` |  | Boolean；缺 OpenAI Key 時強制降級 false |
| `Features__EmailEnabled` |  | Boolean；缺 SMTP Secret 時強制降級 false |
| `Demo__SimulationEndpointsEnabled` |  | 只允許 Demo Environment 為 true |

Vue 只允許 `VITE_API_BASE_URL`、`VITE_APP_DISPLAY_NAME`、`VITE_DEFAULT_LOCALE` 等公開值。任何 `VITE_*` 都視為會出現在瀏覽器，不得放 API Key、SMTP、Connection String、JWT／Cookie Key。

## 團隊設定流程

1. Repository 只保存 `appsettings.Development.example.json` 與 `.env.example` 的 Key／假值；SQL 可保存 Windows Authentication 無密碼範例，不保存 SQL Login 密碼。
2. 每位開發者以 `dotnet user-secrets set <Key> <Value>` 設定本機 Secret，不在聊天、Issue 或 PR 貼值。
3. 組長以面對面或受控密碼管理工具提供必要 Secret；更換成員或疑似外洩立即 Rotation。
4. 啟動前 Configuration Validation 只回報缺少的 Key 名，不輸出值。
5. Log、Health Check、Problem Details、Audit 與備份 Manifest 只顯示 Provider 是否已設定，不顯示帳號或 Secret。

## SQL Server 連線設定

本機與 Demo 採 Windows Authentication：

```text
ConnectionStrings:DefaultConnection
Server=.\SQL2025;Database=DoSelectDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;
```

- `ConnectionStrings:DefaultConnection` 是 .NET Configuration Key；環境變數使用 `ConnectionStrings__DefaultConnection`。
- 上述範例沒有密碼，可以作為文件與 `.example` 值；實際執行者仍只取得 Windows 身分已獲授權的資料庫權限。
- User Secrets 可用 `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<local value>"` 覆寫本機 Instance；文件不得填入任何成員的 Windows Credential。
- 若未來改成 SQL Login、雲端資料庫或非 Windows Host，必須重新審查 TLS、Credential Rotation、最小權限與 CI Secret；不得把 `User ID`／`Password` 直接加進已追蹤設定檔。

## Demo 電腦

- 由展示用 Windows 使用者設定 User-level 環境變數；避免 Machine-level 影響其他帳號。
- `Demo` 設定檔可保存服務開關與路徑，但 Secret 仍由環境變數提供。
- Demo 前檢查只測試 SQL、OpenAI、Brevo 可用性；不可把 Secret 截圖或錄入備援影片。
- 預錄完成或專題結束後 Rotation OpenAI／SMTP Key，清除展示使用者環境變數與 User Secrets。
- AI／Email Secret 缺失時 API 仍可啟動，Health 顯示 Degraded，電商核心功能保持可用。

## Git 與掃描

- `.gitignore` 必須涵蓋 `.env`、`*.secrets.json`、本機覆寫設定、Data Protection Keys、資料庫備份與附件目錄。
- PR／CI 掃描常見 API Key、Connection String 密碼、Private Key Header；發現疑似 Secret 時阻擋合併。
- Secret 一旦進入 Git，即使刪除檔案也視為外洩：先撤銷／Rotation，再評估清理歷史，不只做新 Commit 刪除。

## 驗收

- Fresh Clone 缺 Secret 時給安全且可理解的 Key 名錯誤，核心非 AI／Email 功能仍能依設定啟動。
- 前端建置產物搜尋不到 OpenAI、SMTP、Connection String 或 Data Protection Key。
- Repository、Log、Health、Audit、備份、錯誤頁與 Demo 影片均不含 Secret。
- `Demo__SimulationEndpointsEnabled=true` 在非 Demo Environment 時啟動失敗。
