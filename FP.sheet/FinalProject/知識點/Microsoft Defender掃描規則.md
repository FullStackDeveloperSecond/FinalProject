---
type: knowledge
title: Microsoft Defender 檔案掃描規則
aliases:
  - Microsoft Defender 掃描規則
  - Defender Antivirus File Scan
tags:
  - 知識點
  - 安全
  - 檔案上傳
  - Microsoft Defender
created_at: 2026-08-09
related:
  - "[[02-領域需求/客服與AI功能]]"
  - "[[05-規劃/未完成項目追蹤表]]"
---

# Microsoft Defender 檔案掃描規則

## 目的與邊界

Microsoft Defender 掃描是上傳防護的一層，不能取代檔案大小、副檔名、MIME、檔案簽章、授權與安全儲存等驗證。

本專案已確認由 `IFileScanner` 隔離掃描器實作，第一版在 Windows 展示電腦以 Microsoft Defender 掃描暫存檔；掃描不可用或結果不明時拒絕保存。

## 建議處理流程

```text
接收上傳串流
→ 檢查大小上限
→ 產生系統隨機檔名
→ 寫入不可執行、不可公開存取的隔離區
→ 驗證允許副檔名、MIME 與檔案簽章
→ 呼叫 Microsoft Defender 自訂掃描
→ 只在明確判定乾淨時搬到正式儲存區
→ 保存掃描稽核資料
```

原始檔名只能當作經過編碼的顯示資訊，不可直接成為磁碟檔名，也不可與應用程式放在同一目錄樹。

## MpCmdRun 呼叫方式

Microsoft 官方提供 `MpCmdRun.exe` 進行自動化掃描。常見位置為：

```text
C:\ProgramData\Microsoft\Windows Defender\Platform\<version>\MpCmdRun.exe
C:\Program Files\Windows Defender\MpCmdRun.exe
```

應在啟動時解析最新可用平台版本，不要把 `<version>` 寫死。單檔掃描可採自訂掃描：

```powershell
MpCmdRun.exe -Scan -ScanType 3 -File "<quarantine-file>" -DisableRemediation
```

使用 `-DisableRemediation` 可避免掃描器自行修改上傳暫存檔，讓應用程式掌握後續刪除及稽核流程。路徑必須以程序參數安全傳遞，不得拼接成可注入的 shell 指令。

## 結果規則：Fail Closed

只有「掃描程序正常完成且明確無偵測」才能放行。其他狀況一律拒絕或維持隔離：

| 狀況 | 對外結果 | 內部處理 |
|---|---|---|
| 明確無惡意內容 | 接受 | 搬移至正式區 |
| 偵測到威脅 | 拒絕 | 刪除或隔離並記錄 |
| 掃描錯誤或結果不明 | 拒絕 | 記錄錯誤，不可放行 |
| 程序逾時 | 拒絕 | 終止程序並清理暫存檔 |
| Defender 不存在、停用或病毒碼不可用 | 拒絕 | 回報服務暫時不可用 |

官方文件中，標準回傳碼 `0` 可能包含「無惡意程式」或「已成功處理威脅」，`2` 也可能代表未處理威脅、需要動作或掃描錯誤。因此實作應固定使用不自動修復模式，並把退出碼、標準輸出與錯誤輸出視為同一份掃描結果；不可只因程序有啟動就視為通過。

## 稽核欄位

```text
FileScanResult
├─ FileId
├─ ScannerName
├─ EngineOrPlatformVersion
├─ SecurityIntelligenceVersion
├─ StartedAt / CompletedAt
├─ Outcome
├─ ExitCode
├─ DetectionName
└─ FailureReason
```

日誌不可記錄檔案內容、SMTP 密鑰或使用者提供的未清理路徑。暫存檔應在成功、拒絕、逾時及例外路徑都能清理。

## 執行與效能注意事項

- 設定明確的檔案大小、程序逾時及同時掃描數量上限。
- 不允許使用者指定任意伺服器路徑，掃描範圍只能是本次上傳的隔離檔案。
- 啟動前檢查 Defender 可用性；健康檢查失敗時不要降級為「免掃描」。
- 高流量環境宜採隔離狀態與背景掃描；本機 Demo 若同步掃描，也要避免無限等待。
- 測試環境可注入假掃描器，涵蓋 Clean、Infected、Error、Unavailable、Timeout，不應停用整段安全流程。

> [!warning] 部署限制
> 這個方案依賴 Windows 與 Microsoft Defender。若日後部署到 Linux、容器或多節點環境，應保留 `IFileScanner` 契約並改接適用的掃描服務。

## 參考資料

- [Microsoft Learn：使用 MpCmdRun 管理 Microsoft Defender Antivirus](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus)
- [Microsoft Learn：ASP.NET Core 檔案上傳安全](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0)
- [[05-規劃/決策/02-已寫回/DEC-BATCH-002-第二批核心決策]]
