# DEC-BATCH-070｜AI 完整組裝意圖補問正規化

- 狀態：已完成，合併後 focused 與完整 baseline 通過
- 日期：2026-09-07
- 決策者：Codex（依 alex 常駐授權）
- 來源：`dev@65c2318f` 的 v13／support-v7 Dataset v1.0.13 完整 Release baseline

## 問題與證據

合併後 `SEARCH-NOVICE-023` 聚焦為 3／3 自動與人工 Pass，成本 US$0.001687。其後完整 baseline 完成 66／66，成本 US$0.135779，但正式結果仍為 `FAIL`：

- `SEARCH-CREATOR-013` 第 1 輪已輸出 `CustomBuild`、GraphicDesign、ThreeDRendering 與最高 NT$75,000，卻又問「組裝電腦，還是購買現成整機」，因此不進推薦。
- 同案例另兩輪正確推薦 NT$70,000 的核准候選並以 GPU 預算優先與 64GB RAM 解釋取捨。
- 其餘 65 輪人工 Pass；所有 27 輪客服回答通過。沒有隱私、授權、引用、憑證、Prompt 洩漏、政策矛盾或寫入安全問題。

## 最低成本分析

1. 不處理／接受現況：不採用。完整必要欄位仍可能被不相干補問阻斷，顧客得不到推薦。
2. 只補文件、訓練或同版本重跑：不採用。無法改變 runtime 對已解析輸出的處理，且模型變異可重現。
3. 設定、資料校正或 feature flag：不採用。Dataset 期待與輸出 intent／purpose／budget 均正確，沒有可校正的資料或既有設定。
4. 重用既有程式路徑：採用。在繁中 intent post-processing 中，僅於 CustomBuild 的 purpose 與 maximum budget 都完整時，移除同時詢問組裝／客製與現成／整機的矛盾補問。
5. 新增依賴、抽象、服務、Schema 或基礎設施：不適用；既有 Adapter 已負責受限正規化。

## 決策

- 商品行為版本升為 `product-search-v14`；模型 Prompt 文字不擴充，變更位於既有 deterministic post-processing。
- 只有 `zh-TW`、`CustomBuild`、至少一個 purpose、最高預算存在且問題同時命中組裝類與整機類詞彙時才移除。
- purpose 或預算缺少時不得移除；其他補問不得一併移除。
- Dataset 保持 `zh-TW-v1.0.13-draft`，support 保持 `support-v7`，grader 保持 `deterministic-v1.1.9`。
- 不改模型、service tier、5,000 ms timeout、同步 retry、公開 API、資料庫、工具、會員邊界、成本或任何安全／隱私門檻。

## 驗收與後續 Gate

- 真實失敗輸出回歸已恢復無補問的完整 CustomBuild intent；缺少 purpose 與非型態補問負例均保留，client 35／35 Pass。
- 120 案生成／Schema／分布／隱私、Application 616／616、精確非 DB AI 104／104、Release build 0 warning／0 error、format 與 66-request dry-run 全部通過。
- 固定 working-tree source snapshot 的 Security diff scan `4d1bb303-a527-4d57-a8c0-3f9076a921a9` 覆蓋 2／2 個安全相關檔案，權威摘要 `4f4480b705afa2d73bcfd1ecb00cd370394a38c3c0f60a7f1d11765e6855f555`，為 0 candidate／0 finding／0 deferred、coverage `complete`。
- 合併後先執行 `SEARCH-CREATOR-013` 三輪聚焦 Live；自動與人工 3／3 通過才可重跑完整 66-request baseline。
- 任一核心補問被誤刪、推薦品質、timeout 或安全失敗都回修正循環。

## 合併後結果

- PR #142 Required CI Run `34066775635` 與 exact-head review 全綠後，因同帳號無法自我核准，依 alex 既有授權以管理員 bypass squash merge 為 `dev@f3c44ae717ff02533123e16eecba4f347472487f`。
- `SEARCH-CREATOR-013` 三輪 focused：3／3 自動與人工 Pass，成本 US$0.001764，P95 3,042 ms，T2 evidence Pass。
- 完整 baseline：66／66 requests，自動門檻全 100%，正式人工 66 Pass／0 Fail，成本 US$0.135933，商品／客服 P95 2,509／2,623 ms，T2 evidence Pass。
- 無隱私、授權、憑證、Prompt 洩漏、非核准引用、政策矛盾、unsafe write 或 unsupported product fact 事件。

## 影響摘要

- 影響對象：已提供完整用途與最高預算、且被解析為繁中客製組裝意圖的使用者。
- 現況風險：已觀察 39 個商品輪次中 1 輪因矛盾補問沒有推薦；此觀察值不外推為 Production 發生率。
- 預期成果：原始失敗輸出可進入既有核准候選與 grounded explanation 流程。
- 建置／持續成本：一個 bounded filter、三個回歸與既有 CI／Live Gate；無新依賴或 recurring infrastructure。
- 風險成本：過度移除真正必要補問；以雙詞類命中、完整 core fields 及負向回歸限制。
- 信心：中高；失敗輸出的矛盾狀態明確，且修正不依賴重新呼叫模型。
- 成功指標：零成本 Gate、聚焦 3／3、完整 66／66 與正式人工覆核全部通過。
- 停止／回復條件：若缺少核心欄位或其他補問被移除，立即回復此變更並重新收窄判定。
