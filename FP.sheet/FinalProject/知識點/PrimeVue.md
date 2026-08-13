---
type: knowledge
title: PrimeVue
aliases:
  - Prime Vue
tags:
  - 知識點
  - 前端
  - Vue
  - UI
  - PrimeVue
created_at: 2026-08-09
related:
  - "[[03-架構/系統架構]]"
  - "[[04-展示/Demo流程]]"
---

# PrimeVue

## 定位

PrimeVue 是 Vue 的 UI 元件庫，提供 DataTable、表單控制項、選單、Dialog、Toast、日期選擇與其他常用介面元件。它負責「畫面元件與互動」，不負責呼叫 API、Server State 快取或商業規則。

```text
PrimeVue：表格、輸入、對話框、訊息與無障礙互動
TanStack Query：API 資料、快取、載入與重新抓取
Pinia：登入狀態、UI 偏好與跨頁客戶端流程
```

## 為何適合本專案

本電商專案的後台需要大量表格、排序、篩選、分頁、表單及對話框；前台則需要 RWD 與一致的互動元件。PrimeVue 能減少從零實作複雜控制項的成本。

## 安裝概念

實際版本應在建立前端專案後鎖定，以下為目前主要設定形態：

```ts
import { createApp } from 'vue'
import PrimeVue from 'primevue/config'
import Aura from '@primeuix/themes/aura'

const app = createApp(App)

app.use(PrimeVue, {
  theme: {
    preset: Aura,
    options: {
      darkModeSelector: '.app-dark',
      cssLayer: true,
    },
  },
})
```

元件可按需匯入，避免把不使用的功能全部包入：

```vue
<script setup lang="ts">
import Button from 'primevue/button'
</script>

<template>
  <Button label="儲存" type="submit" />
</template>
```

## 主題策略

PrimeVue 的 Styled Mode 使用 design token，分為 primitive、semantic 及 component token。專案宜從共用 preset 延伸前台與後台主題，優先調整語意 token，而不是散落覆寫元件內部 CSS。

```text
共用品牌 token
├─ 前台：商品導購與品牌風格
└─ 後台：高資訊密度與清楚狀態色
```

若需要完全自行控制內部樣式，可使用 Unstyled Mode 與 Pass Through API，但維護成本較高。第一版宜先採 Styled Mode，除非 UI 規格明確要求自建元件外觀。

## 專案使用規則

- 建立 `ui/` 包裝層承接全域預設，例如確認對話框、分頁與空狀態。
- 表格的分頁、排序與篩選條件由路由或頁面狀態管理，資料本身交給 TanStack Query。
- 後端排序欄位使用白名單映射，不能直接把 PrimeVue 欄位名拼進 SQL。
- 驗證規則以後端為準；前端驗證只改善體驗。
- 大量資料使用 server-side lazy loading，不把全部資料載入瀏覽器再分頁。
- 狀態色不能是唯一訊號，需搭配文字或圖示。
- Icon-only 按鈕提供 `aria-label`，並實測鍵盤與螢幕閱讀器行為。
- 測試以使用者可見角色、名稱及文字定位，避免依賴易變的 PrimeVue 內部 class。

## 常見陷阱

- 把 DataTable 當成資料層，導致查詢條件與 API 契約散落在元件事件中。
- 同時大量使用全域 CSS、component token 與 `!important`，造成主題難以維護。
- 升級 PrimeVue 後未檢查 theme package、圖示及元件 breaking changes。
- 認為元件庫宣告可及性就不需要頁面層測試；實際標籤、焦點順序與錯誤訊息仍由專案負責。

> [!note] 專案決策邊界
> 專案已確認前台與後台使用 PrimeVue、Styled Mode、Aura 作第一版基礎 Preset，並建立 `ui/` 包裝層。精確套件版本在實際建立前端專案時依 lock file 鎖定，屬安裝實作而非產品決策。

## 參考資料

- [PrimeVue 官方文件](https://primevue.org/)
- [PrimeVue Styled Mode](https://primevue.org/theming/styled)
- [PrimeVue Accessibility](https://primevue.org/guides/accessibility/)
- [[05-規劃/決策/02-已寫回/DEC-BATCH-002-第二批核心決策]]
