---
文件狀態: 已確認
最後更新: 2026-09-08
追蹤項目: [DEMO-02]
---

# Demo 前檢查表

## 本輪 Release Candidate 基準

- 本輪 Demo runtime 候選重新固定為 `de49851693748a45dec1338be2b6919892ca2d9e`（PR #162 squash merge）；展示文件所在的後續 `dev` commit 可以更新，但 runtime、Prompt、Schema 或 Seed 有變更時必須重新固定並重跑相稱 Gate。
- 對應最終 PR head：`7bb7e21608c017de8a5924f2e47b89b555b1e890`；GitHub Actions Run `34187680882` 的 `CI Required`、Secret、Backend、雙 Frontend 與 Browser E2E 全部成功。
- DEV-04 已完成 Security、Required CI、exact-head review 與 squash merge；真人完整彩排仍未測試，故整體不得宣稱 `READY FOR REHEARSAL`。
- 展示環境開始前必須執行 `git rev-parse HEAD` 並取得本節最終完整 SHA；不一致時停止，不得以根工作樹或其他 revision 展示。

## AI Demo 固定路徑

- Route：`/ai-search`；角色：未登入的 `DEMO-GUEST`。
- 固定 Prompt：`SEARCH-CREATOR-013`「遊戲美術要同時跑繪圖與 3D，七萬五，請解釋取捨。」
- 成功判定：`CustomBuild`、繪圖、3D、最高 NT$75,000 均保留，推薦理由只依可見候選事實說明 GPU、64GB RAM 與預算取捨。
- 停止條件：30 秒、逾時、`429`、`503`、額度不足、Schema 拒絕或降級結果任一出現，即使用站內一般搜尋降級並接 `/builds/new`；不得在台上重試、改 Prompt 或除錯。
- 品質依據：product-search-v14／support-v7 focused 3／3 與完整 66／66 自動及人工覆核均 `PASS`；展示當日暖機只驗證可用性，不取代或重跑 Release baseline。

## 前一天

- [ ] `git rev-parse HEAD` 等於本節重新固定的 runtime Exact SHA，且 Migration、Prompt／Schema 版本與 Seed 版本已凍結並記錄。
- [ ] 執行 `reset-demo-data.ps1` 建立新的 `DoSelectDemo_<GUID>`，並確認輸出的 11 項資料 Gate 全數通過；不得刪除或覆寫 `DoSelectDb`／共用 `DoSelectDemo`。
- [ ] SQL＋檔案 Backup Set 已建立且還原驗證成功。
- [ ] 若已有核准的 AI／完整預錄片段，確認可離線播放且聲音、字幕正常；尚無核准片段時，確認站內一般搜尋降級可用，DEMO-RC-03 不因此改寫為完成。
- [ ] 展示帳號、TOTP、Email 收件匣與瀏覽器 Profile 可用。
- [ ] 固定訂單、低庫存、不相容、退貨、退款及報表案例存在。

## 展示前 60 分鐘

- [ ] SQL Server、磁碟空間、時間與時區正常。
- [ ] 目前 Windows 使用者已設定至少 32 UTF-8 bytes 的 `GuestOrderAccess__Pepper`，只確認存在／長度，不顯示或截圖值。
- [ ] 執行 `start-all.ps1 -Environment Demo`、`status.ps1`、`health-check.ps1`。
- [ ] API live／ready、Customer Web、Admin Web、Hangfire 都正常。
- [ ] `critical` Queue 無未說明 Failed Job，Outbox 無長時間未處理訊息。
- [ ] OpenAI、Brevo 與網路測試成功；AI 成本未達保護門檻。
- [ ] 使用固定瀏覽器縮放 100%，關閉通知、更新提示與不相關分頁。

## 展示前 10 分鐘

- [ ] 各帳號停在指定起始頁，敏感資料已遮蔽。
- [ ] 以本頁 `SEARCH-CREATOR-013` 暖機一次，核對成功判定；若失敗，只確認一般搜尋降級可接回 `/builds/new`，不在台上重試。
- [ ] 計時器與簡報已開啟；若已有核准預錄，另確認備用播放程式已開啟。
- [ ] 操作者、講者與切換人員確認停止條件及替代路徑。
- [ ] 手機、聊天軟體與系統聲音不會中斷展示。

任何 P0 檢查失敗且 10 分鐘內無法恢復，不執行現場修復；已有核准預錄時改播預錄，否則切至站內一般搜尋降級與確定性自由組裝路徑。
