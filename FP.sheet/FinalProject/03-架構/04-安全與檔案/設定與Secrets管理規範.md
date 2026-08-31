---
文件狀態: 已確認
最後更新: 2026-08-25
追蹤項目:
  - DEV-05
  - DEV-08
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
| `OpenAI__SupportModel` |  | AI 客服模型；預設 `gpt-5.6-terra`，凍結後依評估決定 Snapshot |
| `OpenAI__SupportTimeoutMilliseconds` |  | AI 客服單次嘗試逾時；預設 12000，允許 1000～60000 |
| `OpenAI__SupportInputCostPerMillionTokens` |  | AI 啟用時必填；目前模型 Input 每百萬 Token 單價，非負數 |
| `OpenAI__SupportOutputCostPerMillionTokens` |  | AI 啟用時必填；目前模型 Output 每百萬 Token 單價，非負數 |
| `OpenAI__BudgetAlertRecipientAdminPublicId` |  | AI 啟用時必填；指定唯一組長的 `AspNetUsers.PublicId`。執行時必須對應 Active Admin、有效 AdminProfile 與 `SuperAdmin` 角色；不是 Secret |
| `OpenAI__DemoMemberPublicIds__0/1` |  | 選填、最多兩個不重複 Member PublicId；只繞過 US$90 成本停止門檻 |
| `Email__SmtpHost` |  | Email 功能啟用時必填 |
| `Email__SmtpPort` |  | Email 功能啟用時必填 |
| `Email__UserName` | ✓ | Brevo SMTP 啟用時必填 |
| `Email__Password` | ✓ | Brevo SMTP 啟用時必填 |
| `Email__SenderAddress` |  | 必須是已驗證寄件者 |
| `Storage__DataRoot` |  | Development 未覆寫時使用系統暫存目錄下的 `DoSelectData`；Demo 設為 `E:\FinalProjectData`；啟動時驗證為非磁碟根目錄的絕對路徑，檔案 Logging 啟用時另驗證可寫 |
| `Security__DataProtectionKeyPath` | ✓ | Demo 固定登入／Token 跨重啟時必填 |
| `Security__CouponGuestUsageHmacKeyV1` | ✓ | 訪客使用具每人限制的公開優惠券前必填；至少 32 bytes 隨機值，只用於 HMAC-SHA-256，不得回傳、記錄或放入 Repository |
| `Features__AiEnabled` |  | Boolean，安全預設 `false`；明確設為 `true` 時必須通過 OpenAI 設定驗證 |
| `Features__EmailEnabled` |  | Boolean，安全預設 `false`；明確設為 `true` 時必須通過 SMTP 設定驗證 |
| `Observability__FileLoggingEnabled` |  | Boolean，預設 `true`；停用時只保留 Console JSON |
| `Demo__SimulationEndpointsEnabled` |  | 只允許 Demo Environment 為 true |

Vue 只允許 `VITE_API_BASE_URL`、`VITE_APP_DISPLAY_NAME`、`VITE_DEFAULT_LOCALE` 等公開值。任何 `VITE_*` 都視為會出現在瀏覽器，不得放 API Key、SMTP、Connection String、JWT／Cookie Key。

## 團隊設定流程

1. Repository 只保存 `appsettings.Development.example.json` 與 `.env.example` 的 Key／空值；SQL 可保存 Windows Authentication 無密碼範例，不保存 SQL Login 密碼。範例檔需複製為未追蹤的 `appsettings.Development.json` 或改由 User Secrets／環境變數覆寫。
2. 每位開發者以 `dotnet user-secrets set <Key> <Value>` 設定本機 Secret，不在聊天、Issue 或 PR 貼值。
3. 組長以面對面或受控密碼管理工具提供必要 Secret；更換成員或疑似外洩立即 Rotation。
4. 啟動前 Configuration Validation 只回報缺少的 Key 名，不輸出值。
5. Log、Health Check、Problem Details、Audit 與備份 Manifest 只顯示 Provider 是否已設定，不顯示帳號或 Secret。

DoSelect.Api 已提交非敏感的 `UserSecretsId`。Brevo 展示設定由 `FP.dev/scripts/configure-brevo-secrets.ps1` 在目前 Windows 使用者的 User Secrets 中建立；SMTP Key 只能在互動式隱藏輸入提示中輸入。`test-brevo-smtp.ps1` 只用於寄送單封無個資、無 Token 的驗證信，不等同正式 `IEmailSender` 或重試流程。

訪客優惠券 HMAC V1 Secret 由每位開發者及 Demo 使用者分別透過 User Secrets／使用者層級環境變數設定。缺少或長度不足時不得接受具每人限制的訪客優惠券，也不得降級為明文 Email、一般未加密 Hash 或硬編碼預設值。V1 不輪替；未來要輪替時須先新增版本欄位與相容讀取決策。

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
- AI／Email 未啟用時，即使沒有對應 Secret，API 仍可啟動且核心電商功能保持可用；未啟用的外部服務不使公開 Health 失敗。
- AI 客服 Request 的 `store=false` 是程式固定隱私邊界，不提供環境設定覆寫；`OpenAI__ApiKey` 只能存在後端 Secret Store，不得進入 Vue 設定或 Bundle。

上述降級只適用於 `Features:AiEnabled=false` 或 `Features:EmailEnabled=false`。若操作人員明確啟用功能卻漏填必要設定，API 必須在啟動時失敗，只列出缺少或不合法的 Configuration Key，不得靜默改回 false。

## Git 與掃描

- `.gitignore` 必須涵蓋 `.env`、`*.secrets.json`、本機覆寫設定、Data Protection Keys、資料庫備份與附件目錄。
- PR／CI 使用固定版本 Gitleaks CLI `8.30.1`；完整 Git 歷史與兩個 Vue Production `dist` 都必須通過，工具下載先核對官方 SHA-256，掃描輸出固定使用 `--redact`。
- 不使用 `latest`、不建立廣泛路徑 Allowlist。合成測試值優先改成明顯無效格式；必要忽略只能限制於特定 Fingerprint／單一值並在 PR 留下理由。
- Gitleaks 通過不代表絕對沒有 Secret；人工 Diff、設定範例、Log、Health、Problem Details 與 Artifact 邊界仍須 Review。
- Secret 一旦進入 Git，即使刪除檔案也視為外洩：先撤銷／Rotation，再評估清理歷史，不只做新 Commit 刪除。

## 驗收

- Fresh Clone 以 AI／Email 預設停用，可在沒有 Secret 時啟動核心 API；明確啟用任一功能但缺少設定時，以安全且可理解的 Key 名啟動失敗。
- 前端建置產物搜尋不到 OpenAI、SMTP、Connection String 或 Data Protection Key。
- Repository、Log、Health、Audit、備份、錯誤頁與 Demo 影片均不含 Secret。
- `Demo__SimulationEndpointsEnabled=true` 在非 Demo Environment 時啟動失敗。
