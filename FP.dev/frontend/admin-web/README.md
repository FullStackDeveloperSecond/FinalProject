# DoSelect Admin Web

DoSelect 懂選的管理後台。Vite 與 Vue Router 以 `/admin/` 作為部署基底；實際授權仍由後端 Policy 執行。

API 基底預設為 `http://localhost:5126`；需覆寫時將 `.env.example` 複製為未追蹤的 `.env.local`。共用 API、TanStack Query 與非成功狀態元件由 `@doselect/web-shared` 提供；目前的側邊欄只建立殼層，登入、2FA、Policy Guard 與模組選單須由正式 Session 契約驅動。

```powershell
npm ci
npm run dev
npm run typecheck
npm run lint -- --max-warnings 0
npm test
npm run build
```
