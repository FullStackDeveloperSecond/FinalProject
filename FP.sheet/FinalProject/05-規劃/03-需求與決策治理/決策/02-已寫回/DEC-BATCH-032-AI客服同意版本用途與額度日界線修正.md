---
batch_id: DEC-BATCH-032
status: applied
decision_date: 2026-08-28
decision_ids:
  - DEC-P328
  - DEC-P329
  - DEC-P330
---

# DEC-BATCH-032｜AI 客服同意版本、用途與額度日界線修正

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P328 | AI 客服同意的目前版本由後端程式碼 `AiConsentPolicy.CurrentVersion = 1` 定版；Admission Gate 只接受相同版本且 `Purpose=Support` 的最新有效紀錄。其他版本一律視為尚未同意，不寫入額度、不呼叫模型。後續政策文字升版必須和常數、同意 UI、測試及部署同批變更。 |
| DEC-P329 | AI 客服每日 20 則依既有需求於 `Asia/Taipei` 00:00 重置；資料仍以 UTC 保存與查詢。測試必須涵蓋台灣午夜前後邊界，禁止改以 UTC 00:00 重置。 |
| DEC-P330 | PR #57 尚未合併且 AI Migration 未套用共用資料庫，因此直接修正原 `20260828050333_AddAiSafetyConsentAndUsage` 的新表定義、Designer 與 Snapshot。重建時發現前一支 Migration Designer 遺漏更早已存在的 Product Image Hash model metadata；採 A 方案，不改既有已合併 Migration，也不讓 AI Migration 重複新增三個 Hash 欄位。 |

## Lowest-Cost Analysis

1. 不修正或只修文件：仍會接受舊版同意並錯算每日額度，無法滿足隱私與成本邊界，未採用。
2. 以部署設定保存目前版本：設定可能與實際政策文字分離，第一版沒有需要獨立營運切換的證據，未採用。
3. 沿用現有 Entity、Gate、Configuration 與 Migration，加入固定版本、Purpose 與時區邊界：不新增套件、服務或第二套資料模型即可完整修正，採用。
4. 修補已合併的前一支 Migration Designer 後重新 Scaffold：會擴大歷史檔修改範圍；原 AI Migration 尚未合併，可在自身範圍安全修正，未採用。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 使用 AI 客服的登入會員、管理同意政策與 AI 成本的人員 |
| 現況風險 | 舊版同意仍可能外送資料；台灣午夜到 08:00 的額度會被錯誤歸日；同意證據缺少用途 |
| 預期可量測結果 | 非目前版本同意與撤回後模型呼叫為 0；同意列具 `Purpose=Support`；額度於台灣午夜準時歸零 |
| 建置／持續成本 | 一個 Enum、一個版本常數、既有 Gate／Migration 的小幅修改與兩項 Provider-backed 測試；無持續費用 |
| 風險成本 | 政策升版若只改文字未同步常數會阻擋使用；以同批變更與回歸測試控制 |
| 信心 | 高；SQL Server focused 8／8、完整 Migration 重播、API 479／479 與 EF Pending Model 0 均已驗證 |
| 成功指標 | 版本不符為 `Missing` 且零 Usage；台灣午夜前後額度分界正確；Migration 不包含 Product Image Alter；完整後端 1,795／1,795 |
| 停止／回復條件 | Migration 出現既有表 Add／Alter／Drop、舊版同意可通過或台灣午夜邊界錯誤即停止合併；功能旗標維持關閉並修正前進 |

## 影響文件

- [[05-規劃/03-需求與決策治理/決策/02-已寫回/DEC-BATCH-031-AI客服同意額度與OwnerQuery定版]]
- [[02-領域需求/04-客服與售後/客服與AI功能]]
- [[02-領域需求/90-驗收規格/AI搜尋與客服驗收規格]]
- [[03-架構/03-資料與一致性/資料字典-會員客服AI與治理]]
- [[03-架構/06-AI設計/AI應用詳細設計]]
- [[03-架構/06-AI設計/AI測試與評估規格]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]
