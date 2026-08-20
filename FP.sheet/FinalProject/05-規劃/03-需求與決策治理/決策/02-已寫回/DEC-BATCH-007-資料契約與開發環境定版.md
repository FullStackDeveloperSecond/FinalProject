---
type: decision-record
batch_id: DEC-BATCH-007
title: 資料契約與開發環境定版
status: applied
decision_count: 30
decision_range: DEC-P145～DEC-P174
submitted_at: 2026-08-12
applied_at: 2026-08-12
source: 原始 Meta Bind 互動表單；依 AUTO-DEC-008 由 Git 歷史追溯
---

# DEC-BATCH-007｜資料契約與開發環境定版

## 決策

| ID | 決策結果 |
|---|---|
| DEC-P145 | 超商 Profile 安全範圍為單邊 1～45 cm、三邊和 3～105 cm、重量 0.1～5 kg；宅配為單邊 1～150 cm、三邊和 3～150 cm、重量 0.1～20 kg。 |
| DEC-P146 | alex 是會員、客服檢舉、優惠金流、商品購物及共用整合的唯一具名備援。這是團隊接受的單點風險；各模組測試責任尚未逐項指定。 |
| DEC-P147 | 星期三整合基準 90 分鐘、星期日 150 分鐘；實際時間依當下議題彈性調整，未完成內容轉成具名工作。 |
| DEC-P148 | 星期日五人必到；星期三由 alex 與受影響模組人員參加。`dev` 失敗時停止後續合併，由變更負責人與 alex 修復或回復。請假與無法當日修復的替代流程仍待補充。 |
| DEC-P149 | Product、SKU、Specification 三份 CSV 共用 Multipart Request 的 `templateVersion` 欄位，後端以同一版本原子解析與驗證。 |
| DEC-P150 | CSV 固定 UTF-8 with BOM、逗號分隔、雙引號跳脫及固定英文 Header；Null 與空字串語意分離。 |
| DEC-P151 | 商品匯入不直接異動庫存；初始庫存使用獨立 Inventory Import／Adjustment，保存原因並建立 InventoryMovement。 |
| DEC-P152 | 內部主鍵使用 `bigint identity`；對外不可猜資源另設 `uniqueidentifier PublicId`，API 不暴露連號主鍵。 |
| DEC-P153 | C# Entity／Property 使用單數 PascalCase；SQL Table 使用複數 PascalCase，Column 使用 PascalCase；FK 採 `{Entity}Id`。 |
| DEC-P154 | UTC 持久化使用 `datetime2(3)`；只有需保存外部原始時區偏移的事件使用 `datetimeoffset(3)`。 |
| DEC-P155 | 新台幣金額 `decimal(18,2)`、比例／折扣率 `decimal(9,6)`、數量 `int`；分攤最後一筆吸收尾差。 |
| DEC-P156 | 字串基線：Email 320、電話 32、姓名 100、Code 64、標題 200、URL 2048、一般說明 4000；不全面使用 `nvarchar(max)`。 |
| DEC-P157 | FK 預設 Restrict；只有 Aggregate 內無獨立生命週期的 Owned Detail 可 Cascade。主資料停用／匿名化，交易、庫存與稽核不得 Cascade 刪除。 |
| DEC-P158 | 單一 ASP.NET Core Identity User Store，MemberProfile／AdminProfile 分離，並分開 Cookie Scheme 與授權 Policy。 |
| DEC-P159 | 商品圖保持比例產生 320／800／1600 px 長邊 WebP，Quality 80，不放大原圖。 |
| DEC-P160 | 公開商品圖使用 `/media/products/{publicId}/{variant}/{contentHash}.webp`，內容雜湊 URL 搭配一年 Immutable Cache。 |
| DEC-P161 | Rolling Log 保存 14 天、單檔 100 MB、總量 2 GB 時警告。 |
| DEC-P162 | 一般 Log 保存 PublicId、角色與遮蔽 IP；完整 IP 只在必要 AuditLog 依授權保存，不記姓名、Email 或地址。 |
| DEC-P163 | 展示單機啟動一個 Hangfire Server，固定 4 Workers；量測後才能調整。 |
| DEC-P164 | SearchIntent.intent 固定 `SingleProduct`、`PrebuiltComputer`、`CustomBuild`；不確定時澄清，不以 Unknown 直接搜尋。 |
| DEC-P165 | requiredSpecs 使用 `{ semanticKey, operator, value, unit }[]`；operator 只允許 `eq`、`gte`、`lte`、`in`，語意鍵與單位由後端白名單驗證。 |
| DEC-P166 | AI 工具回傳 `ok`、`not_found`、`forbidden`、`state_conflict`、`unavailable` Result Union，只向模型提供安全錯誤碼。 |
| DEC-P167 | M 階段 120 筆 AI 評估全部繁中；啟動多語系 S 後另增日文 30、韓文 30。 |
| DEC-P168 | terry 主標搜尋／相容性，kafen 主標客服／越權，alex 第二審與發布核准；主標者不可單獨核准自己修改的版本。 |
| DEC-P169 | 一般 PR 只跑 Stub 與安全整合測試；真實 OpenAI 評估由手動工作流及功能凍結前執行，執行前確認預估成本。 |
| DEC-P170 | 評估 P95：搜尋 ≤5 秒、客服 ≤10 秒；平均成本：搜尋 ≤US$0.01、客服 ≤US$0.03。超標先檢查 Prompt／上下文，不自動升級模型。 |
| DEC-P171 | 每日額度於 Asia/Taipei 00:00 重設；訪客使用遮蔽 IP Hash＋第一方 Browser ID，Browser ID 保存 30 天，不採 Fingerprinting。 |
| DEC-P172 | Demo Allowlist 設兩個會員 PublicId 與一個 Browser ID，只繞過 US$90 非 Demo 成本停用，不繞過登入、同意、授權、遮蔽或安全測試。 |
| DEC-P173 | 選擇自有寄件網域驗證，寄件名稱 `alex`、候選位址 `alexyang920528@gmail.com`。因未提供可控制 DNS 的網域且 `gmail.com` 非團隊自有網域，此決策方向已記錄，但 Brevo 實作仍待補網域或改採已驗證單一寄件者。 |
| DEC-P174 | 前後台使用 Node.js 24 LTS＋npm＋`package-lock.json`；根目錄版本檔固定 Node Major，不混用套件管理器。 |

## 一致性與保留事項

- DEC-P145 完成 DEC-P120 未定的 Provider Profile 精確上下限。
- DEC-P146 明確覆蓋 DEC-P115 原本「跨模組備援」的方向：alex 成為所有核心模組唯一備援。這不等於模組測試與交接已完成。
- DEC-P147／P148 補足整合時長、出席與失敗停止條件；請假與跨日修復流程仍待決策。
- DEC-P149～P151 固定 CSV 封裝、語法與庫存分離，但 Product／SKU／Specification 的精確 Header 及欄位型別仍待資料字典完成。
- DEC-P152～P158 是全專案資料契約基線；實際逐表 PublicId、FK、刪除與 Profile 關聯仍須資料字典審核。
- DEC-P159／P160 完成商品圖片尺寸與公開路由方向；本機根目錄、備份份數及未發布預覽仍待落實。
- DEC-P164～P166 完成主要 AI Schema 與工具錯誤結構；字串／陣列上限與完整 DTO 仍待定版。
- DEC-P167～P170 完成 AI 評估治理門檻，但 120 筆實際案例仍未建立。
- DEC-P172 只定 Allowlist 規格，不在文件保存實際展示識別。
- DEC-P173 存在選項與自主輸入衝突：Gmail 位址無法證明自有寄件網域。正式寄送前必須補充可驗證網域或建立後續決策。

## 已寫回文件

- [[01-需求/核心商業規則]]
- [[02-領域需求/02-商品庫存與組裝/商品、組裝與相容性]]
- [[02-領域需求/03-交易與履約/購物車、訂單、付款與物流]]
- [[02-領域需求/01-會員與身分/會員、驗證與通知]]
- [[02-領域需求/04-客服與售後/客服與AI功能]]
- [[03-架構/02-API與前端契約/API共通規範]]
- [[03-架構/06-AI設計/AI應用詳細設計]]
- [[03-架構/06-AI設計/AI測試與評估規格]]
- [[03-架構/03-資料與一致性/資料模型與ERD]]
- [[03-架構/03-資料與一致性/資料字典索引]]
- [[03-架構/03-資料與一致性/資料庫正規化與反正規化策略]]
- [[03-架構/01-系統與環境/系統架構]]
- [[03-架構/01-系統與環境/本機開發環境與版本基線]]
- [[03-架構/04-安全與檔案/檔案與圖片儲存設計]]
- [[03-架構/05-背景工作與維運/Logging與HealthCheck設計]]
- [[03-架構/05-背景工作與維運/背景工作與Hangfire設計]]
- [[03-架構/01-系統與環境/非功能需求]]
- [[05-規劃/01-時程與進度/40天開發計畫]]
- [[05-規劃/03-需求與決策治理/需求追蹤矩陣]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]

## 追蹤結果

- 完成：`DOM-13`、`TECH-07`。
- 已縮小但仍進行中：`PM-05`、`PM-08`、`DES-07`、`DES-08`、`DOM-10`、`AI-02`、`AI-05`、`AI-09`、`TECH-04`、`TECH-06`、`TECH-10`、`QA-01`、`QA-03`、`DEV-02`。
- 新增：`DES-15`，逐表審核正規化與受控反正規化。
- 仍需補充決策：Brevo 可驗證網域／單一寄件者、各模組測試責任、整合請假與跨日修復、PublicId 清單、AI DTO 上限、圖片根目錄與備份、資料讀取模型形式。
