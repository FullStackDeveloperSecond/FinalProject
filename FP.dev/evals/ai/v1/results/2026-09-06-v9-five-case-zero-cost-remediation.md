# product-search-v9 五案零成本修正報告

## 結論

2026-09-06 的 `product-search-v8 + support-v2` 三輪 Release baseline 在商品搜尋的五個案例暴露語意分類、精確規格與既有零件確認階段的缺口。本輪以既有 Adapter、Runner 與 Dataset 完成最小修正，沒有呼叫 OpenAI，Token 與費用均為 0。

本輪只證明資料契約、評分器與本機程式路徑一致；尚未證明 `product-search-v9 + support-v3` 的真實模型品質。下一步必須先取得獨立費用授權，再執行小型 Live Smoke 與人工覆核。

## 基準失敗與根因

| 案例 | v8 三輪證據 | 根因與修正 |
|---|---|---|
| `SEARCH-CREATOR-014` | 三輪均能辨識剪輯用途與 2TB，但必要規格未精確表達 | Dataset 補為 `STORAGE_CAPACITY_GB gte 2048GB` 與 `STORAGE_INTERFACE eq SSD` |
| `SEARCH-CREATOR-015` | 三輪均輸出 `CustomBuild` | 既有期待誤標為 `Prebuilt`，依顧客要求自組清單改正為 `CustomBuild` |
| `SEARCH-NOVICE-020` | 三輪均提出 AM5 CPU 既有零件與 Wi‑Fi 偏好，但誤問整台電腦用途 | 單一主機板需求應停在既有零件確認；Runner 精確評分 `proposedExistingParts` 與 Wi‑Fi 偏好，不把提案誤當推薦 |
| `SEARCH-NOVICE-021` | 三輪均辨識單一 2TB Storage 與速度偏好 | Dataset 移除虛構 `General` 用途，補上精確容量與偏好期待 |
| `SEARCH-NOVICE-023` | 三輪未把「遊戲滑鼠」映射為 Gaming | Prompt 新增明確商品標籤對用途的通用規則，不硬編案例字串 |

## 版本與泛化保護

- 商品 Prompt：`product-search-v9`。
- 客服 Prompt：`support-v3`，本輪未變更其回答規格。
- Dataset：`zh-TW-v1.0.6-draft`，總數仍為 120，分割仍為 development 72／release 36／challenge 12。
- Grader：`deterministic-v1.1.5`，新增既有零件提案與確認階段檢查。
- Fixture：維持 `v1.0.4`，沒有修改候選商品或政策事實。
- development 的 `SEARCH-NOVICE-009` 增加「已有 DDR5、只買主機板」泛化案例。
- challenge 的 `SEARCH-NOVICE-030` 增加「FPS 專用滑鼠」同義語案例；此案例只供獨立觀測，不能用來回頭調整 Prompt。

## TDD 與驗證

- RED：Prompt 通用規則／版本測試與既有零件確認評分測試先按預期失敗。
- GREEN：聚焦測試 7／7。
- Application AI：150／150。
- 受影響 Infrastructure Adapter／Runner：45／45。
- Solution Build：0 warning／0 error；四個受影響 C# 檔案通過 focused Format。
- Dataset 來源同步、Schema、隱私掃描：120／120；分割 72／36／12。
- Release Dry Run：36 案中 22 live、14 deterministic-only，三輪共規劃 66 次請求；所有案例定義已核准，`IsLiveReady=true`。
- 泛化案例 Dry Run：development 009 與 challenge 030 各 1 案／1 個規劃請求，皆為 `IsLiveReady=true`；challenge 結果不得用於 Prompt 調整。

以上皆未使用 `--execute`，沒有外部模型請求。公開 API、資料庫 Schema、Migration、模型、逾時、重試、成本門檻與品質門檻均未變更。

## 待完成 Gate

1. 對五個修正商品案例與代表性 `support-v3` 案例執行小型付費 Live Smoke；執行前需列明案例、請求上限與成本停止線並取得授權。
2. 依顧客視角人工覆核新輸出。
3. 小型 Smoke 通過後，才決定是否重跑三輪 66 次 Release baseline；不得以本輪零成本結果取代 Provider-backed 證據。
