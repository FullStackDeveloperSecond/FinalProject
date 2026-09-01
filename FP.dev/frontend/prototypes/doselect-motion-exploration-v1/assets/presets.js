/* 三套 preset 的原型鏡像。
   數值與 shared/src/motion/presets.ts 一致；原型不經 build step，
   因此這裡以純 JS 重述同一組數值，方便直接在瀏覽器比較。
   若正式檔案調整數值，這裡要一起更新（README 有註明）。 */

export const PRESETS = {
  gentle: {
    id: 'gentle',
    label: 'A — Gentle Guidance',
    tagline: '柔和、低干擾',
    description: '新手友善。不使用 overshoot，位移小、節奏平穩，適合需要專心閱讀的資訊型頁面與表單流程。',
    reveal: { duration: 0.34, ease: 'power2.out', y: 10, scaleFrom: 1 },
    stagger: { duration: 0.32, ease: 'power2.out', y: 12, scaleFrom: 0.98, each: 0.045, maxItems: 12 },
    panel: { duration: 0.42, ease: 'power2.out', x: 14, scaleFrom: 1, leaveDuration: 0.24, leaveEase: 'power1.inOut' },
    feedback: { duration: 0.28, ease: 'power1.inOut', scaleTo: 1.02 },
    shake: { duration: 0.22, ease: 'power2.out', x: 5, repeat: 1 }
  },
  donggu: {
    id: 'donggu',
    label: 'B — Donggu Friendly',
    tagline: '親切活潑',
    description: '帶受控 overshoot，用在品牌、導購與「完成」狀態。錯誤與危險操作刻意退回中性曲線，不使用彈跳。',
    reveal: { duration: 0.42, ease: 'back.out(1.25)', y: 12, scaleFrom: 0.96 },
    stagger: { duration: 0.46, ease: 'back.out(1.25)', y: 14, scaleFrom: 0.94, each: 0.055, maxItems: 12 },
    panel: { duration: 0.48, ease: 'back.out(1.1)', x: 16, scaleFrom: 0.98, leaveDuration: 0.26, leaveEase: 'power2.inOut' },
    feedback: { duration: 0.5, ease: 'elastic.out(1, 0.55)', scaleTo: 1.06 },
    shake: { duration: 0.22, ease: 'power2.out', x: 5, repeat: 1 }
  },
  crisp: {
    id: 'crisp',
    label: 'C — Crisp Tech',
    tagline: '快速精準',
    description: '方向明確、幾乎不使用 bounce。為高資訊密度的管理介面設計；案件面板刻意保留較長時間以符合「稍微放慢」的要求。',
    reveal: { duration: 0.2, ease: 'power3.out', y: 6, scaleFrom: 1 },
    stagger: { duration: 0.18, ease: 'power3.out', y: 8, scaleFrom: 1, each: 0.022, maxItems: 16 },
    panel: { duration: 0.38, ease: 'power3.out', x: 10, scaleFrom: 1, leaveDuration: 0.2, leaveEase: 'power3.in' },
    feedback: { duration: 0.16, ease: 'power3.out', scaleTo: 1.03 },
    shake: { duration: 0.16, ease: 'power3.out', x: 4, repeat: 1 }
  }
};

export const PRESET_IDS = ['gentle', 'donggu', 'crisp'];

/** 十五個代表情境：八個前台、七個後台。 */
export const SCENARIOS = [
  { id: 'home-hero', side: 'store', kind: 'reveal-then-stagger', title: '① 首頁 Hero 與三個入口', note: '標題淡入後，三個導購入口分批出現。' },
  { id: 'product-grid', side: 'store', kind: 'stagger', title: '② 商品列表卡片進場', note: '首次載入時卡片分批出現；超過上限的直接顯示。', items: 8 },
  { id: 'filter-update', side: 'store', kind: 'stagger', title: '③ 商品篩選結果更新', note: '換條件後只重播結果區，不重播整頁。', items: 6, replayLabel: '套用篩選' },
  { id: 'cart-qty', side: 'store', kind: 'feedback', title: '④ 購物車項目增減回饋', note: '金額變動給一次性脈衝；數字本身仍是主要提示。' },
  { id: 'case-panel', side: 'store', kind: 'panel', title: '⑤ 客服案件列表開啟詳細', note: '同層 2/5 : 3/5，詳細不覆蓋列表，只由右上角關閉鈕收起。' },
  { id: 'thread', side: 'store', kind: 'stagger', title: '⑥ 長對話載入及回覆區', note: '往來訊息分批出現；超過上限直接顯示，長對話不會等太久。', items: 12 },
  { id: 'return-steps', side: 'store', kind: 'reveal-then-stagger', title: '⑦ 退貨申請步驟', note: '步驟區塊逐段展開，送出成功給一次回饋。' },
  { id: 'donggu-empty', side: 'store', kind: 'reveal', title: '⑧ Donggu 提示／空狀態', note: 'Donggu 只做一次性出場，沒有無限漂浮。' },
  { id: 'admin-sidebar', side: 'admin', kind: 'sidebar', title: '⑨ 後台側欄導覽切換', note: '當前項指示器移動，不重繪整個側欄。' },
  { id: 'admin-dashboard', side: 'admin', kind: 'stagger', title: '⑩ 儀表板入口卡', note: '入口卡分批出現；卡片數量改變時才重播。', items: 7 },
  { id: 'admin-filter', side: 'admin', kind: 'stagger', title: '⑪ 表格篩選', note: '只重播列，不對每一格建立獨立 timeline。', items: 10 },
  { id: 'admin-case-panel', side: 'admin', kind: 'panel', title: '⑫ 後台案件詳細開啟／關閉', note: '與前台相同的同層面板規則。' },
  { id: 'admin-claim', side: 'admin', kind: 'feedback', title: '⑬ 承接與回覆成功', note: '一次性回饋；disabled／送出中不觸發。' },
  { id: 'admin-conflict', side: 'admin', kind: 'shake', title: '⑭ 錯誤／RowVersion 衝突', note: '一次水平提示，不持續抖動；焦點與文字才是主要提示。' },
  { id: 'admin-return-status', side: 'admin', kind: 'feedback', title: '⑮ 退貨審核狀態變化', note: '狀態徽章更新給一次脈衝，徽章文字同時改變。' }
];
