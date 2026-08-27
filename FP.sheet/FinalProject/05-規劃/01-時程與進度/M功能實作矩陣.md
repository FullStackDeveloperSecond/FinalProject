---
文件狀態: 持續更新
最後更新: 2026-08-27
基準分支: dev@8ef986c
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
| M-01B 管理員 TOTP／Session | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #38 已合併至 `dev`：TOTP、Recovery Code、Session 撤銷、後台登入與 Provider-backed 證據存在；尚無瀏覽器 E2E。 |
| M-02 訪客結帳與訂單驗證 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 資料模型已在 `dev`；訪客查單 PR #40、取消／退貨入口 PR #43 尚未進 `dev`。本地 `codex/sh08-outbox-foundation` 已有 Checkout SQL Gateway，但未有 API／前端且尚未進 `dev`。 |
| M-03 商品、SKU 與目錄 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #24 已交付前後台型錄垂直切片；尚無瀏覽器 E2E。 |
| M-04 商品批次與 Excel | 🔵 | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | Schema／規格已存在，匯入 Staging、API、UI 與原子提交證據未完成。 |
| M-05 商品搜尋與篩選 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | 公開查詢與前台型錄已合併；尚無瀏覽器 E2E。 |
| M-06 購物車 | ✅ | ✅ | 🟡 | ✅ | ⬜ | 🔵 | PR #28 後端與 SQL 測試已合併；前端 PR #29 尚未進 `dev`。 |
| M-07 優惠券 | ✅ | ⬜ | ⬜ | ✅ | ⬜ | 🔵 | PR #7 計算／分攤／生命週期與 PR #8 `CouponRuleReader`／試算服務註冊／SQL 證據均已合併；尚無正式購物車套券 Endpoint。 |
| M-08 訂單 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 訂單模型已在 `dev`；後台管理 PR #47 尚未合併。本地整合分支已有原子建單、缺貨回滾與零元拒絕零副作用 SQL 證據，但 API、前端、完整 replay 與進 `dev` 尚未完成。 |
| M-09 模擬付款 | ✅ | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | PR #9 的七種付款政策與付款嘗試建立 Application／Domain 測試已合併；正式 API、付款成功回呼與 SQL Provider-backed 證據尚未形成。本地 Checkout 只建立初始 PaymentAttempt，不代表付款垂直切片完成。 |
| M-10 庫存保留與逾時取消 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | Reservation 模型已在 `dev`；API PR #36 與堆疊前端 PR #37 尚未合併。本地 Checkout SQL 測試已證明成功保留與缺貨整體回滾，最後一件商品並行競爭與逾時釋放仍缺。 |
| M-11 物流與批次出貨 | ✅ | ⬜ | ⬜ | 🟡 | ⬜ | 🔵 | Shipping／Shipment 模型已在 `dev`。本地 Checkout Gateway 已驗證配送方式、Provider、包裹限制、運費／免運並保存訂單快照，但尚未進 `dev`，且獨立 Application／API／UI／出貨流程未完成。 |
| M-12 單項退貨 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 完整退貨交付集中於 PR #42，尚未進 `dev`。 |
| M-13 部分退款 | ✅ | 🟡 | ⬜ | 🟡 | ⬜ | 🔵 | DES-21 快照與中央 Audit 已合併；退款執行 PR #16 仍為 Draft。 |
| M-14 客服案件與 SLA | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #10 已交付前後台、SLA 與 SQL 證據；DES-23 的完整 Action／衝突刷新範圍仍未關閉。 |
| M-15 營運報表 | 🔵 | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | 需求與資料來源已定，七個報表 Query／API／UI／SQL 證據未完成。 |
| M-16 自由組裝電腦 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 模型已在 `dev`；API PR #34 與堆疊前端 PR #35 尚未合併。 |
| M-17 零件相容性 | ✅ | 🟡 | 🟡 | 🟡 | ⬜ | 🔵 | 與 M-16 共用 PR #34／#35，尚未進 `dev`；本地整合分支另有受保護語意鍵、官方 CPU／晶片組映射、來源型 Catalog Reader、多值規格 Migration 與 SQL 證據，仍待合併及前端／E2E。 |
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
