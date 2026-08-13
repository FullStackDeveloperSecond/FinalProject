---
type: knowledge
title: Secrets 管理
aliases:
  - Secret Management
  - User Secrets
  - API Key管理
tags:
  - 知識點
  - 安全
  - 設定
  - Secrets
  - ASP.NET Core
created_at: 2026-08-13
related:
  - "[[03-架構/設定與Secrets管理規範]]"
  - "[[03-架構/威脅模型與安全檢查表]]"
  - "[[知識點/Brevo SMTP]]"
---

# Secrets 管理

## 什麼是 Secret

Secret 是一旦洩漏便可取得系統或外部服務權限的值，例如資料庫密碼、OpenAI API Key、SMTP Key、Cookie／Data Protection Key。API Base URL、Feature Flag 顯示值等通常只是公開設定，不應混為一談。

## 不同環境的保存方式

- Repository：只保存 Key 名、假值及 `.example` 檔。
- 本機開發：使用 .NET User Secrets 或受控環境變數。
- Demo／部署：由使用者層級環境變數或正式 Secret Store 注入。
- Vue：任何 `VITE_*` 都會進入瀏覽器 Bundle，只能放公開值。

User Secrets 的目的主要是讓值離開專案樹與 Git；它不會加密成正式可信 Vault，因此不適合直接作為正式環境 Secret Store。

## 生命週期

Secret 管理包含發放、最小權限、Rotation、撤銷與外洩應變。若 Secret 曾進入 Git，即使後來刪除檔案，也應先視為外洩並撤銷／Rotation，不能只靠新 Commit。

Log、Health Check、Problem Details、Audit、備份 Manifest、啟動腳本及 Demo 影片都不得輸出值。設定驗證只回報缺少的 Key 名。

## 降級

外部整合缺少 Secret 時，應依功能重要性安全失敗。資料庫連線屬核心依賴，可阻止 API Ready；AI 或 Email 可關閉功能並顯示 Degraded，核心電商仍能運作。

> [!note] 專案決策邊界
> 開發使用 .NET User Secrets，Demo 使用展示帳號的使用者層級環境變數；Vue 不保存任何 Secret。固定 Key、優先序與 Rotation 流程見 [[03-架構/設定與Secrets管理規範]]。

## 參考資料

- [Microsoft Learn：Safe storage of app secrets in development](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
- [Microsoft Learn：Azure Key Vault configuration provider](https://learn.microsoft.com/en-us/aspnet/core/security/key-vault-configuration?view=aspnetcore-10.0)
