---
batch_id: DEC-BATCH-051
status: applied
decision_date: 2026-09-04
decision_ids:
  - DEC-P372
  - DEC-P373
  - DEC-P374
  - DEC-P375
  - DEC-P376
  - DEC-P377
---

# DEC-BATCH-051｜AI 修正版 Smoke 結果與後續 Gate 定版

## 決策

| ID | 已確認內容 |
|---|---|
| DEC-P372 | 組長授權先建立本機可追溯 Commit，再以 6 個核准 Release 案例、各 1 輪與 US$0.05 停止線執行修正版付費 Smoke；不 Push，不擴張成完整 baseline。 |
| DEC-P373 | Run `20260904T090934Z` 綁定 Commit `f195c453`，6／6 案執行完成，規劃／實際 9／11 次模型請求、成本 US$0.010242，未觸發成本停止；Schema 83.33%、Intent 50%、Deterministic 66.67%，商品／客服 P95 18,742／2,562 ms，正式 Verdict 為 `FAIL`。 |
| DEC-P374 | T2 執行證據完整：模型請求前已有 metadata／空 JSONL／checkpoint，每案即時追加 Token、成本、延遲與含 retry 請求數；最終 manifest 綁定 revision、環境、產物大小與 SHA-256。所有輸入為合成／去識別資料，沒有保存 Secret 或真實個資。 |
| DEC-P375 | `SUPPORT-POLICY-010` 與 `SUPPORT-SECURITY-016` 的 deterministic／引用／安全拒絕均通過；Codex 初步內容檢查不可取代正式人工覆核，6 案 Human Verdict 維持 pending。 |
| DEC-P376 | 依既有核准資料集直接修正兩個無歧義缺口：商品 Prompt 升為 `product-search-v3`，以安全上限＋補問處理衝突預算，並明定現成／套裝／買整台與配／組／組裝／預算型遊戲主機的既有分類語意；不修改 Release expected value。 |
| DEC-P377 | `SEARCH-CREATOR-013` 的 GPU／RAM 候選事實與商品 5 秒延遲處理屬核心未決；在組長確認精確 Fixture 與兩階段模型方向前不得自行編造、放寬門檻或再次付費執行，AI-09 維持進行中。 |

## Lowest-Cost Analysis

1. 維持現況：Smoke 已失敗，不能進完整 baseline，未採用。
2. 只補文件：不能修正 invalid output 或 intent 漂移，未採用。
3. 放寬門檻：會隱藏既有需求與 18,742 ms P95，未採用。
4. 延伸既有 Prompt 與 focused tests：不新增套件、服務、Schema 或外部費用，可處理兩個已確認契約缺口，採用。
5. Fixture 精確規格與模型流程調整：需要產品／架構選擇，暫停等待組長決策。

## Business Impact

| 面向 | 影響 |
|---|---|
| 受影響角色 | 電腦組裝新手、專業創作者、展示操作人員、AI-09 發布決策者 |
| 現況風險 | 商品型態誤判、衝突預算直接降級、缺少可解釋事實，以及商品回應超過既定 5 秒 |
| 預期結果 | 先消除既有契約漂移；其餘核心選擇有明確 Owner 與停止線 |
| 建置／持續成本 | Prompt／測試沿用既有能力；後續付費或流程變更另行核准 |
| 信心 | 執行證據高；兩項 Prompt 修正的模型效果仍需新付費 smoke 才能確認 |
| 成功指標 | focused tests、資料／dry run 與格式 Gate 通過；任何新 live run 有精確 revision 與 T2 證據 |
| 停止／回復條件 | 若需改公開 API、模型、5 秒門檻、候選事實或移除推薦理由，停止並重新決策 |

## 證據

- `FP.dev/evals/ai/v1/results/2026-09-04-remediation-smoke-f195c453.md`
- 本機忽略產物：`FP.dev/.run/ai-evals/20260904T090934Z/`
- 修正後 focused tests：20／20；未付費重跑。
