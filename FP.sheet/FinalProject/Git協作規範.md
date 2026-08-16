---
文件狀態: 已確認
最後更新: 2026-08-16
追蹤項目:
  - DEV-01
  - DEV-06
---

# Git 協作規範

這份規範提供組員日常開發使用。GitHub Branch Protection、PR 審核與其他 Repository Rules 由專案主導者統一設定。

## 1. Branch 規則

### 分支用途

- `main`：穩定版本，組員不得直接 Push。
- `dev`：團隊開發整合分支，組員不得直接 Push。
- 個人工作一律從最新的 `dev` 建立新分支，完成後透過 PR 合併回 `dev`。

### 分支命名

```text
<類型>/<功能簡述>
```

| 類型 | 用途 | 範例 |
|---|---|---|
| `feature` | 新功能 | `feature/shopping-cart` |
| `fix` | Bug 修正 | `fix/cart-quantity` |
| `refactor` | 程式重構 | `refactor/order-service` |
| `docs` | 文件修改 | `docs/git-rules` |
| `chore` | 設定、套件或雜項 | `chore/update-packages` |

名稱一律使用小寫英文與連字號 `-`，不要使用空白、底線或中文。

### 建立個人分支

```bash
git switch dev
git pull --ff-only origin dev
git switch -c feature/shopping-cart
```

一個 Branch 只處理一件事，不要把無關功能或格式整理放在同一個 Branch。

## 2. 什麼時候開 PR

符合以下條件時，開 PR 合併至 `dev`：

- 功能或修正已完成，不再是做到一半的狀態。
- 已在自己電腦完成基本測試，專案可以正常啟動與建置。
- 已同步最新 `dev`，並處理完衝突。
- 已檢查變更內容，沒有 `.env`、密碼、API Key、Token、連線字串或無關檔案。

若功能尚未完成，但希望其他組員先查看或討論，可以開 **Draft PR**。

PR 標題要清楚描述內容，例如：

```text
新增購物車數量調整功能
修正庫存被重複扣除的問題
```

PR 說明至少寫明：

- 做了什麼。
- 如何測試。
- 是否影響 API、資料庫或其他組員正在開發的功能。

PR 建立後不要自行合併。只有組長可以核准並合併 PR；其他組員可以提出 Review 意見，但不能取代組長的最終核准。

### PR 自動檢查

PR 合併前至少通過：

- `Backend`：.NET Restore、零警告 Build、Format Verify、Solution Test 與 NuGet 弱點檢查。
- `Frontend (customer-web)`、`Frontend (admin-web)`：鎖檔安裝、Type Check、零警告 Lint、單元／元件測試、Production Build 與正式相依弱點檢查。
- Migration、登入授權、金額退款或庫存相關變更必須附對應測試。

Branch Protection 固定要求彙總 Check `CI Required`；只有 `Backend` 與兩個 Frontend Job 全部成功才會通過。`main`／`dev` 均使用 Strict Status Check，PR 必須先更新至最新目標分支並重新通過。只有組長核准與必要自動檢查皆通過後才能合併。合併到 `dev` 後執行五條核心 Playwright E2E；實際五條流程由 [[03-架構/測試策略]] 追蹤。

### GitHub 合併與保護設定

- `main` 與 `dev` 依本文件使用受保護分支與 PR 流程，組員不得直接 Push。
- Repository 允許的 PR 合併方式固定為 **Squash Merge**，不使用 Merge Commit 或 Rebase Merge 合併 PR。
- 組長帳號保留 Branch Protection／Repository Rules 的 Bypass 權限；其他組員仍必須遵守 PR、核准與必要檢查。
- PR 合併後由 GitHub 自動刪除來源分支，避免已完成的遠端短分支持續累積。

## 3. 用最新 `dev` 更新自己的 Branch

開發期間需要同步 `dev` 時，使用 **Rebase**，不要一直把 `dev` Merge 進自己的 Branch。

先確認自己的變更都已 Commit，再執行：

```bash
git switch feature/shopping-cart
git fetch origin
git rebase origin/dev
```

如果沒有衝突，更新遠端個人 Branch：

```bash
git push --force-with-lease
```

因為 Rebase 會改寫個人 Branch 的 Commit 歷史，所以不能使用一般的 `git push`。請使用 `--force-with-lease`，不要使用 `--force`。

### 發生衝突時

1. 打開衝突檔案並確認要保留的內容。
2. 修改完成後執行：

```bash
git add <衝突檔案>
git rebase --continue
```

3. 如果還有其他衝突，重複以上步驟。
4. Rebase 完成後執行：

```bash
git push --force-with-lease
```

如果不確定如何解衝突，先停止操作並詢問組員。可以使用以下指令取消這次 Rebase，回到開始前的狀態：

```bash
git rebase --abort
```

### 注意事項

- Rebase 前先 Commit；不要在有未提交變更時操作。
- 只 Rebase 自己的個人 Branch，不要 Rebase `main`、`dev` 或其他組員的 Branch。
- 不要反覆執行 `git merge dev`，避免產生大量無意義的 Merge Commit。
- 不要使用 `git push --force`；一律使用較安全的 `git push --force-with-lease`。

## 4. Obsidian 設定提交邊界

- `.obsidian/appearance.json` 的共用外觀設定可以提交；提交前必須確認不是個人暫時操作造成的變更。
- `.obsidian/graph.json` 保留儲存庫既有基準，但後續本機 Graph View 變更不得納入 Commit。
- 暫時性的介面縮放比例不是專案設定，出現變更時恢復成儲存庫版本，不提交。
- Commit 前應以 `git diff` 個別確認 Obsidian 設定檔，不能因為位於同一資料夾就整批加入。
