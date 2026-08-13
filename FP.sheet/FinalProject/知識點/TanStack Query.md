---
type: knowledge
title: TanStack Query
aliases:
  - TanStack Vue Query
  - Vue Query
tags:
  - 知識點
  - 前端
  - Vue
  - Server State
  - TanStack Query
created_at: 2026-08-09
related:
  - "[[03-架構/系統架構]]"
  - "[[知識點/PrimeVue]]"
---

# TanStack Query

## 定位

TanStack Query 是管理 **Server State** 的前端函式庫。在 Vue 專案中使用 `@tanstack/vue-query`，主要處理：

- API 查詢的載入、成功與錯誤狀態。
- 依 Query Key 建立快取。
- 資料過期、背景重新抓取與重試。
- Mutation 完成後使相關查詢失效。
- 分頁、無限列表及 optimistic update。

它不是 HTTP Client，也不是 Pinia 的替代品。`fetch` 或產生的 API Client 負責發送請求；TanStack Query 負責協調請求結果的生命週期。

## 與 Pinia 的責任分工

| 狀態 | 工具 |
|---|---|
| 商品、訂單、報表等 API 資料 | TanStack Query |
| 登入使用者、UI 偏好、尚未送出的組裝流程 | Pinia 或局部狀態 |
| 可分享的篩選、排序、頁碼 | Router query parameters |
| 表單尚未送出的欄位 | 表單／元件狀態 |

不要把 Query 結果複製到 Pinia，否則會產生兩份真相與失效同步問題。

## 基本設定

```ts
import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 2,
    },
  },
})

app.use(VueQueryPlugin, { queryClient })
```

上例數值只是示意。商品列表、庫存、訂單狀態及報表需要不同新鮮度，不應全部套同一個 `staleTime`。

## Query Key 規則

Query Key 頂層必須是陣列，且需要包含所有會改變查詢結果的變數：

```ts
const productKeys = {
  all: ['products'] as const,
  list: (filter: ProductFilter) => ['products', 'list', filter] as const,
  detail: (id: string) => ['products', 'detail', id] as const,
}
```

```ts
useQuery({
  queryKey: productKeys.list({ page, pageSize, sort, keyword }),
  queryFn: () => productApi.list({ page, pageSize, sort, keyword }),
})
```

Key 應使用可序列化且正規化的值。不要把未穩定的 class instance、函式或每次建立都不同的物件放入 key。

## Mutation 與失效

```ts
const queryClient = useQueryClient()

const updateProduct = useMutation({
  mutationFn: productApi.update,
  onSuccess: (product) => {
    queryClient.setQueryData(productKeys.detail(product.id), product)
    return queryClient.invalidateQueries({ queryKey: productKeys.all })
  },
})
```

更新成功後，可直接更新確定的 detail cache，再使列表查詢失效。不要在 API 尚未成功前永久改寫快取；若使用 optimistic update，必須保存舊值並在失敗時回滾。

## 本專案注意事項

- 401 的刷新權杖與重送集中在 API Client，避免每個 query 自行處理。
- 400/409 等業務錯誤通常不應自動重試；暫時性網路或 5xx 才考慮有限重試。
- 庫存與價格是高時效資料，結帳時後端仍須重新驗證，不能信任前端快取。
- 後台列表將頁碼、篩選、排序納入 key，並讓 API 回傳總筆數。
- 登出時清除或隔離使用者專屬 cache，避免下一位使用者看到前一位資料。
- Query Devtools 只用於開發環境。
- 集中建立 query option factory，使 key、queryFn 與型別保持一致。

## 常見陷阱

- 每次 render 建立語意不同但文字相同的 key。
- Mutation 後只更新當前畫面，忘記失效其他相關列表或報表。
- 把 `isLoading`、`isFetching` 混為一談，造成背景更新時整頁閃爍。
- 全站關閉重新抓取，導致使用者長時間看到過期資料。
- 對所有錯誤一律重試，讓驗證錯誤重送多次。

> [!warning] 專案決策邊界
> 專案已確認由 TanStack Query 管理 API Server State，Pinia 管理登入狀態、UI 偏好與跨頁客戶端流程；實際封裝與 Query Key 規範仍待前端基線完成。

## 參考資料

- [TanStack Vue Query 官方文件](https://tanstack.com/query/latest/docs/framework/vue)
- [TanStack Query：Query Keys](https://tanstack.com/query/latest/docs/framework/vue/guides/query-keys)
- [TanStack Query：Query Options](https://tanstack.com/query/latest/docs/framework/vue/guides/query-options)
- [[05-規劃/決策/00-互動中/DEC-BATCH-002-第二批核心決策]]
