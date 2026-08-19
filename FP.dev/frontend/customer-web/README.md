# DoSelect Customer Web

DoSelect 懂選的消費者前台。正式 Route 不設共同前綴，網站首頁由 `/` 開始。

API 基底預設為 `http://localhost:5126`；需覆寫時將 `.env.example` 複製為未追蹤的 `.env.local`。共用 API、TanStack Query 與非成功狀態元件由 `@doselect/web-shared` 提供，禁止在本應用建立第二套 Problem Details 或 Correlation ID wrapper。

```powershell
npm ci
npm run dev
npm run typecheck
npm run lint -- --max-warnings 0
npm test
npm run build
```
