---
type: knowledge-index
title: 系統知識點
tags:
  - 知識點
最後更新: 2026-08-20
---

# 系統知識點

本區整理專案中反覆出現的專業術語與背景知識。知識頁只協助理解概念，不直接建立專案需求；實際行為仍以需求、架構與正式決策為準。

## 01｜電商與組裝

- [[知識點/01-電商與組裝/SLA]]
- [[知識點/01-電商與組裝/SKU]]
- [[知識點/01-電商與組裝/Socket與BIOS]]
- [[知識點/01-電商與組裝/PSU瓦數級距]]

## 02｜身分授權與 Web 安全

- [[知識點/02-身分授權與Web安全/ASP.NET Core Identity]]
- [[知識點/02-身分授權與Web安全/Credentials]]
- [[知識點/02-身分授權與Web安全/CORS]]
- [[知識點/02-身分授權與Web安全/CSRF]]
- [[知識點/02-身分授權與Web安全/Antiforgery]]
- [[知識點/02-身分授權與Web安全/TOTP]]
- [[知識點/02-身分授權與Web安全/RBAC與Policy授權]]
- [[知識點/02-身分授權與Web安全/Secrets管理]]
- [[知識點/02-身分授權與Web安全/Microsoft Defender掃描規則]]

## 03｜API 契約與可觀測性

- [[知識點/03-API契約與可觀測性/DTO與API Schema]]
- [[知識點/03-API契約與可觀測性/OpenAPI與Typed Client]]
- [[知識點/03-API契約與可觀測性/Problem Details]]
- [[知識點/03-API契約與可觀測性/Correlation ID與Trace ID]]
- [[知識點/03-API契約與可觀測性/Health Check]]
- [[知識點/03-API契約與可觀測性/Cursor分頁]]

## 04｜資料模型與一致性

- [[知識點/04-資料模型與一致性/資料庫正規化與反正規化]]
- [[知識點/04-資料模型與一致性/交易快照]]
- [[知識點/04-資料模型與一致性/PublicId與UUID v7]]
- [[知識點/04-資料模型與一致性/SQL Server rowversion]]
- [[知識點/04-資料模型與一致性/冪等性]]
- [[知識點/04-資料模型與一致性/Transactional Outbox]]
- [[知識點/04-資料模型與一致性/Audit Log]]
- [[知識點/04-資料模型與一致性/軟刪除與Filtered Unique Index]]

## 05｜報表與統計

- [[知識點/05-報表與統計/30天線性迴歸]]
- [[知識點/05-報表與統計/Z-Score]]

## 06｜前端

- [[知識點/06-前端/PrimeVue]]
- [[知識點/06-前端/TanStack Query]]

## 07｜基礎設施與交付

- [[知識點/07-基礎設施與交付/Brevo SMTP]]
- [[知識點/07-基礎設施與交付/Hangfire]]
- [[知識點/07-基礎設施與交付/CI與CD]]
- [[知識點/07-基礎設施與交付/Gitleaks]]

## 08｜AI 與智慧搜尋

- [[知識點/08-AI與智慧搜尋/Structured Outputs與JSON Schema]]
- [[知識點/08-AI與智慧搜尋/Embeddings與向量檢索]]

## 建議閱讀路徑

- Cookie Web 安全：Identity → Credentials → CORS → CSRF → Antiforgery。
- Typed API：DTO／Schema → OpenAPI Typed Client → Problem Details → Correlation ID。
- 可靠交易：rowversion → 冪等性 → Outbox → Hangfire → Audit Log。
- AI 搜尋：Structured Outputs → Embeddings；第一版採結構化輸出與 SQL，不採向量檢索。

## 收錄原則

只有跨模組反覆使用、名稱不足以自我解釋且容易誤用的概念才建立知識頁。商業規則、單一設定值、尚未核准方案及決策全文不在此重複維護。
