---
type: knowledge
title: Structured Outputs 與 JSON Schema
aliases:
  - OpenAI Structured Outputs
  - JSON Schema輸出
  - 結構化輸出
tags:
  - 知識點
  - AI
  - OpenAI
  - JSON Schema
  - API契約
created_at: 2026-08-13
related:
  - "[[03-架構/06-AI設計/AI應用詳細設計]]"
  - "[[03-架構/06-AI設計/AI測試與評估規格]]"
  - "[[知識點/03-API契約與可觀測性/DTO與API Schema]]"
---

# Structured Outputs 與 JSON Schema

## 它解決什麼

OpenAI Structured Outputs 讓模型輸出符合指定的 JSON Schema，例如固定欄位、型別、Enum 與必要項目。它比只提示「請輸出 JSON」更可靠，也能讓拒絕等結果被程式化處理。

```text
自然語言
→ 模型依版本化 JSON Schema 輸出
→ 後端解析與 Schema 檢查
→ 後端商業驗證
→ 才能查資料或執行後續流程
```

## Schema 正確不等於內容正確

符合 Schema 只證明形狀合法，不代表商品存在、價格正確、使用者有權限或條件彼此不衝突。後端仍需：

- 驗證 Enum、長度、數量與白名單語意鍵。
- 把 PublicId 對應至可見資源。
- 檢查預算、規格、單位及條件衝突。
- 缺少必要資訊時補問，不自行猜測。
- 對拒絕、截斷、逾時與無效結果安全降級。

## 版本與評估

Prompt、Schema 與工具契約都應有版本。改欄位、Enum 或必要性時，保存實際版本並以代表性評估集驗證，不只測一兩個成功案例。嚴格 Schema 只支援 JSON Schema 的一部分，設計前需核對所用模型與 API 文件。

> [!note] 專案決策邊界
> 自然語言商品需求使用 Structured Outputs 產生版本化 SearchIntent；後端通過白名單及商業驗證後才查 SQL。模型無法決定時最多補問 2 題，正式 Schema 上限見 [[03-架構/06-AI設計/AI應用詳細設計]]。

## 參考資料

- [OpenAI Docs：Structured model outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [JSON Schema](https://json-schema.org/)
- [[03-架構/06-AI設計/AI測試與評估規格]]
