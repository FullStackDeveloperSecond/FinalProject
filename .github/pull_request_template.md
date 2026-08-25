## 本次修改

請簡短列出這個 PR 修改了什麼，以及修改原因。

- （請填寫）

---

## 對應 Issue

請填寫對應的 Issue，例如 `Closes #12` 或 `Related to #12`。

- （請填寫）

---

## 修改範圍

- [ ] 前端（Customer Web）
- [ ] 前端（Admin Web）
- [ ] 後端 / API
- [ ] 資料庫 / EF Core Migration
- [ ] AI / Evaluation
- [ ] CI / 設定 / 文件
- [ ] 其他：

---

## 測試方式

請寫清楚 Reviewer 要怎麼驗證，不要只寫「已測試」或「可以執行」。

1. （請填寫）
2. （請填寫）
3. （請填寫）

測試結果：

- [ ] 已完成相關自動化測試
- [ ] 已完成手動測試
- [ ] 不需要測試，原因：

---

## 資料庫 / API 相容性

- [ ] 沒有修改資料庫 Schema 或 Migration
- [ ] 有修改資料庫 Schema 或 Migration，說明：
- [ ] 沒有修改 API Contract
- [ ] 有修改 API Contract，說明：

---

## 影響範圍與 Reviewer 注意事項

請列出受影響的模組、已知問題、尚未完成項目或部署注意事項。

- （請填寫）

---

## 安全與供應鏈

- [ ] Gitleaks Repository／Production Build Artifact 掃描已通過，或失敗原因已在下方說明
- [ ] 本次沒有新增直接 NuGet／npm Package
- [ ] 本次有新增直接 Package，已提供正式 Registry、官方來源、精確版本與用途
- [ ] 本次沒有新增或擴大 `.gitleaksignore`、Allowlist 或 `gitleaks:allow`
- [ ] 本次有最小 Secret Scan 忽略規則，已說明合成值、範圍與不可改寫原因

Package／Secret Scan 證據或例外說明：

- （請填寫；不適用時填「不適用」）

---

## 提交前確認

- [ ] PR 標題清楚描述修改內容
- [ ] 已確認目標分支正確
- [ ] 沒有提交密碼、Token、連線字串或其他敏感資料
- [ ] CI 已通過，或已在上方說明失敗原因
