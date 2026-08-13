---
type: knowledge
title: RBAC 與 Policy 授權
aliases:
  - Role-Based Access Control
  - Policy-based authorization
  - 角色權限
tags:
  - 知識點
  - 授權
  - RBAC
  - ASP.NET Core
  - 安全
created_at: 2026-08-13
related:
  - "[[01-需求/角色與權限]]"
  - "[[03-架構/API共通規範]]"
  - "[[知識點/ASP.NET Core Identity]]"
---

# RBAC 與 Policy 授權

## 驗證不等於授權

Authentication 回答「你是誰」；Authorization 回答「你能否對這個資源做這個動作」。RBAC 先以角色提供粗粒度權限，例如客服、訂單主管、財務管理員；Policy 再組合 Claim、角色、資源所有權、用途與目前業務狀態。

```text
已登入
＋具必要角色／Claim
＋資源在允許範圍
＋目前狀態允許此動作
＝授權成功
```

## 為何不能只檢查角色

同一角色不代表可讀取所有資源。例如會員只能看自己的訂單；OrderManager 可能只看履約欄位；退款執行需要 Finance Policy；敏感收件資料還需明確用途。這類判斷適合 Resource-based／Policy-based Authorization。

ASP.NET Core Policy 由 Requirement 與 Handler 組成，可集中、重用及測試。單一 Policy 有多個 Requirement 時，通常全部都必須通過。

## 前端不是安全邊界

Vue Router Guard、隱藏按鈕及 `availableActions` 用於體驗，不構成授權。攻擊者可直接呼叫 API，因此每個 Endpoint／Application Use Case 都要在伺服器重新檢查。查詢也要在 SQL 前套用可見範圍，不能先載入全部資料再於記憶體過濾。

負面測試至少涵蓋未登入、缺角色、跨資源、狀態不合法與敏感用途不足。

> [!note] 專案決策邊界
> 專案角色提供粗粒度範圍，精確操作使用 Policy；多角色取可見範圍聯集，但寫入仍走領域專用 Policy。正式矩陣見 [[01-需求/角色與權限]]。

## 參考資料

- [Microsoft Learn：Policy-based authorization in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0)
- [Microsoft Learn：Resource-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased?view=aspnetcore-10.0)
