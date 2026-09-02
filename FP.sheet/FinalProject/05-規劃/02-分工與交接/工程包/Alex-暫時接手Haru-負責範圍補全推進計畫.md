---
文件狀態: WP-H01～WP-H04 已合併，WP-H05 待 Git Gate
最後更新: 2026-09-02
基準分支: dev@a565627c
執行分支: codex/wp-a04-core-transaction-e2e-20260902
原負責人: haru
暫時接手: alex
第一線覆核: yinyin
關聯 PR: "#72、#80、#83、#91"
---

# Alex 暫時接手 Haru｜未完成範圍與推進記錄

## 1. 現況結論

Haru 範圍不是全部重做。最新 `dev` 已包含會員／管理員認證、會員 Profile／地址 API、訪客查單 API 與頁面、正式建單、付款嘗試及訂單查詢；應只補尚未存在的前端與 E2E，並避免建立第二套 Checkout、Guest、Shipping 或 DTO。

PR #72 原本混合 `M-01`、`M-02`、`M-08`、`S-01`，且 base 落後、最新 head 沒有 Required CI。2026-09-01 採用 DEC-HARU-01 A2：沿用原分支，以一般 merge 整合最新 `dev`，拆除已被取代或尚未授權內容，再按本文件逐包推進。禁止 rebase、force push；Approve、merge、關閉 PR 與發布留言仍需另行授權。

`S-01` 沒有可追溯的本輪例外授權，維持 B1：先不推進。PR #73 的 Shipping／Store 公開查詢已於 `dev@0f6be3ba` 合併，並完成 Provider／Store brand 語意校準；C-14 的 Shipping 前置 Gate 已解除，但仍依工作包順序先完成 WP-H02／WP-H03。

## 2. 權威基準與範圍

| 類型 | 基準 |
|---|---|
| 實作 | `dev@e93f2d2a` |
| 接手 PR | `#72 haru/feature/member-favorites@84e99ab` |
| 配送依賴 | PR #73 已合併為 `dev@0f6be3ba`；Shipping／Store 仍為 Terry-owned 公開邊界 |
| 會員 API | 最新 dev 的 `GET/PUT /api/v1/members/me` 與 Address CRUD |
| UI 規格 | C-21 `/account`、C-22 `/account/addresses`，皆為 Member Guard |
| 測試資料 | 專屬、可清理 SQL Server DB；不得寫入共用 `DoSelectDb` |

### 範圍內

- `M-01`：Profile／收件地址 UI 與會員 Browser E2E。
- `M-01B`：管理員登入、TOTP、Recovery Code、Session 撤銷的 Browser E2E。
- `M-02`：既有 Guest API／頁面／Owner Scope 的完整旅程證據。
- `M-08`：消費正式 Orders／Policy／Payment／Shipping 公開契約的 C-14 與核心 E2E。
- Haru 擁有的 Order／OrderItem／Owner Scope 交叉驗收。

### 範圍外／Gate

- 不改寫 Yinyin 的 Coupon／Payment／Refund／Invoice 內部實作。
- 不接手 Terry 的 Shipping／Store 內部實作。
- 不新增 Schema、Migration、Package、認證管線或 Production SQL。
- `S-01` 需明確例外授權後另行重審，不隨 WP-H01 合併。

## 3. 未完成矩陣

| 功能 | 最新 dev | PR #72 可用成果 | 推進方式 |
|---|---|---|---|
| M-01 認證 | 已有前後端 | 無獨特核心變更 | 不重做；補 Browser E2E |
| Profile／地址 API | 已完成且有 RowVersion／Owner 證據 | 無後端必要差異 | 直接重用 typed contract |
| C-21／C-22 UI | 尚缺 | 有 Vue 頁面、query、component tests | WP-H01 校準後保留 |
| M-01B | 已合併 | 無必要差異 | 補 Browser E2E |
| M-02 Guest | API／頁面已進 dev | #72 是重複路徑 | 移除重複；補完整旅程 |
| M-08 Orders／Payment | 正式 API 已進 dev | #72 CheckoutController 已過期 | 移除重複；消費正式契約 |
| Shipping／Store | PR #73 已合併 | #72 的不完整宅配 reader 已移除 | 後續只消費正式公開契約 |
| C-14 Checkout UI | 尚缺 | #72 寫死政策、只支援宅配、Guest 成功後導向 401 | 不直接保留，待依賴後重建 |
| S-01 Favorites | dev 尚缺 | #72 有完整切片雛形 | B1 延後，待例外授權 |

## 4. 工作包順序

1. `WP-H01`：C-21／C-22 Profile 與地址完整前端切片。
2. `WP-H02`：M-01／M-01B 真實 Browser E2E。
3. `WP-H03`：M-02 Guest Access → Order detail／cancel 旅程。
4. ~~Review／merge PR #73，先證明 Store brand code 與 Shipping profile code 語意可正確對接。~~ 已於 `dev@0f6be3ba` 完成。
5. `WP-H04`：C-14 Checkout 完整前端。
6. `WP-H05`：Cart → Checkout → Payment → Order／Invoice 隔離 SQL Server E2E。
7. `WP-H06`：Haru 對 DES-21／DES-22 的 Order 邊界交叉覆核。
8. `WP-H08`：依 exact evidence 回填矩陣、追蹤與本文件。
9. `WP-H07`：S-01，只在明確例外授權後執行。

## 5. 共通 Gate

- Final diff 只包含當前工作包；不得有第二套 Checkout／Guest／Shipping。
- OpenAPI／generated schema 無 drift，不手寫平行 DTO。
- 前端需有 loading、empty、error、retry、conflict、success 與重複提交防護。
- Member／Admin MFA／Guest Scope／Owner A-B 正負案依風險使用 API 或 Browser 證據。
- Provider-specific、RowVersion、冪等、金額、replay、rollback 不以 InMemory／SQLite 代替 SQL Server。
- 本機成功不等於可合併；最後需固定 remote head、獨立 review、必要測試與 Required CI。

## 6. WP-H01 驗收

- C-21 使用正式 `/account`，Member Guard；顯示遮蔽 Email；可更新 display name、phone、locale。
- C-22 使用 `/account/addresses`，Member Guard；支援列表、新增、修改、刪除與預設地址。
- 使用 generated Members DTO、既有 API client、antiforgery 與 TanStack Query。
- RowVersion 衝突保留編輯內容並提供可理解訊息；寫入期間不可重複送出。
- 地址刪除先確認；歷史訂單地址快照不受會員主資料刪改影響。
- 聚焦 Vitest、router guard、typecheck、lint、production build、contract drift 檢查通過。
- 無後端、資料模型、Migration 或 SQL 變更。

## 7. 風險與停止條件

| 風險 | 停止／處理 |
|---|---|
| merge 後出現重複路由／Controller／DTO | 以最新 dev 為權威移除；若需改正式契約則停下裁定 |
| PR #73 ProviderCode 語意無法對接 Checkout | WP-H04／H05 No-Go，先由 Shipping／Checkout 邊界補測試 |
| E2E 會寫共用 `DoSelectDb` | 不執行；改用專屬資料庫與精確清理 |
| 需要 Migration、Identity／Policy 共用變更 | 停止並走材料變更 Gate |
| S-01 未授權 | 不實作、不隨本輪 merge |

## 8. 執行記錄

| 日期 | 項目 | 狀態 | 證據／結果 |
|---|---|---|---|
| 2026-09-01 | 範圍稽核 | 完成 | 比對 dev、PR #72／#73、分工、UI／API 契約與現有測試 |
| 2026-09-01 | DEC-HARU-01 | 採用 A2 | 使用原分支；以獨立實作 worktree 避免主工作區與測試 worktree |
| 2026-09-01 | DEC-HARU-02 | 維持 B1 | S-01 暫不推進，待明確例外授權 |
| 2026-09-01 | merge 最新 dev | 完成 | 一般 merge `origin/dev@a2786a2d`；不 rebase、不 force；merge commit `894d3036` 已推送原分支 |
| 2026-09-01 | PR #72 範圍收斂 | 完成 | 移除舊 Guest／Checkout、PR #73-owned Shipping、未授權 Favorites；最終程式 diff 只留 WP-H01 |
| 2026-09-01 | WP-H01 review 修正 | 完成 | C-21 校正為 `/account`；統一地址 mutation pending；等待 canonical refetch；加入刪除確認與契約長度驗證；final review 無 P0～P3 finding |
| 2026-09-01 | WP-H01 前端驗證 | 通過 | 聚焦 3 files／24 tests；customer-web 全部 43 files／301 tests；typecheck、lint、production build 通過 |
| 2026-09-01 | WP-H01 最終差異 | 通過 | `origin/dev...894d3036` 僅 10 個 customer-web 程式／測試檔與本文件，共 11 檔；無後端、OpenAPI、generated schema、Migration 或套件變更 |
| 2026-09-01 | PR #72 exact-head CI | 通過 | GitHub run `33473509137`：Backend、Browser E2E、customer/admin Frontend、OpenAPI contract、Secret Scan、AI contract、Package Source Evidence 與 `CI Required` 全部成功 |
| 2026-09-01 | 合入 PR #73 後覆核 | 通過 | 一般 merge `dev@0f6be3ba` 至 PR #72，最終 diff 仍為相同 11 檔；customer-web 43 files／301 tests、typecheck、lint、production build 全數通過；無 OpenAPI／generated schema drift |
| 2026-09-01 | PR #72 最終 exact-head CI | 通過 | 遠端 head `a9596de9`；GitHub run `33475238852` 的 Backend、Browser E2E、兩套 Frontend、OpenAPI、Secret Scan、AI contract、Package Source Evidence 與 `CI Required` 全部成功 |
| 2026-09-01 | PR #72 合併 | 完成 | 標題校正為「完成會員個人資料與收件地址前台（M-01／C-21／C-22）」；Approve 後於 `2026-09-01 06:02:46 UTC` squash merge，dev commit `e93f2d2a` |
| 2026-09-01 | 共用 DB 啟動副作用 | 已停止並裁定保留 | OpenAPI check 啟動 API 時誤連 `DoSelectDb`，觸發 7 天未驗證會員清理；155 筆於 `2026-09-01 05:09:46.221 UTC` 匿名化。API 已立即停止；msdb 無 DoSelectDb backup；使用者採用選項 1，接受既有保留規則結果，不復原 |
| 2026-09-02 | WP-H02／WP-H03 | 完成／已進 `dev` | WP-H02 會員 Session／管理員 TOTP Browser E2E 由 PR #80 合併；WP-H03 Guest 查單、跨單隔離、取消與 SQL 零副作用由 PR #83 合併。兩包皆使用專屬 `DoSelectE2E_<GUID>`。 |
| 2026-09-02 | WP-H04 | 完成／已進 `dev` | C-14 Checkout 與 C-15 付款續接由 PR #91 合併；只消費正式 Orders／Shipping／Payment／Policy／Guest 契約。 |
| 2026-09-02 | WP-H05 | 本機完成／待 Git Gate | 固定 Guest Cart 完成組裝商品、優惠、宅配、信用卡、訂單 replay、Guest 驗證、模擬付款與發票 Browser 旅程；另有 SQL Server replay／最後庫存競爭證據。依 DEC-P356 只允許隔離 E2E 顯式啟用模擬端點，未操作共用 `DoSelectDb`。尚待 rebase、final review、exact-head CI 與 squash merge。 |

### 8.1 共用 DB 事故後續約束

- 本機 OpenAPI check 因共用 DB 風險中止，不將本機「Host 可啟動」列為 WP-H01 證據；本輪沒有 API／OpenAPI 變更，並已用 `git diff --exit-code origin/dev` 證明兩個 generated contract 檔無差異。後續 GitHub CI 已在隔離容器資料庫完成 migration、Host 啟動與 OpenAPI contract 驗證。
- 後續不得為 contract、前端或 E2E 驗證啟動指向共用 `DoSelectDb` 的 API Host。
- 需要 Host 的測試必須先建立專屬資料庫、明確覆寫 Connection String，並確認啟動型 BackgroundService 不會操作共用資料。
- 不自行重建已匿名化個資；使用者已裁定接受此次符合 7 天規則的清理結果。

## 9. 下一步

WP-H01～WP-H04 已合併；WP-H05 的本機實作與聚焦驗證完成，下一步只處理最新 `dev` rebase、final review、exact-head Required CI 與 squash merge。完成後 Haru 暫時移交的 H02～H05 即告一段落；WP-H06 仍是 Order 邊界交叉覆核，WP-H07／S-01 仍無本輪授權。
