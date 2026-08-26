---
文件狀態: 持續更新
最後更新: 2026-08-26
基準分支: dev@660b724
---

# M 功能實作矩陣

本表只記錄已在儲存庫找到的實作與測試證據，不以規格完成、一般 CI 綠燈或開放 PR 代替功能完成。

## 圖例

- `✅`：該層的主要範圍已合併至 `dev`，且有對應測試或可執行證據。
- `🔵`：只有部分範圍已合併至 `dev`。
- `🟡`：已有開放 PR，但尚未進入 `dev`。
- `⬜`：尚未找到該層的實作證據。
- `➖`：該功能不直接適用此欄；仍須由其他測試層證明。

## 現況

| 功能 | Domain／Application | API | 前端 | Provider-backed test | E2E | 已進 `dev` | 目前證據／下一個 Gate |
|---|---:|---:|---:|---:|---:|---:|---|
| M-01 會員註冊與登入 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | 註冊、驗證、登入、重設密碼已合併；尚無瀏覽器 E2E。 |
| M-01B 管理員 TOTP／Session | 🔵 | 🔵 | 🔵 | 🔵 | ⬜ | 🔵 | 共用 Admin Cookie／MFA Policy 已在 `dev`；完整 TOTP／Recovery／撤銷仍在 PR #38。 |
| M-02 訪客結帳與訂單驗證 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 資料模型已在 `dev`；訪客查單 PR #40、取消／退貨入口 PR #43 尚未進 `dev`，Checkout 建單仍缺。 |
| M-03 商品、SKU 與目錄 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #24 已交付前後台型錄垂直切片；尚無瀏覽器 E2E。 |
| M-04 商品批次與 Excel | 🔵 | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | Schema／規格已存在，匯入 Staging、API、UI 與原子提交證據未完成。 |
| M-05 商品搜尋與篩選 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | 公開查詢與前台型錄已合併；尚無瀏覽器 E2E。 |
| M-06 購物車 | ✅ | ✅ | 🟡 | ✅ | ⬜ | 🔵 | PR #28 後端與 SQL 測試已合併；前端 PR #29 尚未進 `dev`。 |
| M-07 優惠券 | ✅ | ⬜ | ⬜ | 🟡 | ⬜ | 🔵 | PR #7 計算／分攤／生命週期已合併；`CouponRuleReader`／試算服務註冊與 SQL 證據仍在 PR #8，尚無正式套券 Endpoint。 |
| M-08 訂單 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 訂單模型已在 `dev`；後台管理 PR #47 尚未合併，Checkout 建單垂直切片未完成。 |
| M-09 模擬付款 | ✅ | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | Payment 模型已在 `dev`；PR #9 目前只有 Application／Domain 測試且仍為 Draft，正式 API 與 SQL 證據尚未形成。 |
| M-10 庫存保留與逾時取消 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | Reservation 模型已在 `dev`；API PR #36 與堆疊前端 PR #37 尚未合併。 |
| M-11 物流與批次出貨 | ✅ | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | Shipping／Shipment 模型已在 `dev`，Application／API／UI 垂直切片未完成。 |
| M-12 單項退貨 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 完整退貨交付集中於 PR #42，尚未進 `dev`。 |
| M-13 部分退款 | ✅ | 🟡 | ⬜ | 🟡 | ⬜ | 🔵 | DES-21 快照與中央 Audit 已合併；退款執行 PR #16 仍為 Draft。 |
| M-14 客服案件與 SLA | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #10 已交付前後台、SLA 與 SQL 證據；DES-23 的完整 Action／衝突刷新範圍仍未關閉。 |
| M-15 營運報表 | 🔵 | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | 需求與資料來源已定，七個報表 Query／API／UI／SQL 證據未完成。 |
| M-16 自由組裝電腦 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 模型已在 `dev`；API PR #34 與堆疊前端 PR #35 尚未合併。 |
| M-17 零件相容性 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 與 M-16 共用 PR #34／#35；尚未進 `dev`。 |
| M-18 AI 商品搜尋推薦 | 🔵 | ⬜ | ⬜ | ➖ | ⬜ | 🔵 | 安全與降級 Application 基礎已在 `dev`；搜尋 Endpoint、OpenAI Live Adapter、UI 與 Live 評估未完成。 |
| M-19 AI 客服 | ✅ | ✅ | ⬜ | 🔵 | ⬜ | 🔵 | Fake Client API 測試已在 `dev`；Owner Query、持久化同意／額度、OpenAI Live Adapter 與前端 E2E 未完成。 |
| M-20 模擬發票與折讓 | ✅ | 🔵 | ⬜ | 🔵 | ⬜ | 🔵 | 折讓 API／Writer／SQL 測試已合併；完整發票查詢、開立、作廢、前端與跨 Checkout 金額一致性未完成。 |

## 完成判定邊界

1. `已進 dev` 只表示該功能至少有部分程式在 `dev`；只有 `✅` 才表示本表所列主要層已進入 `dev`。
2. Provider-backed 欄只認 SQL Server 或對應真實 Provider 證據；InMemory、Fake Client、單元測試不可替代。
3. E2E 欄只認可從 UI 經 API 到資料庫／Provider 的可重跑旅程。目前儲存庫尚無 Playwright／E2E 測試檔。
4. Required CI 綠燈只證明當次已納入的 Gate 通過，不會自動把空白或部分功能改為完成。

## 明確未完成

- OpenAI Live Adapter 與 Live Model 評估。
- DATA-06 完整 10,000 筆展示 Seed 與特殊案例分布。
- Domain＋Application 70%、核心 Composable／Store 60% 的 Coverage 收集與失敗門檻。
- 各 M 功能的完整 SQL Server Provider-backed 覆蓋；PR #48 僅證明 Required CI 基礎 SQL Gate 已啟用且可執行現有測試。
- 核心交易與其他 M 功能的瀏覽器 E2E。
