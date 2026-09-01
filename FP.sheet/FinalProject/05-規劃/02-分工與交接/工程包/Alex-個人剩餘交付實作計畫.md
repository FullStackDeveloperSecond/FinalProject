---
文件狀態: 進行中
最後更新: 2026-09-01
基準分支: dev@9a034a16
實作分支: WP-A02 已由 PR #83 合併並刪除
實作人: alex
規劃範圍: alex 正式主責與已明確接手項目
下一工作包: WP-A03／WP-H04
---

# Alex 個人剩餘交付實作計畫

## 1. 決定

`READY`：依本文件順序逐包交付。每個工作包必須具備獨立測試、Review 與 exact-head CI 證據，完成後才能開始下一包。

本計畫只包含 `alex` 正式主責，以及 [[Alex-暫時接手Haru-負責範圍補全推進計畫]] 已明確移交給 `alex` 的 WP-H02～WP-H05。商品匯入、分類規格、優惠券、庫存管理、物流管理、退貨退款與其他組員領域功能不因本計畫轉移責任。

## 2. 目標與範圍

### 2.1 目標

1. 補齊會員、管理員、訪客及核心交易的可重跑瀏覽器證據。
2. 完成 AI Live 評估與兩條 AI 核心整合旅程。
3. 建立固定 10,000 筆展示資料、資料驗證與重設流程。
4. 完成萬筆效能、安全、恢復與 Demo 環境 Gate。

### 2.2 範圍內

- WP-H02～WP-H05：`alex` 已明確接手的 Haru 未完成範圍。
- `AI-09`、`INT-01`、`INT-03`。
- `SH-03`、`DATA-06`～`DATA-08`、`DEV-04` 中的展示 Seed／重設工作。
- `INT-05`、`INT-06`。
- 每包新增私人 Endpoint 時同步維護 `QA-08`；每包新增 Provider 行為時同步維護 `DEV-07`；每包新增高風險寫入時同步檢查 `DES-24`。

### 2.3 排除

- `M-04` 商品批次／Excel、`M-07` 優惠券、`M-10` 庫存後台、`M-11` 物流後台等其他成員主責功能。
- 未取得例外授權的 `S-05`、`S-06` 與其他 S 功能。
- 真實金流、真實物流、正式部署、公網環境與 Docker。
- 為了讓整合測試通過而另建平行 DTO、Endpoint、Checkout、Guest、Shipping 或 AI 路徑。

## 3. 最低成本分析與 Business Impact

### 3.1 最低成本分析

| 層級 | 判定 |
|---|---|
| 不修改 | 不足；現有 CI 綠燈沒有證明缺少的認證、Guest、Checkout、AI 隱私與萬筆資料旅程。 |
| 文件／人工流程 | 不足；人工 Demo 無法提供可重跑、可回歸與 Actor 隔離證據。 |
| 設定／既有能力 | 部分可用；既有 Playwright、SQL Fixture、OpenAPI Client、Seed 與腳本是主要擴充點，但目前案例與資料量不足。 |
| 延伸既有路徑 | 採用；逐包擴充既有測試、頁面、Fixture、Seed 與腳本，不新增服務、套件或 Schema。 |
| 新依賴／服務／Schema | 不採用；現有架構足以完成規格，新增項目只增加維護與回復成本。 |

### 3.2 Business Impact

| 項目 | 內容 |
|---|---|
| 影響對象 | 五人開發團隊、Demo 操作者與現場評審 |
| 現況風險 | 核心流程可能只在單層測試成立；共用資料庫誤用、越權、重複交易或展示資料不一致可能在整合或現場才暴露 |
| 預期結果 | 每個 alex 工作包具有可重跑的 UI／API／SQL／AI 或操作證據，最終可在固定資料下完成展示 |
| 建置成本 | 既有程式、測試與腳本的開發及 Review；不估造未提供的工時 |
| 經常成本 | 既有 CI 執行時間與已核准 OpenAI 預算內的 Live 評估；不新增服務費 |
| 風險成本 | 測試資料污染、AI 費用失控、跨模組契約飄移及整合回歸；以隔離資料庫、額度、exact-head Gate 與逐包回復控制 |
| 信心 | 高：第一至四包已有正式規格與既有垂直切片；AI Live 與萬筆效能受外部模型及展示硬體影響，信心中等 |
| 成功指標 | 本文件所有 Must 工作包完成；Required CI、核心 E2E、資料驗證、AI baseline 與 Demo 復原證據通過 |
| 停止／回復 | 發現新產品決策、公共契約、Migration、新依賴、外部費用或共用 `DoSelectDb` 風險時停止；每包獨立提交，可回復該包而不影響前包 |

## 4. 已驗證基準

- `dev@bc26de94` 已包含 PR #72 的 C-21／C-22，WP-H01 完成。
- `dev@e7a20f58` 已包含 WP-A01 的 PR #80 與收束文件 PR #81；WP-A02 從此 base 開始，Git Gate 前已 rebase 至 `dev@eb5ca4a1`。
- `dev@9a034a16` 已包含 WP-A02 的 PR #83；exact head `3dba8f4c` 的 Required CI 與最終 Review 全部通過，Guest Access 跨單隔離證據已進入 `dev`。
- `OrdersController.CreateOrder` 已提供 `POST /api/v1/orders`；不得另建第二套 Checkout API。
- 現有 Playwright 已涵蓋 M-01／M-01B 真實認證與 Guest 訂單存取／取消；C-14 完整 Checkout 前端與跨交易 Browser E2E 仍待 WP-A03～A04 補齊。
- 既有 Checkout SQL 測試涵蓋成功、缺貨回滾、金額、優惠與超商資料，但最後一件商品競爭、完整 replay 與逾時釋放仍缺證據。
- 現有 `seed-minimal-development-data.ps1` 只是最小開發 Seed；尚無 10,000 筆完整展示產生器與 `reset-demo-data.ps1`。
- 所有需要啟動 API Host 的測試必須使用專屬 `DoSelectE2E_*`／測試資料庫，不得指向共用 `DoSelectDb`。

## 5. 執行順序

| 順序 | 工作包 | 結果 | 依賴 | 完成 Gate | 規模／不確定性 |
|---:|---|---|---|---|---|
| 1 | WP-A01／WP-H02 | M-01／M-01B 真實 Browser E2E | WP-H01、既有認證 UI／API | Member 與 Admin TOTP 正負旅程在隔離 SQL DB 可重跑；Browser E2E CI 通過 | M／低 |
| 2 | WP-A02／WP-H03 | Guest Access → 訂單明細／取消 Browser E2E | WP-A01、既有 Guest API／UI | 正確訂單可存取／取消；錯誤或其他訂單不可存取且零副作用 | M／低 |
| 3 | WP-A03／WP-H04 | C-14 Checkout 完整前端 | WP-A02、既有 Orders／Shipping／Payment 契約 | Guest／Member 可建立訂單並導向正式付款流程；錯誤、衝突與重複提交可處理 | L／中 |
| 4 | WP-A04／WP-H05 | Cart → Checkout → Reservation → Payment → Order／Invoice 核心 E2E | WP-A03 | UI、API 與 SQL 斷言一致；重放、缺貨與 Actor 隔離有證據 | L／中 |
| 5 | WP-A05／AI-09 | OpenAI Live baseline | M-18、M-19、評估資料集 | 保存品質、P95、Token、成本與失敗／降級結果；費用不越過既定保護線 | M／中 |
| 6 | WP-A06／INT-01 | AI 導購→相容組裝→購物車 E2E | WP-A05、M-18、M-16／17 | 自然語言需求產生可解釋且可購買結果，Provider 失敗時安全降級 | M／中 |
| 7 | WP-A07／INT-03 | AI 客服、本人資料與隱私 E2E | WP-A05、M-19 | 同意、本人訂單、遮蔽、越權、額度與轉人工旅程通過 | M／中 |
| 8 | WP-A08／SH-03、DATA-06～08、DEV-04 | 固定 10,000 筆 Seed、驗證及重設 | 核心資料模型穩定 | 固定 seed 可重建；百筆特殊案例、關聯、狀態、庫存及報表基準自動驗證 | L／中 |
| 9 | WP-A09／INT-05 | M 非功能驗收 | WP-A04、A06～A08 | 萬筆 P95、併發、安全、Coverage、桌面與 Browser Gate 有保存證據 | L／高（展示硬體） |
| 10 | WP-A10／INT-06 | Demo 啟停、備份復原與完整彩排 | WP-A08～A09 | 一鍵啟停、Seed 重設、Backup→Restore、降級與完整操作腳本通過 | M／中（現場環境） |

`QA-08`、`DEV-07`、`DES-24` 是每包的橫向 Gate，不另開沒有交付結果的獨立工作包。

## 6. 工作包規則

1. 一次只執行一個工作包；不得把下一包的功能混入同一 PR。
2. 開始前刷新 `origin/dev`；若 `dev` 前進，先確認淨差異與依賴，不覆寫其他人遠端 head。
3. 優先使用 generated OpenAPI types、既有 API client、Fixture、Policy、Audit、Outbox、DbContext 與 BackgroundService。
4. 若失敗源自其他組員主責行為，建立具名阻塞與重現證據，不自行擴張成該模組實作。
5. 測試資料庫名稱必須專屬且可驗證；所有清理由建立者負責，失敗時採 fail-safe，不碰共用 `DoSelectDb`。
6. AI Live 工作包只能傳送既定最小化資料，必須遵守同意、本人資料、去識別化、額度與降級規則。
7. 每包完成後更新本文件執行記錄；影響正式狀態時再同步未完成追蹤表與 M 功能矩陣。

## 7. 驗證矩陣

| 需求 | 工作包 | 證據 | Gate |
|---|---|---|---|
| M-01／M-01B 真實認證 | WP-A01 | Playwright＋隔離 SQL Server＋Router／Session 斷言 | Browser E2E／Required CI |
| M-02 Guest Scope | WP-A02 | Playwright＋API／SQL 零副作用斷言 | Browser E2E／QA-08 |
| C-14 Checkout | WP-A03 | Vue component tests、typecheck、lint、build、契約 diff | Frontend／OpenAPI Gate |
| 核心交易 | WP-A04 | Playwright＋API＋SQL Provider-backed | Browser E2E／Backend／CI Required |
| AI 品質與成本 | WP-A05 | 固定資料集 live 結果、P95、Token、成本 | AI 評估 Gate |
| AI 導購與隱私 | WP-A06～A07 | Playwright、工具／引用、Actor A/B、降級斷言 | AI／Browser／QA-08 |
| 展示資料 | WP-A08 | 固定 Seed、完整性與報表基準驗證 | DATA-06～08／DEV-04 |
| 非功能與 Demo | WP-A09～A10 | 效能、併發、備份復原、啟停與彩排紀錄 | INT-05／INT-06 |

## 8. Definition of Ready／Done

### 8.1 Definition of Ready

- 前一工作包完成並已進入 `dev`，或使用者明確授權在尚未合併時繼續。
- 依賴的正式契約與資料來源存在，沒有需由使用者決定的產品規則。
- 可以建立隔離測試資料庫，且不會啟動指向共用 `DoSelectDb` 的 BackgroundService。
- 不需要未核准的新依賴、Schema、外部服務或費用。

### 8.2 每包 Definition of Done

- 驗收成功、失敗、授權、重複提交／併發與降級路徑依適用範圍完成。
- 聚焦測試與受影響的 build、typecheck、lint、OpenAPI／SQL／AI Gate 通過。
- Final diff 無其他工作包或其他成員未授權範圍。
- Final review 無未解決 P0～P3 finding。
- exact remote head 的 Required CI 通過後才可 approve／squash merge。
- 執行記錄與必要進度文件同步完成。

### 8.3 No-Go

- 任何驗證會操作共用 `DoSelectDb`。
- 需要修改其他成員尚未合併的分支或覆寫遠端 head。
- 規格與現有 API／資料語意衝突，且會改變產品行為。
- 需要 Migration、新套件、新外部服務、提高 OpenAI 費用或放寬安全／隱私邊界而未取得確認。
- Required CI、Actor 隔離、資料完整性或回復證據失敗。

## 9. 風險

| ID | 觸發與影響 | 緩解與偵測 | 停止／負責人 |
|---|---|---|---|
| RISK-A01 | E2E 指向共用資料庫，造成個資或展示資料清理 | 專屬 DB、啟動前檢查連線字串、fail-safe cleanup | 立即停止；alex |
| RISK-A02 | `dev` 與工作包契約同時前進，產生第二套 DTO／Endpoint | 每包 preflight、generated client diff、只消費正式契約 | 停止並 rebase／協調；alex |
| RISK-A03 | 跨模組 E2E 暴露其他人領域缺口 | 保存最小重現與責任邊界，不自行接手產品行為 | 標記 Blocked；原主責＋alex 整合 |
| RISK-A04 | Live AI 不穩定或成本超線 | 固定資料集、既定用量保護、保存模型與時間、驗證降級 | 停止 Live 呼叫；alex |
| RISK-A05 | 10,000 筆 Seed 產生非法歷史狀態 | 使用正式狀態機／服務或可信產生規則，執行完整性驗證 | Seed Gate 失敗不得進 Demo；alex |

## 10. 執行記錄

| 日期 | 工作包 | 狀態 | 證據／結果 |
|---|---|---|---|
| 2026-09-01 | 計畫建立 | 完成 | 依 `dev@bc26de94`、正式分工、未完成追蹤表、M 功能矩陣及程式／測試證據建立；下一包 WP-A01 |
| 2026-09-01 | WP-A01／WP-H02 | 完成 | PR #80 已由 exact head `7a606f52` 通過 Required CI 與 Review，squash merge 為 `dev@6968cbea`；新增會員 Session 與管理員首次 TOTP 綁定／錯碼拒絕／二次登入 Browser E2E，customer 5/5、admin 4/4 均以獨立 `DoSelectE2E_<GUID>` SQL DB 通過並完成清理。完整 customer 回歸另觀察到既有 M-19 降級路徑的 Vue `Unhandled rejection` 警告，留待 WP-A07，不混入本包。 |
| 2026-09-01 | WP-A02／WP-H03 | 完成 | PR #83 已由 exact head `3dba8f4c` 通過 Backend、兩個 Frontend、Browser E2E、Secret Scan、Package Source Evidence、AI Evaluation Contract 與 `CI Required`，最終 Review 無 P0～P3 finding，squash merge 為 `dev@9a034a16`，遠端分支已刪除。以兩張真實 Guest Checkout 訂單完成錯碼拒絕、正確查單、跨單 GET／取消 404、目標訂單取消及另一張訂單狀態／RowVersion 零副作用斷言；並補齊 `--seed-minimal` 主 SKU 的確定性包材尺寸。 |

## 11. 下一步

依序開始 `WP-A03／WP-H04`：先刷新最新 `dev`，核對 C-14 Checkout 現況、既有正式契約與缺口，再以最低成本範圍形成實作 Gate；不得另建第二套 Checkout API。
