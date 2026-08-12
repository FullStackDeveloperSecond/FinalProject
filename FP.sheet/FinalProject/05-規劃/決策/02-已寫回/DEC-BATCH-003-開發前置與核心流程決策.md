---
type: decision-record
batch_id: DEC-BATCH-003
title: 開發前置與核心流程決策
status: applied
created_at: 2026-08-10
submitted_at: 2026-08-11
compiled_at: 2026-08-11
applied_at: 2026-08-11
decision_count: 30
source: "[[05-規劃/決策/00-互動中/DEC-BATCH-003-開發前置與核心流程決策]]"
decision_ids:
  - DEC-P41
  - DEC-P42
  - DEC-P43
  - DEC-P44
  - DEC-P45
  - DEC-P46
  - DEC-P47
  - DEC-P48
  - DEC-P49
  - DEC-P50
  - DEC-P51
  - DEC-P52
  - DEC-P53
  - DEC-P54
  - DEC-P55
  - DEC-P56
  - DEC-P57
  - DEC-P58
  - DEC-P59
  - DEC-P60
  - DEC-P61
  - DEC-P62
  - DEC-P63
  - DEC-P64
  - DEC-P65
  - DEC-P66
  - DEC-P67
  - DEC-P68
  - DEC-P69
  - DEC-P70
---

# DEC-BATCH-003｜開發前置與核心流程決策

本批 30 題均已選擇方案，沒有空白答案或自主輸入覆寫。批次內一致性檢查已通過，並已於 2026-08-11 寫回正式文件與追蹤表。

## 決策

| ID | 最終決策 |
|---|---|
| DEC-P41 | 採功能主責＋備援的混合式分工；組長負責架構、共用能力、整合與 Code Review，並承接小型模組。 |
| DEC-P42 | 40 天分五階段，Day 35 起功能凍結，只修 Bug、整理資料、簡報及彩排。 |
| DEC-P43 | 只有 M 功能可建置、Migration 可重建、API 契約穩定且核心 Demo 測試通過後，才能啟動 S 功能。 |
| DEC-P44 | 每週進行三次整合，不做每日同步。 |
| DEC-P45 | 採 `main`＋`dev`＋短生命週期工作分支。 |
| DEC-P46 | PR 只需組長核准，並只由組長執行合併。 |
| DEC-P47 | Vue 瀏覽器登入採 ASP.NET Core Identity 與 HttpOnly Cookie。 |
| DEC-P48 | 會員與管理員使用獨立 Cookie Scheme。 |
| DEC-P49 | 會員 Session 閒置 8 小時滑動續期、最長 7 天；管理員 Session 絕對 2 小時且不滑動續期。 |
| DEC-P50 | CORS 使用明確 Origin 白名單及 Credentials；狀態變更 API 使用 Antiforgery Header。 |
| DEC-P51 | Email 驗證 24 小時、重設密碼 1 小時、訪客驗證碼 10 分鐘、GuestOrderAccessToken 30 分鐘。 |
| DEC-P52 | 會員登入失敗 5 次鎖定 15 分鐘；管理員 5 次鎖定 30 分鐘；人工解鎖必須稽核。 |
| DEC-P53 | 列表 API 採 Page Number；預設 20、最大 100，回傳總筆數與總頁數。 |
| DEC-P54 | API 錯誤使用 Problem Details，加入穩定 `code`、`traceId` 與欄位 `errors`。 |
| DEC-P55 | 建立訂單與退款等關鍵寫入使用 Idempotency-Key 並保存結果 24 小時；回呼以 Provider Event Id 去重。 |
| DEC-P56 | 一般可編輯資料使用 SQL Server `rowversion`，衝突回傳 409；庫存另使用交易與條件更新。 |
| DEC-P57 | 付款失敗或取消後，訂單保留至原付款期限；每次重試建立新的 Payment Attempt。 |
| DEC-P58 | 第一版不建立 CancellationRequest；符合規則直接取消，不符合時只能由管理員以必填原因及稽核執行例外取消。 |
| DEC-P59 | COD 訂單建立 `AwaitingPayment` Payment，超商取貨完成時轉為 `Paid`，未取退回時轉 `Cancelled`。 |
| DEC-P60 | 退款採 OrderRefundStatus 訂單彙總與 RefundTransactionStatus 單次交易兩層模型。 |
| DEC-P61 | 第一版一張訂單只能有一種配送方式與一張主要物流單，所有商品同批出貨。 |
| DEC-P62 | 退貨核准後 7 個日曆天內交寄；管理員可在到期前延長一次 7 天，必須記錄理由並通知。 |
| DEC-P63 | SupportTicket 的 `Resolved` 滿 3 天自動轉 `Closed`；`Closed` 不重開，後續問題建立新案並關聯舊案。 |
| DEC-P64 | SLA 使用量達 80% 時提醒承辦；100% 時通知承辦與客服主管、標記 Overdue 並置頂，不自動轉派或提高優先級。 |
| DEC-P65 | 測試工具採 xUnit、ASP.NET Core Integration Testing、Vitest、Vue Test Utils 與 Playwright。 |
| DEC-P66 | 後端整合測試使用獨立 SQL Server 測試資料庫；Domain／Application 單元測試不連資料庫。 |
| DEC-P67 | PR 執行單元與受影響整合測試；合回 `dev` 後執行核心 Playwright E2E。Migration、權限、金額與庫存變更必須有對應測試。 |
| DEC-P68 | AI 自然語言需求使用 OpenAI Structured Outputs、版本化 JSON Schema 及後端商業驗證；缺漏時追問，不猜測。 |
| DEC-P69 | 第一版商品檢索採 SQL 結構化篩選與確定性規則，不使用向量；評估不足時才考慮 Embedding 混合檢索。 |
| DEC-P70 | 訪客 AI 搜尋每日 10 次；會員搜尋 30 次、AI 客服 20 則；估算累計 $70 警告、$90 保護，保留 Demo Allowlist。 |

## 一致性檢查

- DEC-P44 選擇每週三次整合且不做每日同步，取代 DEC-P44 題目的每日同步建議，不與時程凍結規則衝突。
- DEC-P46 選擇組長唯一核准及合併者；DEC-P67 仍要求自動檢查，兩者分別控制人工作業與 CI Gate，沒有衝突。
- DEC-P47 與 DEC-P50 一併套用：Cookie 驗證必須同時有 Antiforgery 與明確 CORS Origin，不可只寫入其中一項。
- DEC-P63 與既有 DEC-P28 一致：顧客只在 Resolved 後 3 天內重開；滿 3 天自動 Closed，Closed 後改建關聯新案件。

## 已寫回文件

- [[00-專案概述/專案概述]]
- [[01-需求/功能範圍]]
- [[01-需求/核心商業規則]]
- [[02-領域需求/會員、驗證與通知]]
- [[02-領域需求/購物車、訂單、付款與物流]]
- [[02-領域需求/退貨與退款政策]]
- [[02-領域需求/客服與AI功能]]
- [[03-架構/系統架構]]
- [[03-架構/狀態機設計]]
- [[Git協作規範]]
- [[04-展示/Demo流程]]
- [[05-規劃/40天開發計畫]]
- [[03-架構/API共通規範]]
- [[03-架構/測試策略]]
- [[05-規劃/未完成項目追蹤表]]
- [[05-規劃/決策紀錄]]

## 已更新追蹤項目

- 完成：PM-06、PM-07、DES-01～DES-05、DES-12、AI-03、AI-06、QA-02、DEV-01。
- 進一步收斂但尚未完成：PM-05、PM-08、DES-06、DES-09、DES-11、CS-04、AI-02、QA-03。

## 仍需補充但不影響本批已套用決策

- DEC-P41 尚未提供五位成員姓名或代稱與實際主責／備援映射。
- DEC-P44 尚未指定每週三次整合的星期與時間。
- DEC-P67 尚未選定五條核心 Playwright 流程的精確清單。
- DEC-P68 已選定結構化方法，但搜尋需求 JSON Schema 的完整欄位仍需詳細設計。
- DEC-P70 的每日重設時區、IP／瀏覽器識別實作及 Demo Allowlist 帳號仍需詳細設計。

以上項目保留至追蹤表，不自行補值。
