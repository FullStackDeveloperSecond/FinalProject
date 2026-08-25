---
type: decision-record
batch_id: AUTO-DEC-013
title: Gitleaks CLI 自動 Secret 掃描定版
status: applied
created_at: 2026-08-25
applied_at: 2026-08-25
source: alex 確認採用 Gitleaks CLI 方案
---

# AUTO-DEC-013｜Gitleaks CLI 自動 Secret 掃描定版

## 正式決策

1. QA-08 的自動 Secret Scanner 採 Gitleaks CLI `8.30.1`，不採 `gitleaks-action`。
2. GitHub Actions 從官方 GitHub Release 下載 Linux x64 壓縮檔，並以官方 SHA-256 `551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb` 驗證後才執行。
3. 獨立 `Secret Scan` Job 使用完整 Checkout History，掃描完整 Git 歷史；兩個 Vue Matrix Job 在 Production Build 後另外掃描各自的 `dist`。
4. 所有掃描使用 `--redact`，不得把偵測到的疑似 Secret 值輸出至 CI Log。
5. `Secret Scan` 納入既有 `CI Required`；Repository 歷史或任一 Vue Build Artifact 偵測到疑似 Secret 時阻擋合併。
6. 第一版不建立 Baseline、全域路徑 Allowlist 或 `.gitleaksignore`。誤報必須以最小、可說明的規則處理；真實 Secret 不得加入忽略清單。
7. 本決策只完成 QA-08 的 Secret Scanner 子項；Actor A／B 授權矩陣、刪除無副作用與新增 Package 來源證據仍由 QA-08 繼續追蹤。

## 最低成本與商業影響

- 維持人工 Diff／Repository／Artifact 搜尋無法形成自動合併阻擋，不滿足 QA-08。
- GitHub 內建掃描受帳號方案與 Repository 設定影響，無法保證本機重現。
- `gitleaks-action` 對 GitHub Organization Repository 要求額外 License Key；直接使用開源 CLI 可避免外部帳號、License Secret 與續期依賴。
- 受影響者為所有提交 PR 或 Push 至 `main`／`dev` 的開發者。預期結果是疑似 API Key、Token、密碼與私鑰在合併前被阻擋。
- 每個 CI Run 增加固定版本工具下載與掃描時間；停止條件為官方 Release 無法穩定取得、校驗失敗或誤報無法以最小規則控制。回復方式是先移除 `CI Required` 對 Secret Job 的依賴，再回退 Workflow。

## 驗收證據

- Windows 本機從官方 Release 下載 `8.30.1` 並通過官方 Checksums 驗證。
- 首次完整 Repository 掃描涵蓋 175 個 Commit、約 10.71 MB，結果為 0 個 Secret。
- 合成 Secret 負例必須使 Scanner 回傳非零 Exit Code，且輸出不顯示完整 Secret。
- 兩個 Vue Production Build 的 `dist` 必須各自掃描通過。
- GitHub Actions 實際 Run 的 `Secret Scan`、兩個 Frontend Matrix 與 `CI Required` 必須通過後，才能把自動化證據視為遠端成立。

## 參考

- [Gitleaks CLI Repository](https://github.com/gitleaks/gitleaks)
- [Gitleaks v8.30.1 Release](https://github.com/gitleaks/gitleaks/releases/tag/v8.30.1)
- [gitleaks-action Organization License](https://github.com/gitleaks/gitleaks-action/blob/master/LICENSE.txt)
