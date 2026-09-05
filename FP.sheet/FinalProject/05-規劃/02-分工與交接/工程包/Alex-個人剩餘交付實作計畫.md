---
文件狀態: 進行中
最後更新: 2026-09-05
基準分支: dev@b2862e0b
實作分支: codex/wp-a05-ai-v6-smoke-20260905
實作人: alex
規劃範圍: alex 正式主責與已明確接手項目
下一工作包: WP-A05／AI-09 OpenAI Live baseline
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
- 既有 Checkout SQL 測試涵蓋成功、缺貨回滾、金額、優惠與超商資料；PR #85 已補訂單逾時取消、資源釋放與付款競爭證據。最後一件商品雙請求競爭與完整 Checkout replay 仍缺證據。
- 現有 `seed-minimal-development-data.ps1` 只是最小開發 Seed；尚無 10,000 筆完整展示產生器與 `reset-demo-data.ps1`。
- 所有需要啟動 API Host 的測試必須使用專屬 `DoSelectE2E_*`／測試資料庫，不得指向共用 `DoSelectDb`。

## 5. 執行順序

| 順序 | 工作包 | 結果 | 依賴 | 完成 Gate | 規模／不確定性 |
|---:|---|---|---|---|---|
| 1 | WP-A01／WP-H02 | M-01／M-01B 真實 Browser E2E | WP-H01、既有認證 UI／API | Member 與 Admin TOTP 正負旅程在隔離 SQL DB 可重跑；Browser E2E CI 通過 | M／低 |
| 2 | WP-A02／WP-H03 | Guest Access → 訂單明細／取消 Browser E2E | WP-A01、既有 Guest API／UI | 正確訂單可存取／取消；錯誤或其他訂單不可存取且零副作用 | M／低 |
| 3 | WP-A03／WP-H04 | C-14 Checkout 完整前端 | WP-A02、既有 Orders／Shipping／Payment 契約 | Guest／Member 可建立訂單；會員導向正式付款／訂單，訪客依限單驗證流程接續；錯誤、衝突與重複提交可處理 | L／中 |
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
| 2026-09-02 | WP-A03／WP-H04 | 完成／已進 `dev` | 由 `dev@e3e5abdb` 起完成 C-14：正式 `/checkout`、Cart 導頁、收件／宅配／示範門市、政策版本、七種模擬付款、模擬發票、Coupon 套用、冪等重試及錯誤處理。依 DEC-P351 回傳具體 `PaymentMethod[]`；DEC-P352 已由 DEC-P355 覆寫，C-15 改接上游 Owner-scoped Latest Attempt；依 DEC-P353 以 Coupon Quote 重算 Shipping／COD；依 DEC-P354 將 DiscountType 擴為 `varchar(24)`。Review 修正組裝購物車只允許組裝宅配。rebase `dev@70015781` 後 exact head `192e0499` 的 Backend、Browser E2E、雙前端、OpenAPI、Secret Scan、AI contract、Package Source Evidence 與 `CI Required` 全部成功；PR #91 squash merge 為 `dev@72db6fcc`，遠端來源分支已刪除。後續 WP-A04／WP-H05 已由 PR #94 補齊核心交易 Browser／SQL E2E。 |
| 2026-09-02 | WP-A04／WP-H05 | 完成／以 PR #94 進 `dev` | 使用固定 Guest Cart、八類組裝 SKU＋一件獨立 GPU 與 `CREATOR10`，在專屬 `DoSelectE2E_<GUID>` 完成 Cart → Checkout → 同請求 replay → Email＋訂單編號限單驗證 → 信用卡模擬付款成功 → Invoice 顯示；金額 45,000／-2,000／+300／+300＝43,600。額外獨立 GPU 讓優惠適用小計達 20,000，但不修改 AI 組裝依賴的相容性 Seed 單價。另以真實 SQL Server 證明同請求不重複建立 Order／Reservation／Payment／Idempotency，兩個 Guest Cart 競爭最後一件只成功一筆。依 DEC-P356，模擬端點只新增隔離 E2E Environment 顯式例外；Development／Production 仍 fail-fast。PR #94 已整合上游 PR #88 的導頁失敗復原，並以 rebase 後聚焦元件、typecheck、lint 與核心 Browser E2E 通過後進行 exact-head CI、final review 與 squash merge。 |
| 2026-09-03 | WP-A05／AI-09 | 進行中 | User Secrets 前置已完成。初次兩案例煙霧測試實際成本 US$0.001880、未達 US$0.10 停止線；商品搜尋因 `uniqueItems` 不在 Responses strict Schema 支援子集合而零 Token 失敗，客服通過 Schema／引用但政策來源不足。已移除該 Schema 關鍵字並在後端拒絕重複品牌，Runner 補上成本與案例選取防呆，兩個政策 Fixture 補齊 15 筆案例需要的核准快照，資料集升為 `zh-TW-v1.0.2-draft`。Kafen 主標與 Alex 第二審完成後 120 筆均核准，Release 與雙案例 dry-run 均為 `IsLiveReady=true`；仍待 commit 與第二次 Live 費用授權，初次結果不算 baseline。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中 | Commit `9ea03fc3` 已推送；第二次雙案例 Live 煙霧測試 deterministic 與人工覆核 2／2 Pass，成本 US$0.006085、Input／Output Tokens 3,545／694。商品搜尋單筆 10,083 ms 高於 5 秒目標，只列為正式 baseline 待確認風險；完整 Release 為 102 次商品搜尋＋27 次客服，共 129 次模型請求，尚待獨立成本授權。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中／首次 baseline 失敗 | Commit `5e7cc8f2` 的首次三輪 Release Adapter baseline 已完成 33 案／99 輪，成本 US$0.149338；Schema 74.75%、Intent 16.67%、Citation 77.78%、Deterministic 28.28%，商品／客服 P95 17,831／3,023 ms，Verdict `FAIL`。已依報告在本分支收束 Adapter scope 為 22 live／14 deterministic-only、96 規劃請求，修正安全拒絕、Prompt v2、grader v1.1、單行 JSONL、分階段觀測、分 feature Summary，以及執行前 metadata／逐案 append／checkpoint。focused tests 已通過；未進行付費重跑，模型品質與延遲改善尚未驗證。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中／修正版 Smoke 失敗 | 先建立 Commit `f195c453`，再以 6 個核准案例、1 輪與 US$0.05 停止線執行 Run `20260904T090934Z`；6／6 完成，規劃／實際 9／11 次請求、成本 US$0.010242，Verdict `FAIL`。執行中逐案與 T2 manifest 證據完整；客服引用／安全通過，商品暴露整機詞彙、衝突預算、Fixture 與 P95 18,742 ms 缺口。商品 Prompt v3 兩項契約修正 focused 20／20 通過；GPU／RAM Fixture 已升為 v1.0.3 並加入 Runner 映射與回歸測試，待 Terry／Alex 覆核。延遲方向仍待裁定，未付費重跑。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中／方案 A 零成本實作完成 | 依 DEC-P381～DEC-P384 將商品搜尋升為 `product-search-v4`：單次 5 秒 Responses 意圖呼叫、零同步重試，推薦理由只由後端核准候選事實確定性產生。公開 API／Schema／資料庫不變，三輪 Release 規劃由歷史 96 次降為 66 次；focused Infrastructure 22／22、Application 10／10 通過。尚未付費重驗 P95／品質，Fixture 兩案仍待 Terry／Alex 覆核。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中／標註與 deterministic 證據完成 | `SEARCH-CREATOR-008`、`013` 已完成 Terry 主標與 Alex 第二審，120 筆均為 `approved`，Release Dry Run 為 `AnnotationsApproved=true`、`IsLiveReady=true`。Run `20260904T120035Z-deterministic` 以正式 Application／Domain 路徑補齊 14 筆 deterministic-only orchestration，14／14 通過；無結果 UI 7／7 通過，模型呼叫與成本均為 0。Commit `f195c453` 的 6 案歷史 Smoke 正式人工複核亦已完成，3 Pass／3 Fail，維持 `FAIL`。只剩另行授權的 v4 Live Gate。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中／v4 Smoke 失敗 | 系統 PowerShell 的 6 案／1 輪 Run `20260904T133533Z-v4-smoke-system` 完成 6 次模型請求，成本 US$0.006287；客服 2／2 通過，商品 3／4 在 5 秒 Timeout 前無輸出，唯一成功案例於 4,679 ms 完成，商品 P95 5,033 ms，正式結果 `FAIL`。品牌偏好／排除理由缺口已在工作樹修正，聚焦測試 12／12；Timeout／5 秒方向待組長決策，未執行 baseline。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中／低延遲設定完成 | 依 DEC-BATCH-054 保留 `gpt-5.6-luna`、單次 5 秒、零同步重試與預設 service tier；商品 SearchIntent payload 新增 `reasoning.effort: none`、`text.verbosity: low`，strict Schema／白名單／後端驗證與公開契約不變。聚焦 Adapter 12／12、Live plan 12／12 通過；本批未呼叫 Provider，仍待另行授權固定 Smoke。 |
| 2026-09-04 | WP-A05／AI-09 | 進行中／低延遲 Smoke 失敗 | 系統 PowerShell Run `20260904T144911Z` 完成 6 案／6 次請求，成本 US$0.007224。商品 4／4 在 5 秒內取得 Provider 結果，P95 3,013 ms；但可用 Intent 3／4、Schema 83.33%、Intent 25%、有效推薦 66.67%，Verdict `FAIL`。品牌案例通過；013 多推論 Gaming、019 無效輸出、026 與資料集分類衝突。T2 證據與報告已形成；當時尚待的正式 Alex 覆核與 taxonomy 已於 2026-09-05 由 DEC-BATCH-055 完成。 |
| 2026-09-05 | WP-A05／AI-09 | 進行中／v5 零成本修正完成 | 依 DEC-BATCH-055 完成正式覆核 3 Pass／3 Fail，定版一般「主機」為 `PrebuiltComputer`、明示組裝或用途＋預算整機需求為 `CustomBuild`，且職稱「遊戲美術」不自動代表 Gaming。商品 Prompt 升為 `product-search-v5`；InvalidOutput 只在內部結果與評估 JSONL 保存固定原因碼＋欄位名稱，不保存 raw output，也不擴張公開 DTO／資料庫／產品日誌。RED 先證明契約尚無診斷欄位；GREEN 後 Infrastructure 24／24、Application 10／10、Build、Format、120 筆資料驗證與 6 案 Dry Run 均通過。本批未呼叫付費 Provider。 |
| 2026-09-05 | WP-A05／AI-09 | 進行中／v5 Smoke 失敗、v6 零成本修正完成 | Run `20260904T193855Z-v5-smoke-system` 為 6 案／6 次請求、US$0.006735、商品 P95 3,588 ms，品質 Verdict `FAIL`。依 DEC-BATCH-056，六案正式 Human Verdict 全部維持 pending；完成 `product-search-v6` 泛化 Prompt、大寫 Semantic Key＋精確 allowlist、選填 Citation 語意及預算／Badge 取捨理由。聚焦 Application 35／35、Infrastructure AI 26／26、Application 完整 578／578、Build、focused Format、120 筆驗證與 Release Dry Run 通過；完整 Infrastructure Provider-backed 測試因本機 SQL Server 加密／SSPI／登入中斷而不可用。本批未呼叫 Provider。 |
| 2026-09-05 | WP-A05／AI-09 | 進行中／顧客視角零成本修正完成 | Alex 確認舊商品回答未切合顧客問題，且以內部人員為讀者。依 DEC-BATCH-057，推薦理由改為承接用途、預算、硬性規格與軟性偏好，使用在地化顯示名稱並排除 Enum／代碼／Fixture ID／後端術語；Live Runner 將此列為確定性 Gate，人工覆核表並列顧客問題、必要回答重點與顧客可見回答。8 個回歸檢查先 RED，修正後 Infrastructure AI 聚焦測試 31／31 通過；未呼叫 Provider，AI-09 仍待可追溯 Commit、新 v6 小型 Smoke 與正式人工覆核。 |
| 2026-09-05 | WP-A05／AI-09 | 進行中／合併前契約飄移已修正 | Review 發現 DEC-P394 已定版大寫 Semantic Key，但正式 SQL 型錄 Metadata 仍轉成小寫，會讓含規格的真實 SearchIntent 被驗證器拒絕。已將正式 Metadata 統一 Trim＋大寫並補規格書與 SQL 斷言；Application AI 47／47、Infrastructure AI 31／31、系統 PowerShell SQL Server 聚焦 1／1 通過。未呼叫 OpenAI；v6 Live Gate 狀態不變。 |
| 2026-09-05 | WP-A05／AI-09 | 進行中／v6 Smoke 失敗 | PR #112 已 squash merge 為 `dev@eb83ecf6`。同 revision 的固定 6 案／1 輪 Run `20260905T100442Z-v6-smoke-system` 完成 6 次請求、US$0.007227，商品／客服 P95 4,199／2,126 ms；Schema、推薦、引用與隱私安全通過，但 Intent 75%、補問精確率 50%、Deterministic 66.67%，Verdict `FAIL`。`SEARCH-CREATOR-013` 多問兩個非必要問題，`SEARCH-NOVICE-019` 把 8TB 儲存容量映射成 8192GB 記憶體。T2 證據完整；Alex 後續顧客方向與根因審查確認 Runner fidelity／資料語意也需修正，舊覆核表不作為 v7 通過證據。 |
| 2026-09-05 | WP-A05／AI-09 | 進行中／v7 零成本修正完成 | 依 DEC-BATCH-059，Runner 在補問／既有零件確認時停止推薦；新增 `STORAGE_CAPACITY_GB`、分類規格白名單、TB→GB、Prompt v7，Dataset／Grader 升為 v1.0.4／v1.1.3。RED 證明三類根因後，Application AI 47／47、Infrastructure focused 50／50、API AI 24／24、系統 PowerShell SQL Server 1／1、Dataset 驗證與 Solution Build 通過；模型呼叫／成本為 0。功能 Commit `90e71a43` 已完成 Review、rebase `origin/dev@b2862e0b` 並建立 PR #120，尚待合併及另行授權的新 v7 Smoke。 |

## 11. 下一步

v6 歷史 Smoke 維持 `FAIL`；DEC-BATCH-059 的 v7 零成本修正已完成 Review、提交並 rebase 最新 `origin/dev`。下一步推送分支並完成 PR Gate；其後若取得新的付費授權，以同一固定六案／一輪／成本停止線執行 v7 Smoke，再由 Alex 覆核新顧客輸出。只有自動 Gate 與人工覆核都通過，才另行核准 66 次 Release baseline。
