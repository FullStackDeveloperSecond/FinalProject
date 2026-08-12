---
type: decision-record
batch_id: DEC-BATCH-006
title: 開發定版與驗收門檻決策
status: applied
decision_count: 30
decision_range: DEC-P115～DEC-P144
submitted_at: 2026-08-12
applied_at: 2026-08-12
source: "[[05-規劃/決策/00-互動中/DEC-BATCH-006-開發定版與驗收門檻決策]]"
---

# DEC-BATCH-006｜開發定版與驗收門檻決策

## 決策

| ID | 決策結果 |
|---|---|
| DEC-P115 | 採核心主責＋跨模組備援；alex 為組長，負責共用、架構與整合；haru 負責會員；kafen 負責客服與檢舉；yinyin 負責優惠與金流；terry 負責商品與購物。個別備援對應仍需排定。 |
| DEC-P116 | 固定整合改為每週兩次：星期三 10:00 與星期日 11:00；整合時長、必到人員與缺席處理仍需補充。 |
| DEC-P117 | Day 20 起每週一次局部彩排，Day 30 起每兩天一次完整彩排，Day 36～39 每天一次。 |
| DEC-P118 | CSV 匯入使用三個獨立上傳欄位，分別提交 Product、SKU、Specification 資料集，三者共同預覽及原子提交。 |
| DEC-P119 | XLSX 與 CSV 匯入接受目前模板版本與前一版本；前一版轉成目前內部格式後再驗證。 |
| DEC-P120 | 超商與宅配使用不同 Provider Profile 與安全上下限；精確數值尚未選定，維持待確認。 |
| DEC-P121 | 硬性不相容規則固定阻擋；後台只可調整明確開放且受安全上下限保護的警告餘裕門檻。 |
| DEC-P122 | PSU 級距採 450、550、650、750、850、1000、1200、1500 W。 |
| DEC-P123 | AI 搜尋解析與摘要使用 `gpt-5.6-luna`，AI 客服使用 `gpt-5.6-terra`；品質不足時才依評估升級。 |
| DEC-P124 | OpenAI 整合統一採 Responses API，並由後端 Adapter 控制工具、Structured Outputs 與保存設定。 |
| DEC-P125 | 開發期使用可設定 Alias；功能凍結後，若有 Snapshot，鎖定通過評估的 Snapshot 供 Demo 使用。 |
| DEC-P126 | SearchIntent 用途 Enum 固定為 Gaming、VideoEditing、ThreeDRendering、GraphicDesign、Office、Programming、Streaming、General，可多選。 |
| DEC-P127 | 完整組裝至少需要用途與最高預算；單品至少需要商品類別或可辨識關鍵字；缺少必要資訊時每次最多補問 2 題。 |
| DEC-P128 | AI 客服使用本人訂單摘要、公開 FAQ、退貨政策、公開商品詳情四個只讀工具；引用固定包含來源類型、來源 ID、標題及版本／更新時間。 |
| DEC-P129 | 第一版 AI 評估資料集共 120 筆：新手搜尋 30、創作者 20、相容性 20、無結果／降級 15、客服政策 15、本人訂單／越權／注入 20。 |
| DEC-P130 | AI 發布門檻：Schema Valid ≥98%、Intent Field Accuracy ≥90%、Citation Grounding ≥95%；隱私／授權、合法推薦與安全降級必須 100%。 |
| DEC-P131 | 前後台使用 `openapi-typescript`＋`openapi-fetch`，共用 wrapper 處理 Credentials、CSRF、Correlation ID 與錯誤。 |
| DEC-P132 | CI 匯出 OpenAPI、重新產生型別、要求 Git Diff 為空，並執行兩個 Vue 專案 Typecheck。 |
| DEC-P133 | Hangfire 使用 `critical`、`notifications`、`maintenance`、`ai` 四個 Queue；Job 必須可冪等。 |
| DEC-P134 | Hangfire 重試依工作分類：Email／暫時外部錯誤 3 次、清理工作 2 次、商業狀態衝突 0 次。 |
| DEC-P135 | Hangfire Dashboard 只有完成 TOTP 的 SuperAdmin 可進入，且維持唯讀；人工重試另走具理由與稽核的系統管理 API。 |
| DEC-P136 | 消費者前台支援 Chrome、Edge、Firefox、Safari 最新版與前一版；後台支援 Chrome、Edge 最新版與前一版。 |
| DEC-P137 | 10,000 筆展示資料下，P95 門檻為一般讀取 1 秒、非 AI 寫入 2 秒、七個報表 3 秒；AI 維持既有 8／12 秒逾時。 |
| DEC-P138 | 併發驗收至少涵蓋 20 個同時結帳競爭同一 SKU、50 個同時讀取及 10 個同時後台操作。 |
| DEC-P139 | 每日完整備份，重大 Migration／Demo 重設前額外備份；RPO 24 小時、RTO 2 小時。 |
| DEC-P140 | Domain＋Application 行覆蓋率至少 70%；前端核心 Composable／Store 至少 60%；高風險案例仍是必要閘門。 |
| DEC-P141 | OrderManager 與 SuperAdmin 可新增、編輯及停用示範門市；CatalogManager 只讀。 |
| DEC-P142 | 三案件領域附件於案件結束後保存 180 天；暫存與孤兒檔 24 小時清除；每日執行，失敗最多重試 2 次，支援爭議保留旗標。 |
| DEC-P143 | 商品圖片採本機檔案系統＋`IImageStorage`；專案外資料目錄保存原圖、產生 WebP 縮圖及不可猜檔名，資料庫保存來源、授權與中繼資料。 |
| DEC-P144 | 採 Serilog 結構化 JSON、Console 與每日 Rolling File；`/health/live` 檢查程序，`/health/ready` 檢查 SQL Server 及必要本機依賴，詳細資訊限授權管理員或本機腳本。 |

## 一致性與保留事項

- DEC-P116 取代 DEC-P44 的每週三次整合，正式節奏改為每週兩次；舊決策保留於歷史紀錄但不再適用。
- DEC-P116 已決定每週兩次及開始時間，但沒有填入整合時長、必到人員與缺席處理；正式計畫只寫入已確認內容。
- DEC-P120 只定案 Provider Profile 分離，精確上下限尚未選定。參考台灣目前公開限制時，可將超商 Profile 的既有 45 cm／105 cm／5 kg 視為不可超越上限；宅配方案仍需選定採用的模擬服務基準。
- DEC-P123 的模型識別需由部署設定管理；若帳號無法使用指定模型，必須回到決策流程，不得自行換模。
- DEC-P129 固定總數與六類分布，但繁中、日文、韓文的案例比例及標註／覆核者仍待安排。
- DEC-P137 固定 API 類別門檻；前端頁面首次載入、Web Vitals 與 AI 單次成本門檻仍未定。

## 已寫回文件

- [[01-需求/角色與權限]]
- [[02-領域需求/商品、組裝與相容性]]
- [[02-領域需求/購物車、訂單、付款與物流]]
- [[02-領域需求/客服與AI功能]]
- [[03-架構/AI應用詳細設計]]
- [[03-架構/AI測試與評估規格]]
- [[03-架構/系統架構]]
- [[03-架構/非功能需求]]
- [[03-架構/測試策略]]
- [[05-規劃/40天開發計畫]]
- [[05-規劃/未完成項目追蹤表]]
- [[05-規劃/需求追蹤矩陣]]

## 追蹤結果

- 可直接完成：`CS-05`、`AI-01`、`TECH-03`、`TECH-05`、`QA-05`。
- 保持進行中：`PM-05`、`PM-08`、`DOM-10`、`DOM-12`、`DOM-13`、`AI-02`、`AI-05`、`AI-09`、`TECH-06`、`TECH-07`、`QA-01`、`QA-03`、`QA-07`。
- 仍待後續決策：Provider Profile 精確上下限、備援分配、整合會議細節、AI 語言分布與評估責任人。
