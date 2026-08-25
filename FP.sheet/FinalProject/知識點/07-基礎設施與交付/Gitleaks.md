---
type: knowledge
title: Gitleaks
aliases:
  - Secret Scanner
  - Secret Scanning
tags:
  - 知識點
  - CI
  - Security
  - Secrets
  - Gitleaks
created_at: 2026-08-25
related:
  - "[[03-架構/04-安全與檔案/安全與供應鏈強制驗收標準]]"
  - "[[03-架構/04-安全與檔案/設定與Secrets管理規範]]"
  - "[[03-架構/08-測試與驗收/測試策略]]"
  - "[[知識點/07-基礎設施與交付/CI與CD]]"
---

# Gitleaks

## 它解決什麼問題

Gitleaks 以規則與熵值偵測 Git 歷史、目錄或標準輸入中的疑似 Secret，例如 API Key、Access Token、Private Key 與含密碼設定。它是預防性閘門，不代表掃描通過後 Repository 絕對沒有 Secret，也不能取代最小權限、Rotation、User Secrets 或人工 Review。

## 本專案的使用方式

| 掃描 | 指令概念 | 目的 |
|---|---|---|
| Git 歷史 | `gitleaks git --redact <repository>` | 找出目前或過去 Commit 中的疑似 Secret |
| Build Artifact | `gitleaks dir --redact <dist>` | 找出被 Vue Build 打包進瀏覽器檔案的疑似 Secret |
| 本機待提交內容 | `gitleaks git --pre-commit --staged --redact` | 開發者在 Commit 前額外檢查 Staged Diff |

CI 使用固定的 CLI `8.30.1`，先核對 Linux x64 Release SHA-256，再掃描完整 Git 歷史與兩個 Vue `dist`。不使用 `latest`，避免上游更新在沒有 Review 時改變規則或執行行為。

## 為什麼不用 gitleaks-action

DoSelect Repository 位於 GitHub Organization。官方 `gitleaks-action` 對 Organization Repository 需要 License Key；CLI 本身可以直接由官方 Release 下載執行，因此本專案不新增 License Secret、外部帳號或 Action 授權相依。

## 發現疑似 Secret 時

1. 立即停止合併與散布，不把完整值貼到 Issue、PR、聊天或日誌。
2. 確認是否為真實可用的 Credential；若是真值，先撤銷或 Rotation。
3. 判斷值出現於工作樹、單一 Commit、已 Push 歷史、Artifact 或 Log。
4. 清除來源並重新掃描；必要時另案處理 Git 歷史，但不能只新增一個刪除檔案的 Commit。
5. 若為合成測試值，優先改成不符合真實格式的明顯占位值；只有無法調整時才建立最小忽略規則並留下原因。

## 誤報與忽略規則

- 不建立涵蓋整個 `docs`、`tests`、`dist` 或設定檔的廣泛 Allowlist。
- 不因「這只是 Demo」忽略可使用的真實 Key。
- `.gitleaksignore` 只能忽略已人工確認的特定 Fingerprint，且必須在 PR 說明原因。
- `gitleaks:allow` 必須貼近單一合成測試值，不可用來略過整個檔案。
- Scanner 規則或版本升級要重新跑完整歷史、Artifact 與合成負例。

## 限制

Gitleaks 可能漏掉未知格式、低熵值或經自訂編碼的 Secret，也可能把測試資料判為疑似 Secret。因此仍要保留：

- Review Diff 與設定範例。
- Vue 僅使用公開 `VITE_*` 值的規則。
- Log、Problem Details、Health Check 與 Artifact 的敏感資訊測試。
- Secret 曾曝光時先 Rotation，再考慮歷史清理。
