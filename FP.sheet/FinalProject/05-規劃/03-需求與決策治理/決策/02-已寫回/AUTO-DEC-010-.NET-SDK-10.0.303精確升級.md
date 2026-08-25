---
type: decision-record
batch_id: AUTO-DEC-010
title: .NET SDK 10.0.303 精確升級
status: applied
created_at: 2026-08-21
applied_at: 2026-08-21
source: alex 選擇將精確 SDK 基準由 10.0.302 升級至 10.0.303
---

# AUTO-DEC-010｜.NET SDK 10.0.303 精確升級

## 正式決策

1. `global.json` 的 SDK 版本由 `10.0.302` 精確升級至 `10.0.303`。
2. 保留 `rollForward: disable` 與 `allowPrerelease: false`；本次不改回浮動 Patch，也不允許其他 SDK 自動替代。
3. GitHub Actions 繼續以 `global-json-file: FP.dev/global.json` 安裝相同 SDK，五位開發者亦須使用 `10.0.303`。
4. 升級只在 Restore、警告視為錯誤的 Build、Format、完整 Test 與 NuGet 弱點檢查通過後成立；任一相容性失敗即回復 `10.0.302` 並另行處理。

## 最低成本與商業影響

- 繼續使用既有使用者層 `10.0.302` 可以暫時建置，但無法解決 Windows 同 Feature Band servicing update 已由 `10.0.303` 取代舊 Patch所造成的全機安裝衝突。
- 改回 `latestPatch` 雖可降低安裝阻塞，卻會失去五位成員與 CI 精確一致的可重現性，因此不足以滿足既有驗收契約。
- 採用最小充分方案：只提升精確 servicing patch，不更換 Feature Band、Target Framework、NuGet 套件、語言版本或 CI 架構。
- 受影響者為五位開發者與 GitHub Actions；預期結果是本機與 CI 能用 Microsoft 目前同 Feature Band 的修補版本一致建置。無新增服務或持續費用。
- 成功指標為 `dotnet --version` 輸出 `10.0.303`，且 Restore、Build、Format、Test 與弱點檢查全數通過。

## 相容性與回復

- `10.0.303` 與 `10.0.302` 同屬 `10.0.3xx` Feature Band；本次仍以實際全套建置證據判定相容，不以版本號推定成功。
- 若產生 SDK、MSBuild、Source Generator、EF Tool 或測試相容性錯誤，回復 `global.json` 與現行文件至 `10.0.302`，並使用隔離的官方 SDK 安裝，不移除其他專案需要的 SDK。
- 無資料庫 Migration、公開 API、前端契約或執行期商業行為變更。

## 外部依據

- [Microsoft：.NET releases, patches, and support](https://learn.microsoft.com/en-in/dotnet/core/releases-and-support)
- [Microsoft：.NET SDK 10.0.303 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.11/10.0.303.md)
