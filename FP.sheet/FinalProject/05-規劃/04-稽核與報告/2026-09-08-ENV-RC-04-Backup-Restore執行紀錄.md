---
文件狀態: 已執行／部分未測試
建立日期: 2026-09-08
最後確認: 2026-09-08
追蹤項目:
  - ENV-RC-04
  - QA-07
  - DEV-04
基準版本: 14cc90ebbdd5bb016452c5d3dddbc67f4a2a26a0
---

# ENV-RC-04 Backup→Restore 執行紀錄

## 結論

- SQL／展示資料 Backup→Restore 演練：`PASS`。
- 證據狀態：`EVIDENCE_INCOMPLETE`；`E:\FinalProjectData` 在執行前不存在，因此商品圖片／私有附件封存與還原未測試，依 alex 2026-09-08 裁定列為非阻擋，不得宣稱通過。
- ENV-RC-04／QA-07：維持未勾選，狀態為 `⚪ 未測試／非阻擋`；後續依序進入 ENV-RC-05。
- 現有共享 `DoSelectDemo`：唯讀驗證確認為舊 Seed 漂移，不修改、不刪除；由 DEV-04 的安全重設流程後續處理。

## 執行契約

| 項目 | 固定值 |
|---|---|
| Revision | `14cc90ebbdd5bb016452c5d3dddbc67f4a2a26a0` |
| PowerShell／.NET／SQL Server | `7.6.5`／`10.0.303`／`17.0.1125.2` |
| 正式隔離來源庫 | `DoSelectDemo_14cc90ebbdd5bb016452c5d3dddbc67f` |
| 正式隔離還原庫 | `DoSelectDemo_14cc90ebbdd5bb016452c5d3dddbc680` |
| Backup Set | `20260907T171432Z-5c77922a60324f8f90970aaa2ed54e44` |
| Migration | `20260902073750_AddInventoryMovementAdjustmentNote` |
| RTO 門檻 | 2 小時 |
| 資料邊界 | 只保存聚合結果、時間、檔名、大小與 hash；不保存 Secret、連線字串、帳密、資料列、機器名或使用者名 |

來源與還原都只使用本機 allowlist `DoSelectDemo_<32 hex>` 隔離資料庫。共用 `DoSelectDb` 與既有 `DoSelectDemo` 沒有被 Migration、重設或刪除。

## 首輪失敗與修正

1. 對既有 `DoSelectDemo` 的首輪 SQL `BACKUP ... COPY_ONLY, CHECKSUM` 與 `RESTORE VERIFYONLY` 成功。
2. `backup-demo.ps1` 在檔案來源為零時，因 StrictMode 下 pipeline 結果為 `$null` 而讀取 `.Count` 失敗；首輪 Manifest 正確保存 `result=failed`。
3. 以最小變更把快照來源強制收斂為陣列；PowerShell parse 與零來源 focused check 通過，建立 commit `14cc90eb`。
4. 修正後資料庫-only Backup Set 可成功產生，`files=null`；沒有用虛構空附件冒充檔案備份。
5. 既有 `DoSelectDemo` 與其還原庫的應用驗證均以相同三項失敗：`featureProfileMarker`、`specialDistributions`、`reportBaselines`。這證明還原忠實，但來源已落後目前固定 Seed。
6. 為避免直接重設共享展示庫，改由既有安全 Seed 腳本建立可拋棄的 allowlist 隔離來源，再完成正式演練。

## 正式演練結果

| 階段 | 結果 | 證據 |
|---|---|---|
| 隔離來源 Seed | PASS | `Created=true`、固定 Seed `20260907`、主要商業資料 10,000 筆 |
| 隔離來源應用驗證 | PASS | 11／11 checks 為 true、Failures 0、資料完整性異常 0 |
| SQL Backup／VERIFYONLY | PASS | `COPY_ONLY`、`CHECKSUM`；0.576 秒 |
| Manifest／SHA-256 | PASS | Revision、Migration、大小與 `.bak` hash 全部一致 |
| 隔離 Restore／DBCC CHECKDB | PASS | 0.966 秒；Manifest 回寫 `lastRestoreVerification.result=success` |
| 還原後應用驗證 | PASS | 11／11 checks 為 true、Failures 0、資料完整性異常 0 |
| Backup 起至應用驗證完成 | PASS | 71.508 秒，小於 RTO 7,200 秒 |
| 清理 | PASS | 3 顆本輪隔離 DB、2 個驗證目錄、2 個非正式 Backup Set 均清為 0 |
| 保留策略 `-WhatIf` | PASS | 沒有刪除；唯一保留 Backup Set 已有成功還原驗證 |
| 圖片／私有附件封存與授權 Smoke | 未測試／非阻擋 | 執行前 `E:\FinalProjectData` 不存在；Manifest `files=null` |

## 保留 Artifact

| Artifact | 大小 | SHA-256 | 保存位置 |
|---|---:|---|---|
| `manifest.json` | 798 bytes | `9f57aed252296cc4403f9546bda3f60ab7e215d26241e7c429565aac1e94a9d5` | `E:\FinalProjectBackups\20260907T171432Z-5c77922a60324f8f90970aaa2ed54e44` |
| SQL `.bak` | 2,654,720 bytes | `fee5308209e4bca65a1ced095b1bd0bae53ad76ccf3c98e759d90610055e1ddb` | 同一 Backup Set |

Repository 只提交去識別摘要與 hash；原始 SQL Backup 不進 Git。清除的失敗／非正式產物不可復原，但正式 Backup Set 仍保留且已通過還原驗證。

## 限制與後續

- 商品圖片、私有附件、還原後授權與五條核心 UI Smoke 尚未在具備實際檔案資料根目錄的展示環境執行；依使用者裁定為未測試、非阻擋。
- 既有共享 `DoSelectDemo` 目前不符合固定 Seed v2，不能作為正式展示就緒證據；DEV-04 必須提供可回復的 `reset-demo-data.ps1` 後再重建與重驗。
- 本輪只證明 SQL／資料庫 Backup→Restore 路徑與既有應用資料驗證器；不宣稱完整檔案復原或整體 Release Ready。

機器可讀摘要：[[2026-09-08-ENV-RC-04測試證據清單.json]]
