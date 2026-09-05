---
title: 品牌與 UI 視覺規範
status: 已確認
last_updated: 2026-09-05
---

# DoSelect 懂選｜品牌與 UI 視覺規範

## 品牌基線

- 正式名稱：`DoSelect 懂選`。
- 標語：「說出需求，組出適合你的電腦。」
- Logo：圓角藍色選擇框、白色 `D` 與淺藍勾選符號；前後台共用同一圖形與中英文標準字。
- 前台顯示 `DoSelect 懂選`；後台附加「管理後台」，不得另造第二套品牌。
- 展示資料仍須保留既定 Demo／無背書聲明。

## 色票

| 用途 | Token | 色值 |
|---|---|---|
| 主色 | `--color-primary` | `#2F7DD3` |
| 主色深 | `--color-primary-dark` | `#1F63AC` |
| 主色淡底 | `--color-primary-soft` | `#EAF3FC` |
| 主要文字 | `--color-text` | `#153452` |
| 次要文字 | `--color-text-muted` | `#4C6B85` |
| 表面底色 | `--color-surface` | `#F4F8FC` |
| 邊框 | `--color-border` | `#C9DCEC` |
| 成功 | `--color-success` | `#1F8A5C` |
| 警告 | `--color-warning` | `#B8790F` |
| 危險 | `--color-danger` | `#C23A3A` |

顏色不得作為唯一狀態提示；錯誤、成功、逾時與禁用狀態仍須有文字、圖示或語意屬性。

## PrimeVue 與共用元件

- Customer Web 與 Admin Web 精確使用 `primevue@4.5.5`。
- Aura Styled Mode 使用 MIT 的 `@primeuix/themes@2.0.3`；這是已棄用 `@primevue/themes` 的正式後繼套件，與 PrimeVue 4.5.5 共用 `@primeuix/styled` 0.7 系列。
- 兩個前端共用 `@doselect/web-shared/brand.css` 與 `DoSelectAura`，不得各自重定義另一套主色。
- 頁面不得直接大量散佈 PrimeVue import；新增通用控制項先由 `@doselect/web-shared/ui` 包裝，再由功能頁使用。
- 不得安裝、解析、隱藏或繞過 `@primeui/license-manager`，也不得提交授權金鑰。

## Logo 使用

- 使用 `@doselect/web-shared/ui` 匯出的 `DoSelectBrand`，不要複製 SVG。
- Logo 周圍至少保留圖形高度四分之一的空白。
- 不拉伸、不旋轉、不更換圖形內部顏色，也不放在對比不足的背景。
- 圖形已有可存取名稱；若外層連結另有完整 `aria-label`，需避免產生重複或衝突名稱。

## 驗收

兩個前端須通過 typecheck、零警告 lint、完整單元測試、production build 與 `npm audit --omit=dev`；lockfile 不得再解析 PrimeVue 5 或 `@primeui/license-manager`。
