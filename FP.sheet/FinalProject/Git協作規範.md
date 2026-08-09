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

PR 建立後不要自行合併，等待專案主導者或指定 Reviewer 處理。

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
