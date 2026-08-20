---
type: knowledge
title: Embeddings 與向量檢索
aliases:
  - Vector Embeddings
  - Semantic Search
  - 向量搜尋
tags:
  - 知識點
  - AI
  - OpenAI
  - Embeddings
  - 搜尋
created_at: 2026-08-13
related:
  - "[[03-架構/06-AI設計/AI應用詳細設計]]"
  - "[[03-架構/06-AI設計/AI測試與評估規格]]"
  - "[[知識點/08-AI與智慧搜尋/Structured Outputs與JSON Schema]]"
---

# Embeddings 與向量檢索

## Embedding 是什麼

Embedding 把文字轉成數值向量，使語意相近的內容在向量空間中較接近。常見用途包含語意搜尋、分群、推薦、異常偵測與分類。

向量檢索通常包含：

```text
文件切片與清理
→ 產生 Embedding
→ 保存向量與來源 Metadata
→ 查詢也產生 Embedding
→ 依相似度取 Top K
→ 再做權限、版本與商業篩選
```

## 它不是權威資料來源

相似度只表示向量接近，不保證事實正確、政策最新或商品符合硬條件。價格、庫存、Socket、瓦數、權限及訂單狀態仍應由結構化資料與確定性規則判斷。

若文件含私人資料，檢索前後都要套用授權範圍；不能先跨會員搜尋，再希望模型自行忽略不該看的結果。

## 何時值得導入

只有關鍵字／結構化篩選無法達到可量測的 Recall、Precision 或人工處理成本目標時，才值得加入向量儲存、重建、版本、刪除、成本與監控負擔。導入前需建立代表性查詢集，比較既有基準與向量方案，而非因為「AI 系統通常有向量資料庫」就加入。

> [!warning] 專案決策邊界
> 本專案第一版不使用 Embeddings 或向量資料庫。商品搜尋先採 Structured Outputs 轉成白名單結構化條件，再以 SQL 與確定性規則查詢；只有評估證明有需要才另立決策。

## 參考資料

- [OpenAI Docs：Vector embeddings](https://developers.openai.com/api/docs/guides/embeddings)
- [[03-架構/06-AI設計/AI測試與評估規格]]
