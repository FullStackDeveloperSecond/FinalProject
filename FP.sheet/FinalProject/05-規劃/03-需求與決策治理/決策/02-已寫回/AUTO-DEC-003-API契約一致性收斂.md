---
文件狀態: 已寫回
最後更新: 2026-08-13
decision_type: auto
status: applied
applied_at: 2026-08-13
---

# AUTO-DEC-003｜API 契約一致性收斂

## 背景

Endpoint 目錄與錯誤碼目錄存在 52 個未登錄別名及 1 個 `shipment_*` 萬用碼；分頁回應同時使用頁碼語意與 Cursor 型別；退貨核准／退款執行 DTO 名稱不一致；檔案上傳對相同錯誤同時出現 400、422 與多組名稱。

本次依既有商業規則與 API 共通原則收斂，不改變 M/S/O 範圍、角色權限、狀態機或資料保存政策。

## 定案

| 項目 | 統一結果 |
|---|---|
| Endpoint 錯誤碼 | Endpoint 只能引用錯誤碼目錄正式 code；同義別名改用既有 code，`shipment_*` 拆為具名物流錯誤；稽核 `Missing = 0` |
| 一般分頁 | 使用 `pageNumber/pageSize` 與 `PageResult<T>`，提供 `totalCount/totalPages` |
| Cursor 例外 | 只限庫存保留、後台訂單、客服 SLA、統一案件工作台、匯入預覽列、報表明細列，使用 `CursorPage<T>` |
| 退貨核准 DTO | `ApproveReturnRequest`，回傳更新後 `ReturnRequestDto` |
| 退款執行 DTO | `ExecuteRefundRequest`，使用 `Idempotency-Key` Header，回傳 `RefundDto` |
| 上傳數量超限 | `file_count_exceeded`／400 |
| 上傳大小超限 | `file_size_exceeded`／413 |
| 格式、MIME、簽章不符 | `file_format_invalid`／415 |
| 確認惡意內容 | `file_malware_detected`／422 |
| 掃描不可用 | `file_scan_unavailable`／503 |
| 圖片內容不可安全處理 | `image_processing_failed` 或 `image_metadata_incomplete`／422 |

## 影響文件

- [[03-架構/02-API與前端契約/API Endpoint目錄]]
- [[03-架構/02-API與前端契約/API錯誤碼目錄]]
- [[03-架構/02-API與前端契約/API共通規範]]
- [[03-架構/02-API與前端契約/API DTO與Schema契約]]
- [[03-架構/04-安全與檔案/檔案與圖片儲存設計]]
- [[03-架構/07-領域設計/統一案件工作台設計]]
- [[02-領域需求/90-驗收規格/商品組裝客服與報表驗收規格]]
- [[05-規劃/01-時程與進度/未完成項目追蹤表]]

## 驗收

- Endpoint 目錄所有 snake_case 錯誤碼均能在錯誤碼目錄找到，缺漏為 0。
- 不存在 `PagedResult<T>`、`ApproveRefundRequest`、`attachment_*` 或 `shipment_*` 的現行契約引用。
- 一般頁碼與六類 Cursor 例外可以從共通規範追溯到 Endpoint／DTO。
