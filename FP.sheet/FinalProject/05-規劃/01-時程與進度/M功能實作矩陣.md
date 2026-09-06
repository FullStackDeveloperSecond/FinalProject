---
文件狀態: 持續更新
最後更新: 2026-09-06
基準分支: dev@19b7d9c6（AI v7 搜尋評估修正：PR #120）；另列 PR #110（codex/kafen-completion）待合併補強
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
| M-01 會員註冊與登入 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | 註冊、驗證、登入、重設密碼已合併；PR #80 已補會員登入／Session Browser E2E，註冊、驗證與重設密碼仍未逐條覆蓋。 |
| M-01B 管理員 TOTP／Session | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | PR #38 已合併 TOTP、Recovery Code、Session 撤銷、後台登入與 Provider-backed 證據；PR #80 已補管理員登入／TOTP Browser E2E，Recovery Code 與撤銷變體仍未逐條覆蓋。 |
| M-02 訪客結帳與訂單驗證 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | C-14、會員／訪客建單後交接、Guest Email 限單 Cookie、本人查單／取消及 WP-A02 跨訂單隔離均已交付；WP-A04 再證明 Cart→Checkout→Guest 驗證→Payment／Invoice 的預付主旅程。其他訪客付款與錯誤變體尚未全覆蓋。 |
| M-03 商品、SKU 與目錄 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | PR #24 已交付前後台型錄垂直切片；PR #52 新增可重跑的商品列表→詳情 Playwright Smoke，但尚未覆蓋後台型錄管理旅程。商品圖片後台五條端點、`CatalogImage.*` Policy、A-06 圖片區塊與前台／商品卡圖片投影已交付（2026-09-04，Provider-backed 與 HTTP 測試各 10／6 支）。 |
| M-04 商品批次與 Excel | 🔵 | 🔵 | 🔵 | ✅ | ⬜ | 🔵 | PR #85 已合併商品批次上下架／調價、共用篩選的 CSV／XLSX 匯出、A-04 UI、單一交易與 SQL Provider-backed 證據。Excel／CSV 匯入 Staging、驗證預覽與原子提交仍未完成。 |
| M-05 商品搜尋與篩選 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | 公開查詢與前台型錄已合併；PR #52 的 Playwright Smoke 已驗證固定 Seed 商品可由搜尋列表進入詳情，但尚未覆蓋完整篩選與排序組合。 |
| M-06 購物車 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | 購物車後端、前端、身份隔離、組裝整組移除、結帳前重驗及 C-13 配送預覽均已交付；WP-A04 以固定 Guest Cart 完成組裝購物車到 Checkout 的預付主旅程。完整功能變體仍未逐一 E2E。 |
| M-07 優惠券 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | 已有優惠券計算／生命週期、管理 API、A-23、用途 Catalog picker、Checkout 套用與 Shipping／COD 重算；PR #97 已合併正式 Cart Coupon `POST/DELETE` 與購物車套券 UI，WP-A04 證明 `CREATOR10` 的門檻、上限與金額快照。其他優惠券錯誤與並行變體仍未完整 E2E。 |
| M-08 訂單 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | 原子建單、缺貨回滾、訂單快照、正式 Checkout API、C-14、前後台查詢與 Guest 查單／取消均已交付；PR #94 補核心隔離 Browser／SQL 旅程，PR #95 補同冪等鍵不同 payload 的 SQL 零副作用。PR #105 已補 Refund→Order 的 `OrderRefundStatus`／`RefundedAmount`／歷程同交易投影並合併為 `dev@4224cac0`；PR #115 再證明 COD 履約後 Paid／Completed 的顧客與管理員 Browser 投影。其他狀態變體仍未完整 E2E。 |
| M-09 模擬付款 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | 付款 Attempt、Demo complete、Owner／Guest Scope、Antiforgery、`simulationKey`、SQL Writer、Audit／Outbox、發票 Consumer、Latest Attempt 與 C-15 均已交付；WP-A04 補信用卡模擬成功至 Invoice 的隔離 Browser E2E。PR #106 已完成 COD 於宅配 `Delivered`／超取 `PickedUp` 的正式物流收款接線，PR #115 以隔離 Browser E2E 證明中間狀態不提前收款、Demo complete 拒絕 COD、最終 Paid／Completed 與冪等重播。其他付款失敗／逾期等非主路徑仍未完整 E2E。 |
| M-10 庫存保留與逾時取消 | ✅ | ✅ | ✅ | ✅ | 🔵 | 🔵 | 庫存保留／後台、Checkout 成功與缺貨回滾、逾時取消／釋放、RowVersion 與背景排程均已交付；WP-A04 新增兩個 Guest Cart 競爭最後一件的 SQL Server 證據，並由 Browser 主旅程形成保留。UC-ADM-INV-01 人工釋放端點（PR #36 round 3 撤回）已連同 `inventory_reservation.release` 中央 Audit 補回，A-12 釋放 UI 啟用。UC-ADM-INV-01 對帳 dismiss／resolve 端點（PR #36 round 4 撤回）已依組長裁定 A1～H1 補回，`inventory_reconciliation.dismiss`／`resolve` 中央 Audit 與案件同交易。其他逾時 UI 變體仍未 E2E。 |
| M-11 物流與批次出貨 | ✅ | ✅ | 🔵 | ✅ | 🔵 | 🔵 | 宅配／超取、Provider、限制、運費／免運、配送選項、門市 API、C-13／C-14 與 Typed Client 均已交付；WP-A04 證明組裝宅配與優惠後運費快照。批次出貨已由 PR #93 交付；PR #106 交付物流狀態命令（`POST /admin/shipments/{id}/actions/{action}`）、COD 於 `Delivered`／`PickedUp` 同交易收款與 Order Completed、訂單 DTO 物流摘要／歷程／availableActions。PR #115 以真實管理員 TOTP、批次出貨與兩種履約 UI 完成隔離 Browser E2E；其他失敗／退回變體仍未逐條 Browser E2E。 |
| M-12 單項退貨 | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | PR #42 已交付退貨申請、審核、寄回、收件、檢查、退款交接與前後台；PR #53 修復可信退款輸入，PR #99 讓核准／檢查同交易建立唯一待審退款，PR #102 讓正額退款成功時同交易完成 Return，PR #103 補零淨額核准取消與回放／回滾。PR #110 已補會員申請至管理員檢查／等待退款的完整瀏覽器旅程，並以退貨專用 E2E 管理員隔離 TOTP 狀態；與客服旅程同庫並行及依序執行均通過，Required CI 全綠且最終 Review 無 finding，待合併。 |
| M-13 部分退款 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | 退款 execute、可信七類分攤、中央冪等／Audit、清單／明細 API、A-21/A-22、OpenAPI／Typed Client、角色路由與穩定 Idempotency-Key 已進 `dev`；PR #99／#102／#103 補齊待審建立、Return 結案、獨立核准及零淨額原子取消。PR #105 再補 Order 退款累計投影並合併為 `dev@4224cac0`；完整 Infrastructure／SQL Server 1087/1087 與同步後聚焦 57/57 通過。完整退款瀏覽器 E2E 仍缺。 |
| M-14 客服案件與 SLA | 🔵 | ✅ | ✅ | 🔵 | 🟡 | 🔵 | 稽核校正：PR #10／#51 已交付客服基礎、主管 Action、Internal Note、Reopen、SLA 唯讀佇列、案件工作台、Actor Scope、衝突刷新與中央 Audit，但 `dev` 尚缺 UC-SLA-01 的 80%／100% 背景通知與 Resolved 3 天自動關閉，故不得列整體完成。PR #110 已補公開回覆瀏覽器旅程與 SLA Hangfire／Outbox／System Audit；rebase Review 另修正已處理案件塞滿掃描批次及重開 SLA 週期不得繼承舊暫停秒數，SQL Server 回歸 5／5 通過，Required CI 全綠且最終 Review 無 finding，待合併。 |
| M-15 營運報表 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | PR #66 已合併七個報表 Query、一般／財務 Policy、CSV／XLSX、A-27 UI、SQL Provider-backed 與 INT-04 對帳證據；已有代表性後台 Playwright 旅程，但未逐一涵蓋七個 Report Key。固定 10,000 筆資料下的 P95 仍待 DATA-06～08／效能 Gate。 |
| M-16 自由組裝電腦 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #34 已交付組裝清單、分享、整套加入購物車與 SQL Server 證據；PR #35 已交付前端並合併 `dev`。完整瀏覽器旅程仍缺。 |
| M-17 零件相容性 | ✅ | ✅ | ✅ | ✅ | ⬜ | ✅ | PR #52 已交付來源型規格資料基礎；PR #34 已交付確定性檢查、後台規則、Audit 與 Provider-backed 證據；PR #35 已交付前端並合併 `dev`。完整瀏覽器旅程仍缺。 |
| M-18 AI 商品搜尋推薦 | ✅ | ✅ | ✅ | ✅ | 🔵 | ✅ | PR #62 的公開 Endpoint、額度、SQL 候選、組裝／相容性、Fail Closed、關鍵字降級、UI 與契約已合併；PR #112 再將單次 5 秒意圖呼叫、後端確定性理由、低延遲設定、`product-search-v6`、大寫 Semantic Key 與顧客視角理由合併至 `dev@eb83ecf6`。同 revision 六案 Live Gate 因多餘補問及 8TB 儲存語意錯映射而 `FAIL`。DEC-BATCH-059 的 Runner fidelity、`STORAGE_CAPACITY_GB`、分類規格白名單、TB→GB、Prompt v7 與精確 grader 已由 PR #120 合併至 `dev@19b7d9c6`；零成本回歸通過，仍待另行授權執行 v7 Live Smoke 與新輸出人工覆核。公開 API／資料庫 Schema 不變，M-18 合併狀態不回退；Live Gate 由 AI-09 繼續追蹤。 |
| M-19 AI 客服 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | PR #57／#58／#59 已合併 SQL-backed 同意／額度、Responses Adapter、本人 Order／SupportTicket／Conversation Query、互動／引用／Token／成本、US$70／US$90 保護、會員聊天、A-28 管理彙總與 Playwright 降級旅程。AI-09 live baseline 獨立保持未完成。 |
| M-20 模擬發票與折讓 | ✅ | ✅ | ✅ | ✅ | 🔵 | 🔵 | 折讓、付款成功冪等發票 Outbox／Consumer、前後台查詢、開立／作廢 API、A-24/A-25 與顧客發票 UI 均已交付；WP-A04 補信用卡付款成功後由 Browser 輪詢並顯示發票。PR #106／#115 已補 COD 正式物流接線，以及宅配 `Delivered`／超取 `PickedUp` 後由 Outbox Consumer 開立唯一發票並顯示於顧客頁的隔離 Browser E2E；其他發票／折讓跨層變體仍缺。M-20 不增加 [[01-需求/功能範圍]] 原列 19 項 M 功能數。 |

## 完成判定邊界

1. `已進 dev` 只表示該功能至少有部分程式在 `dev`；只有 `✅` 才表示本表所列主要層已進入 `dev`。
2. Provider-backed 欄只認 SQL Server 或對應真實 Provider 證據；InMemory、Fake Client、單元測試不可替代。
3. E2E 欄只認可從 UI 經 API 到資料庫／Provider 的可重跑旅程。PR #52 建立 Playwright／CI 與商品瀏覽 Smoke，PR #55 補入共用身分 Fixture 及前後台 Router Guard；這些基礎不等於核心交易與其他 M 功能 E2E 已完成。
4. Required CI 綠燈只證明當次已納入的 Gate 通過，不會自動把空白或部分功能改為完成。

## 明確未完成

- AI 客服已有歷史 Live 樣本；`product-search-v6` 六案 Smoke 已於 `dev@eb83ecf6` 執行，延遲、成本與安全通過，但 Intent、補問精確率及 deterministic Gate 失敗。v7 零成本修正已由 PR #120 合併至 `dev@19b7d9c6`，仍待另行授權執行 v7 Live Smoke 與新輸出人工覆核；由 AI-09 獨立追蹤，不回退 M-18／M-19 已合併狀態。
- DATA-06 完整 10,000 筆展示 Seed 與特殊案例分布。
- 各 M 功能的完整 SQL Server Provider-backed 覆蓋；Required CI 已啟用 SQL Gate，但現有測試通過不代表每個功能案例皆有 Provider-backed 證據。
- 核心信用卡預付主旅程已有 WP-A04 E2E，COD 宅配／超取履約收款與發票主旅程已由 PR #115 補齊；其他付款、物流、退款／折讓及各 M 功能的非主路徑瀏覽器 E2E 仍未完整覆蓋。

Coverage 收集與失敗門檻已由 PR #55 納入 Required CI：Domain＋Application 70%，雙前端核心 Composable／Store 60%。Coverage Gate 完成只表示門檻可執行，不會自動補足缺少的功能測試。
