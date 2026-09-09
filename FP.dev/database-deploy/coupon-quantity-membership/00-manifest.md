# 本機 Demo 優惠規則升級

2026-09-10；基準 commit `aeb144b0845e8caa861e0d5cb6effec993e6efe3` 加本批未提交差異。Context：`DoSelectDbContext`；SQL Server／EF Core／dotnet-ef 10.0.10。

- 從 `20260909074216_AddBuildOwnedParts` 至 `20260909164147_AddCouponQuantityAndMembershipRules`。
- 僅限系統 CLI、`.\SQL2025` 上經核對的 `DoSelectDemo_<32 hex>`。不是正式環境部署授權。
- 風險：兩個 nullable 欄位與兩個 CHECK；不回填、不刪資料。CHECK 會取得 schema lock；先停止本機受管理服務。產生 SQL 本身沒有套用到 Demo。
- `02-forward.sql` SHA-256：`3C6D55D96F7B60495443A5E681AFF3552592E654D4D8DD59906B896DEB1DF659`。EF 原始產物在 SQL Server 同批編譯 CHECK 時找不到剛新增欄位，已人工加入一個 `GO` 分隔（維持同連線／交易），並以實際 SQL 檔重跑隔離升級測試 4／4 通過。原始雜湊 `7A6D50B23CCFBD2F11687107814F51BC53D75B33CAA88F80F21F723F873B2118` 已作廢；換行或內容改變後須重新核對。
- 已在隨機命名的可拋棄 SQL 測試庫演練舊 schema＋既有優惠→新 schema，4 項 migration 測試通過，約 8 秒；這不是目前 Demo 的停機時間估計，也不是備份還原演練。
- 使用者已核准：保留歷史、備份及驗證後更新隔離 Demo；額外 29 張 `DEMO001`～`DEMO029` 一併停用、保留歷史紀錄。

## 執行前

1. 在 `FP.dev` 用 `scripts/status.ps1` 核對環境。記下原 AI／Email 開關；不要輸出秘密。服務仍在提供使用時，不做 schema 更新。
2. 建置 `src/backend/DoSelect.Api`，確認本批測試與 review；使用既有 `scripts/stop-all.ps1` 停止受管理服務，不停止 SQL Server。
3. 點載 `scripts/common.ps1`，以 `Read-DemoDatabaseState` 讀取並以 `Assert-IsolatedDemoDatabaseName` 驗證目標。不使用預設 `DoSelectDb`、不重跑 seed/reset。
4. 先以 `sqlcmd -S '.\SQL2025' -E -C -b -d <已核對的資料庫> -i database-deploy/coupon-quantity-membership/01-preflight.sql` 唯讀確認。任何非零 exit code 停止。
5. 執行既有 `scripts/backup-demo.ps1 -DatabaseName <已核對的資料庫> -Environment Demo -DataRoot <實際檔案根目錄> -BackupRoot <本機受保護備份目錄> -Reason coupon-rules`。不得將備份放入 Git 或上傳。檢查新 manifest 的資料庫檔案雜湊、`result=success` 與檔案快照狀態；`RESTORE VERIFYONLY` 通過不等於真的還原成功。備份失敗即停止，不放寬 ACL 或略過備份。

## 套用與檢查

1. 核對 forward SQL 雜湊，以同一個明確 `-d` 目標及 `-b` 執行 `02-forward.sql`，成功後執行 `03-verify.sql`。不要套用其他 pending migration。
2. 以獨立 PowerShell 程序設定 `ASPNETCORE_ENVIRONMENT=Development`、`ConnectionStrings__DefaultConnection=New-DemoConnectionString` 的回傳值。不要輸出連線字串；外部 provider 不會在本命令啟動。
3. 在 API 目錄執行 `dotnet bin/Debug/net10.0/DoSelect.Api.dll --update-demo-coupons`。**只有使用者裁定停用舊展示券，才追加 `--disable-legacy-coupons`**。命令不啟動 HTTP、背景排程或 SMTP/OpenAI；限定本機隔離庫、v3 marker 與既有啟用管理員，沿用原管理服務的版本／交易／稽核。
4. 以管理介面或唯讀 SQL 核對 CREATOR10 停用、兩張新券的全部規則、舊訂單與使用紀錄數量未變，再用原啟動方式恢復服務（保留原 AI／Email 開關）。本命令不會重設帳號、訂單或 secrets。
5. 完整流程不是單一交易：每張券透過原服務獨立提交並留下 Audit。若中途失敗，保持服務停止，核對既有狀態；schema 已是目標版本時只跑 verify，不重跑 forward。相同新券重用，規則不同則拒絕覆蓋，已停用券不強制復活。補正後可重跑維護命令。

## 回復與限制

依 `04-recovery.sql`：優先修正後向前升級；若要回舊版，必須先用新版受權限控制的管理功能暫停 SCHOOL2026 與 MEMBER100，因舊版不認得件數或入會期限。保留新增欄位，不執行會丟失規則的 Down。必要時把備份還原至另一個隔離庫並核對備份後新增資料，不直接覆寫目前 Demo。

本次只新增兩張券，所以維護後不再是 seed 初始恰好一萬筆的快照；不可為符合 seed count 而刪除訂單／其他資料，亦不重跑帳號啟用器。

目前狀態（2026-09-10）：隔離 Demo `DoSelectDemo_e2f7abe1602840a4aa6574599f341a11` 已備份、套用並驗證。備份組 `20260909T171015Z-393652416c52482db0612e5febfc16fd` 保存在 `E:\FinalProjectBackups`，COPY_ONLY／CHECKSUM／VERIFYONLY 與檔案快照雜湊通過；真還原尚未測。

第一次原始 EF SQL 在 CHECK 編譯時失敗；核對 migration 仍為前版、新欄位數 0、優惠數 30，沒有殘留 schema 或資料變更。加入 GO 後先以實際 SQL 演練 4／4，再重試成功。CLI 以 `--disable-legacy-coupons` 成功：CREATOR10 與 29 張展示券 Disabled；SCHOOL2026、MEMBER100 Active；650 訂單、100 用券紀錄、0 歷史 OrderCoupon 快照保持不變，新增 35 筆活動稽核。

API、前後台已依原 AI／Email 啟用設定恢復且健康檢查 Ready。使用者完成 MFA 後已複核優惠規則與篩選、發票搜尋區、退款列表／明細；此庫無發票資料，發票明細實機驗收仍未測，元件測試通過。SMTP／OpenAI 未發送驗證測試。沒有 commit、push 或 CI。
