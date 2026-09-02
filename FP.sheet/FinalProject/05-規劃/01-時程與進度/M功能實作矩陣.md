---
文件狀態: 持續更新
最後更新: 2026-09-02
基準分支: dev@e3e5abdb；WP-A03 本機分支尚未進 dev
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
| M-02 訪客結帳與訂單驗證 | ✅ | ✅ | 🔵 | ✅ | 🔵 | 🔵 | `dev` 已有 Checkout API、Guest Email 驗證／限單 Cookie、本人查單／取消與 WP-A02 跨訂單 Browser 隔離證據。WP-A03 本機已完成 C-14 與訪客成功交接，但尚未進 `dev`；Cart→Checkout→Guest 驗證→Payment 完整跨交易 E2E 仍由 WP-A04 追蹤。 |
| M-03 商品、SKU 與目錄 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | PR #24 已交付前後台型錄垂直切片；PR #52 新增可重跑的商品列表→詳情 Playwright Smoke，但尚未覆蓋後台型錄管理旅程。 |
| M-04 商品批次與 Excel | 🔵 | ⬜ | ⬜ | ⬜ | ⬜ | 🔵 | Schema／規格已存在，匯入 Staging、API、UI 與原子提交證據未完成。 |
| M-05 商品搜尋與篩選 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | 公開查詢與前台型錄已合併；PR #52 的 Playwright Smoke 已驗證固定 Seed 商品可由搜尋列表進入詳情，但尚未覆蓋完整篩選與排序組合。 |
| M-06 購物車 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | 購物車後端、前端、身份隔離、組裝整組移除、結帳前重驗及 C-13 配送預覽均已進 `dev`；完整 Cart→Checkout Browser E2E 仍缺。 |
| M-07 優惠券 | ✅ | ✅ | 🟡 | ✅ | ⬜ | 🔵 | `dev` 已有優惠券計算／生命週期、管理 API、A-23 與用途 Catalog picker；WP-A03 本機新增 Checkout 套用優惠券與 Coupon Quote 後 Shipping／COD 重算，但尚未進 `dev`。購物車頁套券 UI、持久化 Cart Coupon Endpoint 與完整 E2E 仍缺。 |
| M-08 訂單 | ✅ | ✅ | 🔵 | ✅ | 🔵 | 🔵 | 原子建單、缺貨回滾、訂單快照、正式 Checkout API、前後台訂單查詢與 WP-A02 Guest 查單／取消 E2E 已進 `dev`。WP-A03 C-14 尚待 Git Gate；完整 Checkout replay 與跨交易 E2E 仍缺。 |
| M-09 模擬付款 | ✅ | ✅ | ✅ | ✅ | ⬜ | 🔵 | 付款重試／新增 Attempt、Demo complete、Owner／Guest Scope、Antiforgery、`simulationKey` 冪等、SQL Writer、Audit／Outbox、付款成功發票 Consumer 與 Owner-scoped Latest Attempt GET 均已進 `dev`。WP-A03 已改接上游 latest 契約，C-15 可續接非終態、呈現可重試終態並保留已付款結果；尚待 Git Gate。COD 正式物流命令接線與完整 E2E 仍缺。 |
| M-10 庫存保留與逾時取消 | ✅ | 🟡 | 🟡 | ✅ | ⬜ | 🔵 | PR #52 已合併 Checkout 成功保留與缺貨整體回滾 SQL 證據；PR #36 已合併庫存保留／異動／盤點後台 API，堆疊前端 PR #37 尚未合併，最後一件商品並行競爭與逾時釋放仍缺。 |
| M-11 物流與批次出貨 | ✅ | ✅ | 🔵 | ✅ | ⬜ | 🔵 | 宅配／超取、Provider、包裹限制、運費／免運、配送選項與示範門市 API、C-13 預覽及 C-14 共用選擇元件已進 `dev`；WP-A03 本機新增可選 Coupon Code，後端以 Coupon Quote 重算免運、運費與 COD，並消費正式 Typed Client。批次出貨、物流狀態命令、COD 收款接線與完整 E2E 仍缺。 |
| M-12 單項退貨 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #42 已交付退貨申請、審核、寄回、收件、檢查、退款交接與前後台；PR #53 修復可信退款輸入及 CI Gate。仍缺完整瀏覽器 E2E。 |
| M-13 部分退款 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | 退款 execute、可信七類分攤、中央冪等／Audit、SQL Server 證據，以及 WP-05 的清單／明細 API、A-21/A-22、OpenAPI／Typed Client、角色路由、確認門檻與穩定 Idempotency-Key 均已隨 `8cf41558` 進 `dev`；完整 E2E 仍缺。 |
| M-14 客服案件與 SLA | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #10 已交付客服前後台、SLA 與 SQL 證據；PR #51 已完成主管 Action、Internal Note、Reopen SLA、案件工作台、Actor Scope、衝突刷新與中央 Audit。仍缺完整顧客→客服瀏覽器 E2E。 |
| M-15 營運報表 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | PR #66 已合併七個報表 Query、一般／財務 Policy、CSV／XLSX、A-27 UI、SQL Provider-backed 與 INT-04 對帳證據；已有代表性後台 Playwright 旅程，但未逐一涵蓋七個 Report Key。固定 10,000 筆資料下的 P95 仍待 DATA-06～08／效能 Gate。 |
| M-16 自由組裝電腦 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #34 已交付組裝清單、分享、整套加入購物車與 SQL Server 證據；PR #35 已交付前端並合併 `dev`。完整瀏覽器旅程仍缺。 |
| M-17 零件相容性 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #52 已交付來源型規格資料基礎；PR #34 已交付確定性檢查、後台規則、Audit 與 Provider-backed 證據；PR #35 已交付前端並合併 `dev`。完整瀏覽器旅程仍缺。 |
| M-18 AI 商品搜尋推薦 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | PR #62 已完成 Review、Required CI 並 squash merge 至 `dev`：strict Adapter、公開 Endpoint、10／30 額度、SQL 候選、既有零件確認、八類 CustomBuild、正式相容性、Fail Closed 保存、關鍵字降級、`/ai-search` UI、OpenAPI／Typed Client 均已合併。Provider-backed CustomBuild 與隔離公開搜尋降級 E2E 已通過；真實模型推薦旅程、品質、P95、Token／成本仍由 AI-09 獨立追蹤。 |
| M-19 AI 客服 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | PR #57／#58／#59 已合併 SQL-backed 同意／額度、Responses Adapter、本人 Order／SupportTicket／Conversation Query、互動／引用／Token／成本、US$70／US$90 保護、會員聊天、A-28 管理彙總與 Playwright 降級旅程。AI-09 live baseline 獨立保持未完成。 |
| M-20 模擬發票與折讓 | ✅ | ✅ | ✅ | ✅ | ⬜ | 🔵 | 折讓、付款成功冪等發票 Outbox／Consumer、前後台查詢、WP-06 開立／作廢 API、DEC-P348 窄查詢、A-24/A-25 管理 UI、WP-07 顧客發票 UI 與 SQL Server 49/49 均已納入 `dev`；COD 正式物流接線與完整跨層 E2E 仍缺。M-20 是模擬發票支援流程的實作追蹤 ID，不增加 [[01-需求/功能範圍]] 原列 19 項 M 功能數。 |

## 完成判定邊界

1. `已進 dev` 只表示該功能至少有部分程式在 `dev`；只有 `✅` 才表示本表所列主要層已進入 `dev`。
2. Provider-backed 欄只認 SQL Server 或對應真實 Provider 證據；InMemory、Fake Client、單元測試不可替代。
3. E2E 欄只認可從 UI 經 API 到資料庫／Provider 的可重跑旅程。PR #52 建立 Playwright／CI 與商品瀏覽 Smoke，PR #55 補入共用身分 Fixture 及前後台 Router Guard；這些基礎不等於核心交易與其他 M 功能 E2E 已完成。
4. Required CI 綠燈只證明當次已納入的 Gate 通過，不會自動把空白或部分功能改為完成。

## 明確未完成

- AI 商品搜尋與客服的 Live Model 品質、P95、Token 與成本評估均未執行；由 AI-09 獨立追蹤，不回退 M-18／M-19 已合併狀態。
- DATA-06 完整 10,000 筆展示 Seed 與特殊案例分布。
- 各 M 功能的完整 SQL Server Provider-backed 覆蓋；Required CI 已啟用 SQL Gate，但現有測試通過不代表每個功能案例皆有 Provider-backed 證據。
- 核心交易與其他 M 功能的瀏覽器 E2E。

Coverage 收集與失敗門檻已由 PR #55 納入 Required CI：Domain＋Application 70%，雙前端核心 Composable／Store 60%。Coverage Gate 完成只表示門檻可執行，不會自動補足缺少的功能測試。
