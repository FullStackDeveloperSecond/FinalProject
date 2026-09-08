---
文件狀態: 已確認
最後更新: 2026-09-08
追蹤項目: [DEMO-02]
---

# Demo 前檢查表

## 本輪 Release Candidate 基準

- 前一核准 Exact SHA：`48819192644fe3cbf7540e45274f5cf9c045307c`；最新已驗證合併基準為 `7134a3a9654c6ecf89ac6473db52e7883720f0cd`。
- 最新合併基準 Required CI：GitHub Actions Run `34173145044`，`CI Required` 成功。
- DEV-04 候選完成 Security、Required CI 與 squash merge 後，必須在本節重新填入唯一最終 Exact SHA；未重新固定前不得宣稱 `READY FOR REHEARSAL`。
- 展示環境開始前必須執行 `git rev-parse HEAD` 並取得本節最終完整 SHA；不一致時停止，不得以根工作樹或其他 revision 展示。

## 前一天

- [ ] `git rev-parse HEAD` 等於本節重新固定的最終 Exact SHA，且 Migration、Prompt／Schema 版本與 Seed 版本已凍結並記錄。
- [ ] 執行 `reset-demo-data.ps1` 建立新的 `DoSelectDemo_<GUID>`，並確認輸出的 11 項資料 Gate 全數通過；不得刪除或覆寫 `DoSelectDb`／共用 `DoSelectDemo`。
- [ ] SQL＋檔案 Backup Set 已建立且還原驗證成功。
- [ ] 完整預錄與 AI 片段可離線播放，聲音與字幕正常。
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
- [ ] AI 搜尋暖機一次，但不改變正式 Seed 關鍵案例。
- [ ] 計時器、簡報、預錄與備用播放程式已開啟。
- [ ] 操作者、講者與切換人員確認停止條件及替代路徑。
- [ ] 手機、聊天軟體與系統聲音不會中斷展示。

任何 P0 檢查失敗且 10 分鐘內無法恢復，直接改用預錄，不執行現場修復。
