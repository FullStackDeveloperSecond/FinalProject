# DoSelect 懂選｜GSAP 動態方案 A／B／C 比較原型

本資料夾是 `feature/gsap-motion-exploration-v1` 的比較素材。
**三套方案在 Codex 一次核對前都不刪除。**

## 怎麼開

```bash
node FP.dev/frontend/prototypes/doselect-motion-exploration-v1/server.js
```

然後開 <http://localhost:8937/prototypes/doselect-motion-exploration-v1/index.html>。

伺服器的 ROOT 設在 `FP.dev/frontend/`，因此頁面直接以
`/customer-web/node_modules/gsap/index.js` 載入 **npm 安裝、版本固定 3.15.0 的同一份 GSAP**。
不走 CDN、不用私人 registry、不需要授權 token。

## 頁面上的四個控制項

| 控制項 | 作用 |
|---|---|
| `A 柔和` / `B 親切` / `C 俐落` | 切換方案，全部十五個情境立即以同一組操作重播 |
| `強制 reduced motion` | 不改系統設定即可對照 reduced 行為，方便截圖 |
| `慢速檢視` | 把 GSAP 全域 `timeScale` 降到 0.2，逐格比較三套差異。**只影響本原型，不改任何 preset 數值** |
| `全部重播` | 重播所有情境（此模式刻意不搶焦點，避免捲動被拉走） |

`window.__motionLab` 另外提供 `gsapVersion`、`liveTweens()`、`revertAll()`，
可在 console 直接檢查是否有殘留 tween。

## 十五個代表情境

前台 8：① 首頁 Hero 與三個入口、② 商品列表卡片進場、③ 商品篩選結果更新、
④ 購物車項目增減回饋、⑤ 客服案件列表開啟詳細、⑥ 長對話載入及回覆區、
⑦ 退貨申請步驟、⑧ Donggu 提示／空狀態。

後台 7：⑨ 側欄導覽切換、⑩ 儀表板入口卡、⑪ 表格篩選、⑫ 案件詳細開啟／關閉、
⑬ 承接與回覆成功、⑭ 錯誤／RowVersion 衝突、⑮ 退貨審核狀態變化。

## 三套方案共同遵守的限制

- 只動 `transform`（x／y／scale）與 `opacity`；不動 width、height、top、left、margin、box-shadow、filter。
- 沒有任何 `repeat: -1`；Donggu 不做無限漂浮。
- 不使用 ScrollSmoother，本輪也沒有引入任何 GSAP plugin（產物只含 gsap-core + CSSPlugin）。
- 進場一律 `gsap.from()`：起始狀態由執行期寫入，CSS 不預先把內容設成 `opacity: 0`，
  JS／GSAP 載入失敗時內容仍然看得到。
- reduced motion 時**完全不建立 tween**，不是把動畫放慢；位移、縮放、彈跳與 stagger 全部消失。
- 表單錯誤只做一次水平提示並把焦點移到錯誤欄位；錯誤文字與 ARIA 才是主要提示。
- disabled 或送出中的控制項不會有任何回饋動畫（情境 ④ ⑬ ⑮ 直接示範）。
- 案件面板維持同層 2/5 : 3/5，只改 `opacity` 與 `x`，不改欄寬，列表全程不會被遮住；
  窄畫面上下堆疊，不轉成 modal／drawer；只由右上角關閉鈕收起。
- 長列表只有前 N 筆進場，其餘直接顯示（情境 ⑥ 直接示範）。

## 與正式程式的關係

`assets/presets.js` 是 `FP.dev/frontend/shared/src/motion/presets.ts` 的**數值鏡像**。
原型不經 build step，所以用純 JS 重述同一組數值。
**正式檔案調整數值時，這裡要一起更新。**

正式 App 端另有 dev-only 的方案切換：在 dev 模式下網址加 `?motion=gentle|donggu|crisp`，
或用右下角的切換器。該切換器的 import 被 `import.meta.env.DEV` 包住，
production build 中整個元件與其字串都不存在（有自動化測試掃描 `dist/` 驗證）。

## 資料與安全

本頁所有文字皆為示意假資料。不連線任何 API、不含帳號、token 或機密內容，
重新整理即回復初始狀態。

## 授權

GSAP 為 GreenSock **Standard「no charge」License**，**不是 MIT**：
<https://gsap.com/standard-license>
Copyright (c) 2008-2026, GreenSock. All rights reserved.

本 repository 目前沒有第三方授權文件慣例（沒有 `LICENSE`／`NOTICE`／`THIRD-PARTY` 檔，
README 也沒有授權章節），因此本輪**不自行建立授權治理架構**，
只在這裡與交付報告中記錄事實，待組長決定要不要正式建檔。
