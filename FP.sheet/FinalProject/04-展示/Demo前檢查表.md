---
文件狀態: 已確認
最後更新: 2026-08-13
追蹤項目: [DEMO-02]
---

# Demo 前檢查表

## 前一天

- [ ] Git Commit、Migration、Prompt／Schema 版本與 Seed 版本已凍結並記錄。
- [ ] SQL＋檔案 Backup Set 已建立且還原驗證成功。
- [ ] 完整預錄與 AI 片段可離線播放，聲音與字幕正常。
- [ ] 展示帳號、TOTP、Email 收件匣與瀏覽器 Profile 可用。
- [ ] 固定訂單、低庫存、不相容、退貨、退款及報表案例存在。

## 展示前 60 分鐘

- [ ] SQL Server、磁碟空間、時間與時區正常。
- [ ] 執行 `start-all.ps1`、`status.ps1`、`health-check.ps1`。
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
