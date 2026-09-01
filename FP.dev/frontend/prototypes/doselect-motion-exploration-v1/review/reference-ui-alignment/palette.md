# 參考圖取樣色盤

取樣方式：`System.Drawing.Bitmap.GetPixel` 逐像素讀取六張原始參考圖，
平面色區取 9×9 視窗的眾數，避免抗鋸齒與文字像素。原始檔唯讀，未修改。

## 一、取樣值 → token

| 取樣值 | 來源 | 角色 | token |
|---|---|---|---|
| `#FEFEFE` | 01 header 底 | 白色表面 | `--color-surface: #FFFFFF` |
| `#FEFFFF` | 01 hero 下方內容區 | 頁面畫布 | `--color-page: #F7FAFE` |
| `#F5F9FE` | 01 hero 卡片底 | 分區 | `--brand-blue-100` |
| `#F3F7FC` | 01 底部信任列 | 分區 | `--brand-blue-100: #F1F6FD` |
| `#F2F7FC` | 04 表頭列 | 表頭 | `--color-section` |
| `#F1F6FD` | 03 資訊提示條 | 提示區 | `--color-section` |
| `#E9F0FC` | 01 三選一卡片藍格 | 淡藍色塊 | `--brand-blue-200: #ECF4FE` |
| `#ECF4FE` | 04 側欄當前項底 | 當前項 | `--color-primary-soft` |
| `#EEF4FE` | 05 客服訊息泡泡 | 訊息泡泡 | `--color-surface-strong` |
| `#F5F8FE` | 05 案件列表選取列 | 選取列 | `--color-primary-soft` |
| `#0665F4` | 03「前往結帳」實心鈕 | 主操作 | `--brand-blue-600: #0B66E8` |
| `#0271E7` | 04「＋新增商品」實心鈕 | 主操作 | 同上 |
| `#0168FA` | 06 長條圖填色 | 圖表 | `--chart-1` |
| `#0067FB` | 06 甜甜圈第 1 段 | 圖表 | `--chart-1` |
| `#0A68EC` | 03 步驟指示圓 | 流程 | `--color-primary` |
| `#056AE4` | 04 分頁當前頁 | 當前項 | `--color-primary` |
| `#1373F7` | 01 當前導覽底線 | 字標／底線 | `--brand-blue-500` |
| `#126DF1` | 01 商品價格 | 重點數字 | `--color-primary` |
| `#001C46` | 01 hero 主標 | 深海軍藍內文 | `--brand-ink-900` / `--color-ink` |
| `#74C1FD` | 06 甜甜圈第 2 段 | 圖表淺藍 | `--brand-blue-400` → `--chart-2: #7CC4F8` |
| `#52C998` | 06 甜甜圈第 3 段 | 圖表綠 | `--green-300`；`--chart-3: #0F9B7A`（提深以過 3:1） |
| `#FEAC37` | 06 甜甜圈第 4 段／低庫存 | 圖表橘 | `--amber-400: #F5A524` → `--chart-4` |
| `#E8EDF5` | 04 進度條軌 | 軌道 | `--chart-track` |
| `#DDF6EE` | 04「上架中」徽章底 | success 底 | `--color-success-bg` |
| `#DEF7F3` | 04「啟用」徽章底 | success 底 | 同上 |
| `#E6F8F2` | 03「相容」徽章底 | info／success 底 | `--color-info-bg: #E4F2F4` |
| `#E4F2F4` | 01 青綠入口卡 | 青綠分區 | `--brand-teal-100` |
| `#E9F9F7` | 05「進行中」徽章底 | 狀態底 | `--color-info-bg` |
| `#ECEEFD` | 01 第三張入口卡 | 紫色裝飾 | `--brand-violet-100` |
| `#E6C8D4` | 品牌指定 | 輔助色 | `--brand-pink-500` / `--color-brand-pink` |

## 二、最終語意色盤

```css
--color-page:          #F7FAFE   /* 近白冷色畫布，最大面積 */
--color-surface:       #FFFFFF   /* 卡片／表格／header／sidebar */
--color-section:       #F1F6FD   /* 分區底、表頭、提示區 */
--color-surface-strong:#ECFAFE→#ECF4FE  /* 淡藍色塊、選取列、訊息泡泡 */
--color-primary:       #0B66E8   /* 高飽和亮藍：操作與資訊焦點 */
--color-primary-hover: #0A55C4   /* 較深一階 */
--color-ink:           #001C46   /* 深海軍藍：文字 */
--color-border:        #7D90B4   /* 表單控制項，白底 3.22:1 */
--color-border-soft:   #E2E9F3   /* 表格／卡片細邊 */
--color-border-line:   #C3D2E6   /* header／sidebar 分隔線 */
--color-brand-pink:    #E6C8D4   /* 輔助：光暈、局部背景、少量漸層 */
--color-accent-teal:   #01B6B3   /* Logo 青綠，小面積點綴 */
```

語意色（保留原意，未粉紅化、未藍色化）：

```css
--color-success: #0D7050  on #DDF6EE   /* 5.36:1 */
--color-warning: #8A5A12  on #FDEFD9   /* 5.22:1 */
--color-danger:  #C02434  on #FDE8EA   /* 5.05:1 */
--color-info:    #04716F  on #E4F2F4   /* 5.09:1 */
```

圖表系列（圖例一律同時提供文字標籤與數值，顏色不是唯一辨識通道）：

```css
--chart-1: #0B66E8  藍     --chart-4: #F5A524  黃橘
--chart-2: #7CC4F8  淺藍   --chart-5: #D3789F  粉（#E6C8D4 的可讀化版本）
--chart-3: #0F9B7A  青綠   --chart-6: #7B5CF0  紫
```

相鄰系列亮度比：2.72 / 1.85 / 1.72 / 1.55 / 1.66（皆 ≥ 1.35，單色列印仍可分辨）。

## 三、對比度實測（WCAG 2.1）

### 文字 ≥ 4.5:1

| 前景 | 背景 | 比值 |
|---|---|---|
| `#001C46` 內文 | `#FFFFFF` | **16.74** |
| `#001C46` | `#F7FAFE` 畫布 | **15.99** |
| `#001C46` | `#F1F6FD` 分區 | **15.42** |
| `#001C46` | `#ECF4FE` 淡藍塊 | **15.10** |
| `#001C46` | `#E6C8D4` 粉區 | **10.82** |
| `#0B66E8` 連結／重點數字 | `#FFFFFF` | **5.15** |
| `#0B66E8` | `#F7FAFE` | **4.92** |
| `#0B66E8` | `#F1F6FD` | **4.74** |
| `#0B66E8` | `#ECF4FE` | **4.64** |
| `#FFFFFF` | `#0B66E8` 主按鈕 | **5.15** |
| `#FFFFFF` | `#0A55C4` hover | **6.75** |
| `#43536F` 次要文字 | `#FFFFFF` | **7.76** |
| `#43536F` | `#F1F6FD` | **7.15** |
| `#5A6B88` 第三階 | `#FFFFFF` | **5.39** |
| `#5A6B88` | `#ECF4FE` | **4.86** |

### 非文字 ≥ 3:1

| 元素 | 比值 |
|---|---|
| `#7D90B4` 表單描邊 on `#FFFFFF` | **3.22** |
| `#7D90B4` on `#F7FAFE` | **3.08** |
| `#0B66E8` focus ring on `#FFFFFF` | **5.15** |
| `#0B66E8` focus ring on `#E6C8D4` | **3.33** |
| `#0B66E8` 圖示 on `#ECF4FE` 底板 | **4.64** |

### 明確禁止

| 組合 | 比值 | 說明 |
|---|---|---|
| `#FFFFFF` on `#E6C8D4` | **1.55** | 粉底不得使用白字 |
| `#FFFFFF` on `#ECF4FE` | **1.11** | 淡藍區塊不得使用白字 |
| `#FFFFFF` on `#F1F6FD` | **1.06** | 淡藍分區不得使用白字 |

以上三條在 `brand-system.spec.ts` 的「3. 淡藍／淡青只負責分區」與
「6. #E6C8D4 是輔助色」中都有對應的自動化護欄。
