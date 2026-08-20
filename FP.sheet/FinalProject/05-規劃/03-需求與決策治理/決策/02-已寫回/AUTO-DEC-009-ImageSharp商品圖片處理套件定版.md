---
type: decision-record
batch_id: AUTO-DEC-009
title: ImageSharp 商品圖片處理套件定版
status: applied
created_at: 2026-08-20
applied_at: 2026-08-20
source: alex 明確選擇 ImageSharp，並確認改用 3.1.12
---

# AUTO-DEC-009｜ImageSharp 商品圖片處理套件定版

## 正式決策

1. SH-06 商品圖片 Identify、解碼、EXIF 方向校正、Resize 及 WebP 編碼使用 `SixLabors.ImageSharp`。
2. 套件精確固定為 `3.1.12`，由 `Directory.Packages.props` 統一管理；不採 floating version。
3. 不加入 `ImageSharp.Web`，不建立動態 URL 圖片轉換服務；衍生圖只在受控上傳流程預先產生。
4. 產生長邊 320／800／1600 px、Q80 的 WebP，不放大原圖；公開衍生圖移除 Metadata，私有原圖保留原始位元。
5. 技術安全基線為 10 MB、最長邊 10,000 px、總像素 25,000,000、單 Frame、平行度 2、Allocator 256 MB 及 Pool 64 MB；任一衍生圖未完成前不得移入正式圖片目錄。

## 最低成本與商業影響

- 不新增圖片套件無法在跨平台 .NET 流程可靠完成 WebP、方向校正與安全解碼；要求管理員離線轉圖也不符合後台上傳與可重現 Demo 流程。
- `ImageSharp 4.1.1` 雖是較新主版，但直接相依專案在建置時要求授權金鑰，會增加五位組員、CI 與展示電腦的 Secret 配置及失效風險；`3.1.12` 已滿足 .NET 10、JPG／PNG／WebP 與本專題處理需求，因此採用較低維運成本的充分方案。
- 受影響者為型錄管理開發者、五位組員與 Demo 操作者。改善結果是所有環境可無額外授權 Secret 還原、建置並產生一致衍生圖；建置與維運成本限於單一套件及既有本機儲存流程。
- 成功指標為乾淨 Restore、0 警告 Build、套件稽核無已知漏洞警告，以及 JPG／PNG／WebP、三尺寸、不放大、偽格式、路徑穿越和失敗清理測試通過。

## 風險與回復條件

- `3.1.12` 是上一個主要版本；若官方停止安全維護、NuGet 出現已知漏洞，或後續需求必須使用 4.x API，建立獨立升級決策並先處理授權金鑰與五人開發環境配置。
- 若 ImageSharp 無法滿足安全、效能或 WebP 相容性驗收，回復套件與 `IImageStorage` 實作，另行比較替代方案；不得在同一 PR 同時維護兩套圖片引擎。
- 本決策沒有資料庫 Migration、公開 API 或既有圖片資料轉換。

## 外部依據

- [Six Labors ImageSharp 官方文件：4.x 直接相依需要建置授權](https://docs.sixlabors.com/articles/imagesharp/index.html)
- [NuGet 官方套件頁：SixLabors.ImageSharp 3.1.12](https://www.nuget.org/packages/SixLabors.ImageSharp/3.1.12)
