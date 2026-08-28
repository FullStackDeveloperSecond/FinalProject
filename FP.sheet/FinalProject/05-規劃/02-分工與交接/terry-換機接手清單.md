---
文件類型: 交接指引
建立時間: 2026-08-28 17:40
作者: terry／Claude
負責模組: terry 的工程包（M-05／M-06／M-10／M-11 等）
狀態: 已確認
適用情境: 換一台電腦繼續開發，或重灌／重新 clone 後回復工作環境
---

# terry 換機接手清單

換到另一台電腦繼續開發時，東西分成三類：**跟著 git 走的**、**要手動帶的**、**必須在新機器重建的**。
只做 `git clone` 是不夠的。

---

## 一、跟著 git 走（不用特別處理）

- 所有已推送分支的程式碼與 commit
- `FP.sheet/` 底下全部文件，含決策紀錄與 `日誌/YYYY/MM/`
- PR 的說明與留言（在 GitHub 上）

**接手時最先看**：`日誌/` 最新一篇。日誌會寫清楚當時每個 PR 的狀態與待處理事項，
是實際的交接文件。

---

## 二、要手動帶（git 看不到，換機一定遺失）

### Claude Code 的記憶檔

路徑：`C:\Users\<你>\.claude\projects\<依專案路徑產生的資料夾>\memory\`

裡面存的是跨對話的工作習慣與專案背景，例如：

- 日誌撰寫規則（只在被要求時寫、作者標記格式）
- 不自動開 PR、PR 依序處理（前一個 merge 才動下一個）
- **送 PR 或回應 review 前，必須先對照專案文件做整體自我審查**（不是只改留言指到的行）
- 各切片的分支計畫與狀態
- 本機環境陷阱（見下方第三節）

**帶法**：先在新機器對這個專案開一次 Claude Code，讓它建出正確的資料夾（資料夾名稱是依專案
絕對路徑產生的，路徑不同名稱就不同），再把舊機器 `memory\` 裡的 `.md` 檔全部複製進去。

### 本機 stash

`git stash list` 裡的東西不會跟著 remote 走。換機前先確認是否還需要，需要就轉成 commit 或 patch。

---

## 三、新機器必須重建的環境

以下四項都是這個專案已知會擋住新環境的地方。

### 1. .NET SDK 版本

`FP.dev/global.json` 釘死 `10.0.303` 且 `rollForward: disable`。新機器若裝的不是這個版本，
所有 `dotnet build`／`test` 都會失敗。

- **正解**：安裝 10.0.303。
- **暫時繞過**：把 `global.json` 改成本機實際版本 —— 但**提交前務必還原**：

  ```bash
  git checkout -- FP.dev/global.json
  ```

  還原後用 `git status` 確認它不在變更清單裡再 commit。這是每次都要做的固定動作。

### 2. 後端啟動用的環境變數

`GuestOrderAccess__Pepper`，至少 32 bytes，否則後端啟動即失敗。
本機開發隨便給一個夠長的字串即可；**不要提交進 repo**。

### 3. 本機資料庫

本機 `DoSelectDev` 需要跑完全部 migration。曾經因為落後 14 個 migration，導致 32 個
Support 模組測試失敗而被誤判成程式碼缺陷。**新機器第一件事就是更新資料庫**：

```bash
dotnet ef database update --project FP.dev/src/backend/DoSelect.Infrastructure --startup-project FP.dev/src/backend/DoSelect.Api
```

### 4. 前端相依

三個套件都要裝，且要**先裝 shared**（另外兩個以 `file:../shared` 相依）：

```bash
npm install --prefix FP.dev/frontend/shared && npm install --prefix FP.dev/frontend/customer-web && npm install --prefix FP.dev/frontend/admin-web
```

---

## 四、分支狀態：以 origin 為準

**重要**：這個專案會有多個工作階段／多台機器同時動同一支分支。實際發生過的情況是
**本地分支看起來「ahead」，但其實是舊副本** —— 另一個工作階段已經把同樣的工作 rebase 到更新的
`dev` 並推上去，還順手把多個 commit 壓成較少的 commit。這時 `git status` 會顯示
「ahead N, behind M」，很容易誤判成「我有未推送的工作」。

**判斷方法（不要只看 ahead／behind 數字）**：

```bash
git fetch origin
git log --format='%s' origin/<branch>..<branch>     # 本地獨有 commit 的主旨
git diff --name-only origin/<branch> <branch> -- <那些 commit 動過的檔案>
```

如果檔案內容相同，代表工作已經在 origin 上（只是 commit 形狀不同），**不要推**。
`git cherry` 在 rebase 過的分支上會因 patch-id 改變而誤報，不可單獨作為判準。

2026-08-28 實測結論：`feature/shipping-core-api`、`feature/shipping-admin-api`、
`feature/inventory-reservation-frontend`、`feature/build-compat-api` 四支本地都「ahead」，
但檔案內容全部已在 origin，且 `feature/build-compat-api` 本地還停在
DEC-BATCH-027 重構**之前**的狀態（含已刻意刪除的 SKU-attributes API）。
**推上去會造成倒退**。換機時直接以 origin 為準重新 checkout 即可。

---

## 五、驗證環境有裝好

依序跑完都通過，才算環境正常：

```bash
dotnet build FP.dev/DoSelect.slnx
```

```bash
dotnet test FP.dev/DoSelect.slnx
```

```bash
npm run test --prefix FP.dev/frontend/customer-web && npm run test --prefix FP.dev/frontend/admin-web
```

- 後端全量測試需要本機 SQL Server；沒有 SQL Server 時用
  `dotnet test FP.dev/DoSelect.slnx --filter "Category!=RequiresSqlServer"`（等同 CI 的範圍）。
- 前端另有 `npm run lint`、`npm run typecheck`、`npm run build`，送 PR 前都要乾淨。
- 若要重新產生 OpenAPI 型別（改過 API contract 時）：先啟動後端，再於
  `FP.dev/frontend/shared` 執行 `npm run api:export && npm run api:generate`。

---

## 六、請 Claude 接手的開場白

```
繼續 FinalProject 的工作。
先讀 FP.sheet/FinalProject/日誌/ 最新一篇了解目前進度，
以及 FP.sheet/FinalProject/05-規劃/02-分工與交接/terry-換機接手清單.md 確認環境設定。
```

若記憶檔沒帶過來，再補一句提醒它幾個固定規則：日誌只在被要求時寫、不要自動開 PR、
PR 依序處理、以及**送 PR 或回應 review 前要先對照專案文件做整體自我審查**。

---

## 相關連結

- [[05-規劃/02-分工與交接/開發分工與交接]]
- [[05-規劃/03-需求與決策治理/決策/決策分級與自動定案原則]]
- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
