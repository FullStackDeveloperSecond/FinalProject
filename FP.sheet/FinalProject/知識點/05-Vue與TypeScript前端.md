# Vue 與 TypeScript 前端

## 技術邊界

DoSelect 有 customer/admin 兩個 Vue SPA，共用 `@doselect/web-shared`。兩邊 `main.ts` 都註冊 Pinia、Vue Router、TanStack Vue Query、PrimeVue；共用 package 管 API client、query defaults、theme、design tokens、UI wrapper。

### Pinia 與 TanStack Query

- Pinia：client-owned state，例如跨元件 UI 選擇與本機流程狀態。
- TanStack Query：server state，例如商品、訂單、庫存；管理 cache、stale、refetch、retry、invalidation。
- Local state：單一元件輸入或展開狀態，不必全域化。

把 API 回應全複製進 Pinia，容易出現雙 cache、失效規則分散與 loading/error 重造。

### Query 設計

共用 QueryClient 設 query `staleTime` 30 秒、關閉 focus refetch，只對 network/5xx 最多重試一次；mutation 不重試。讀取通常可安全重試，寫入沒有冪等保證時可能重複副作用。

Query key 要含所有影響結果的輸入。Mutation 後精準 `setQueryData` 或 invalidate 受影響 key，不要每次清空全部 cache。

### OpenAPI typed client

ASP.NET Core OpenAPI → `openapi-typescript` 產生 `schema.d.ts` → feature API 用 `openapi-fetch`。產生檔不可手改；wrapper 統一：

- `credentials: 'include'`
- unsafe method 的 Antiforgery token 與 client 身分
- `X-Correlation-ID`
- Problem Details → `ApiError`
- network error 與 `Retry-After`

這降低 path、request/response type 與錯誤處理漂移；但 TypeScript 不驗證執行期惡意資料，server 仍要驗證與授權。

### PrimeVue 與 wrapper

PrimeVue 提供元件與 Aura theme；共用 wrapper/design tokens 控制兩 SPA 的樣式與用法，讓升級集中並減少第三方耦合。代價是 wrapper 也要維護，不能無理由把所有元件再包一層。

## Vue 必會題

### `ref`、`reactive`、`computed`、`watch`？

`ref` 管單值或需保留身分的狀態；`reactive` 是物件 proxy，解構要小心失去響應；`computed` 是可快取、盡量無副作用的推導值；`watch` 執行副作用，要處理 cleanup 與競態。

### `v-if` 與 `v-show`？

`v-if` 建立／銷毀子樹，切換成本高但可不初始渲染；`v-show` 保留 DOM 只切 display，適合頻繁切換。

### 為何 `key` 重要？

讓 virtual DOM 辨識身分。以 index 當 key，在插入、刪除、排序時可能重用錯誤元件狀態，應使用穩定唯一 ID。

### Router guard 是安全控制嗎？

不是，只改善 UX。真正授權在 API，使用者能直接呼叫 endpoint 或改前端。

### XSS 如何發生？

一般插值會編碼，但 `v-html`、不安全 URL、第三方 script、DOM API 仍可引入。HttpOnly 只限制讀 Cookie，惡意 script 仍可能代送 request。

## 情境題

「快速切換搜尋，舊慢 response 覆蓋新結果」：將條件放 query key，讓結果各自歸檔；必要時取消舊 request 或只顯示當前 key。說明 loading/previous data UX，以可控延遲測試 race。

## 專案證據入口

- `FP.dev/frontend/customer-web/src/main.ts`
- `FP.dev/frontend/admin-web/src/main.ts`
- `FP.dev/frontend/shared/src/api/client.ts`
- `FP.dev/frontend/shared/src/query/query-client.ts`
- `FP.dev/frontend/customer-web/src/features/cart/useCart.ts`
