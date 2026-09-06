/* =============================================================================
   DoSelect 懂選 — 視覺預覽假資料與文案
   本檔集中所有 user-facing 文案與示意資料；共用元件不含任何文案。
   全部為虛構資料，不對應真實商品、訂單、會員或案件。
   ============================================================================= */
(function (global) {
  'use strict';

  /* ---- 圖示（線條圖形，統一以 currentColor 上色，不寫死顏色） ---- */
  var ICON = {
    desktop: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="12" y="6" width="24" height="34" rx="3"/><path d="M18 14h12M18 21h12M18 28h7"/><circle cx="30" cy="33" r="2"/></svg>',
    laptop: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="10" y="11" width="28" height="19" rx="2"/><path d="M5 35h38l-3 4H8z"/></svg>',
    monitor: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="6" y="9" width="36" height="24" rx="2"/><path d="M20 39h8M24 33v6"/></svg>',
    keyboard: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="4" y="14" width="40" height="20" rx="3"/><path d="M11 21h2M18 21h2M25 21h2M32 21h5M11 27h2M18 27h12M36 27h1"/></svg>',
    gpu: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="5" y="15" width="34" height="18" rx="2"/><circle cx="16" cy="24" r="4"/><circle cx="29" cy="24" r="4"/><path d="M39 20h4v8h-4"/></svg>',
    storage: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="8" y="10" width="32" height="28" rx="3"/><path d="M14 17h20M14 24h20"/><circle cx="32" cy="31" r="2"/></svg>',
    headset: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M11 30v-6a13 13 0 0 1 26 0v6"/><rect x="7" y="27" width="7" height="11" rx="2.5"/><rect x="34" y="27" width="7" height="11" rx="2.5"/></svg>',
    purpose: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="24" cy="24" r="16"/><circle cx="24" cy="24" r="9"/><circle cx="24" cy="24" r="2.5"/></svg>',
    budget: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="6" y="13" width="36" height="22" rx="3"/><circle cx="24" cy="24" r="5"/><path d="M13 20v8M35 20v8"/></svg>',
    hot: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M24 6s9 8 9 17a9 9 0 0 1-18 0c0-4 2-6 2-6s1 3 3 3c0-6 4-10 4-14z"/><path d="M13 34a11 11 0 0 0 22 0"/></svg>',
    cart: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 8h5l5 21h20l4-14H14"/><circle cx="19" cy="37" r="2.5"/><circle cx="34" cy="37" r="2.5"/></svg>',
    support: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 32V22a16 16 0 0 1 32 0v10"/><rect x="5" y="28" width="8" height="12" rx="3"/><rect x="35" y="28" width="8" height="12" rx="3"/><path d="M24 44h8a4 4 0 0 0 4-4"/></svg>',
    tag: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 24 24 6h18v18L24 42z"/><circle cx="33" cy="15" r="3"/></svg>',
    box: '<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 15 24 6l18 9v18l-18 9-18-9z"/><path d="M6 15l18 9 18-9M24 24v18"/></svg>'
  };

  function money(value) {
    return 'NT$ ' + value.toLocaleString('zh-TW');
  }

  /* ---- 商品 ---- */
  var products = [
    { id: 'PRD-1001', name: '文書輕鬆組 A1', icon: 'desktop', price: 16800, category: '桌上型電腦', purpose: '文書上網', budget: 'under20k', tags: ['免組裝', '內建 Wi-Fi'], forWho: '第一次買電腦、上網與文書為主', stock: 24, hot: true },
    { id: 'PRD-1002', name: '學生筆電 L2', icon: 'laptop', price: 21900, category: '筆記型電腦', purpose: '文書上網', budget: '20to40k', tags: ['輕薄', '長效電池'], forWho: '學生上課、報告、線上會議', stock: 12, hot: true },
    { id: 'PRD-1003', name: '創作繪圖組 C3', icon: 'gpu', price: 46800, category: '桌上型電腦', purpose: '影像創作', budget: '40to80k', tags: ['獨立顯卡', '高色準'], forWho: '修圖、剪片、繪圖創作者', stock: 6, hot: true },
    { id: 'PRD-1004', name: '電競主機 G4', icon: 'desktop', price: 62800, category: '桌上型電腦', purpose: '遊戲娛樂', budget: '40to80k', tags: ['高更新率', '水冷'], forWho: '想順跑 3A 大作的玩家', stock: 3, hot: true },
    { id: 'PRD-1005', name: '27 吋護眼螢幕 M5', icon: 'monitor', price: 6980, category: '螢幕', purpose: '文書上網', budget: 'under20k', tags: ['低藍光', '可升降'], forWho: '長時間看螢幕、想減少疲勞', stock: 40 },
    { id: 'PRD-1006', name: '靜音鍵盤 K6', icon: 'keyboard', price: 1590, category: '周邊配件', purpose: '文書上網', budget: 'under20k', tags: ['靜音', '有線'], forWho: '辦公室與宿舍不想吵到別人', stock: 88 },
    { id: 'PRD-1007', name: '大容量固態硬碟 S7', icon: 'storage', price: 3280, category: '零組件', purpose: '影像創作', budget: 'under20k', tags: ['2TB', '高速讀寫'], forWho: '照片影片存不下、想加速開機', stock: 31 },
    { id: 'PRD-1008', name: '降噪耳機 H8', icon: 'headset', price: 4290, category: '周邊配件', purpose: '遊戲娛樂', budget: 'under20k', tags: ['降噪', '附麥克風'], forWho: '線上開會與遊戲語音', stock: 0 },
    { id: 'PRD-1009', name: '進階創作組 C9', icon: 'gpu', price: 88000, category: '桌上型電腦', purpose: '影像創作', budget: 'over80k', tags: ['專業顯卡', '大記憶體'], forWho: '4K 影片剪輯與 3D 算圖', stock: 2 },
    { id: 'PRD-1010', name: '家用一體機 D10', icon: 'monitor', price: 28800, category: '桌上型電腦', purpose: '文書上網', budget: '20to40k', tags: ['省空間', '一體成型'], forWho: '客廳、櫃檯、不想接很多線', stock: 15 },
    { id: 'PRD-1011', name: '電競螢幕 M11', icon: 'monitor', price: 12800, category: '螢幕', purpose: '遊戲娛樂', budget: 'under20k', tags: ['165Hz', '1ms'], forWho: '重視畫面流暢度的玩家', stock: 9 },
    { id: 'PRD-1012', name: '無線鍵鼠組 K12', icon: 'keyboard', price: 1280, category: '周邊配件', purpose: '文書上網', budget: 'under20k', tags: ['無線', '省電'], forWho: '桌面想少一點線', stock: 54 }
  ];

  var categories = [
    { key: '桌上型電腦', icon: 'desktop', note: '整台組好，插電就能用' },
    { key: '筆記型電腦', icon: 'laptop', note: '帶著走，上課上班都行' },
    { key: '螢幕', icon: 'monitor', note: '看得清楚、眼睛不累' },
    { key: '零組件', icon: 'storage', note: '升級容量與速度' },
    { key: '周邊配件', icon: 'keyboard', note: '鍵盤滑鼠耳機' },
    { key: '組裝服務', icon: 'box', note: '我們幫你裝好測好' }
  ];

  var purposes = [
    { key: '文書上網', label: '文書 / 上網', note: '打字、報告、看影片' },
    { key: '遊戲娛樂', label: '遊戲 / 娛樂', note: '想玩得順、畫面漂亮' },
    { key: '影像創作', label: '影像 / 創作', note: '修圖、剪片、繪圖' }
  ];

  var budgets = [
    { key: 'under20k', label: '2 萬以下' },
    { key: '20to40k', label: '2–4 萬' },
    { key: '40to80k', label: '4–8 萬' },
    { key: 'over80k', label: '8 萬以上' }
  ];

  /* ---- 優惠活動 ---- */
  var promotions = [
    { id: 'PROMO-01', name: '開學季筆電加碼', period: '2026-08-20 ～ 2026-09-15', kind: '滿額折抵', rule: '單筆滿 NT$ 20,000 折 NT$ 1,500', status: 'in-progress', statusText: '進行中', used: 128, quota: 300 },
    { id: 'PROMO-02', name: '周邊配件買二送一', period: '2026-08-25 ～ 2026-09-05', kind: '組合優惠', rule: '同分類任選 3 件，最低價 1 件免費', status: 'in-progress', statusText: '進行中', used: 64, quota: 200 },
    { id: 'PROMO-03', name: '螢幕升級折扣', period: '2026-09-10 ～ 2026-09-30', kind: '折扣券', rule: '指定螢幕 9 折', status: 'waiting', statusText: '待開始', used: 0, quota: 150 },
    { id: 'PROMO-04', name: '夏季清倉', period: '2026-07-01 ～ 2026-08-15', kind: '直接折價', rule: '指定商品折 NT$ 800', status: 'stopped', statusText: '已結束', used: 210, quota: 210 }
  ];

  /* ---- 訂單 ---- */
  var orders = [
    { id: 'SO-260826-0142', date: '2026-08-26', total: 46800, status: 'in-progress', statusText: '待出貨', items: [{ name: '創作繪圖組 C3', qty: 1, price: 46800, icon: 'gpu' }], recipient: '王＊＊', ship: '宅配到府', pay: '信用卡（模擬）' },
    { id: 'SO-260820-0098', date: '2026-08-20', total: 8570, status: 'complete', statusText: '已完成', items: [{ name: '27 吋護眼螢幕 M5', qty: 1, price: 6980, icon: 'monitor' }, { name: '靜音鍵盤 K6', qty: 1, price: 1590, icon: 'keyboard' }], recipient: '王＊＊', ship: '超商取貨', pay: '取貨付款（模擬）' },
    { id: 'SO-260812-0051', date: '2026-08-12', total: 21900, status: 'complete', statusText: '已完成', items: [{ name: '學生筆電 L2', qty: 1, price: 21900, icon: 'laptop' }], recipient: '王＊＊', ship: '宅配到府', pay: 'ATM 轉帳（模擬）' },
    { id: 'SO-260805-0017', date: '2026-08-05', total: 4290, status: 'stopped', statusText: '已取消', items: [{ name: '降噪耳機 H8', qty: 1, price: 4290, icon: 'headset' }], recipient: '王＊＊', ship: '超商取貨', pay: '未付款' }
  ];

  /* ---- 前台客服案件 ---- */
  var myTickets = [
    {
      id: 'CS-260827-0311', subject: '螢幕收到時外箱破損', category: '商品瑕疵', created: '2026-08-27 10:24',
      status: 'waiting', statusText: '待您回覆', order: 'SO-260820-0098',
      thread: [
        { who: 'customer', name: '我', time: '08-27 10:24', text: '今天收到螢幕，外箱有壓痕，開箱後螢幕邊框有一小塊刮傷，想確認可以怎麼處理。' },
        { who: 'agent', name: '客服 林小姐', time: '08-27 11:02', text: '您好，已收到您的訊息。方便再提供刮傷處的近照嗎？我們會依照商品瑕疵流程協助換貨。' }
      ],
      attachments: ['外箱照片.jpg', '螢幕邊框.jpg']
    },
    {
      id: 'CS-260822-0288', subject: '想確認筆電保固範圍', category: '保固諮詢', created: '2026-08-22 15:40',
      status: 'complete', statusText: '已結案', order: 'SO-260812-0051',
      thread: [
        { who: 'customer', name: '我', time: '08-22 15:40', text: '請問學生筆電 L2 的保固是幾年？自己升級記憶體會不會失去保固？' },
        { who: 'agent', name: '客服 陳先生', time: '08-22 16:12', text: '主機保固 2 年，電池 1 年。加裝記憶體不影響主機保固，但因加裝造成的損壞不在範圍內。' },
        { who: 'customer', name: '我', time: '08-22 16:30', text: '了解，謝謝！' }
      ],
      attachments: []
    },
    {
      id: 'CS-260810-0207', subject: '訂單金額與活動折扣不符', category: '訂單問題', created: '2026-08-10 09:12',
      status: 'complete', statusText: '已結案', order: 'SO-260805-0017',
      thread: [
        { who: 'customer', name: '我', time: '08-10 09:12', text: '結帳時看到滿額折抵，但明細沒有折到。' },
        { who: 'agent', name: '客服 林小姐', time: '08-10 10:05', text: '已確認為活動門檻未達成，該筆訂單金額未滿 NT$ 20,000。已於訂單頁補上說明。' }
      ],
      attachments: []
    }
  ];

  var faqs = [
    { cat: '購買前', q: '我完全不懂規格，要怎麼開始？', a: '從首頁的「依用途挑選」開始，回答 2～3 個問題就好。系統會把規格翻譯成「適合做什麼」，不需要先懂零件名稱。' },
    { cat: '購買前', q: '預算不多，可以買到能用的電腦嗎？', a: '可以。使用「依預算挑選」設定上限後，只會顯示總價在預算內、且我們確認過搭配沒問題的組合。' },
    { cat: '訂單', q: '下單後可以修改收件地址嗎？', a: '訂單在「待出貨」狀態前可由客服協助修改。已出貨的訂單需改由物流端處理。' },
    { cat: '訂單', q: '可以指定到貨時間嗎？', a: '本展示版本不提供指定時段，實際上線後會依配送方式提供可選時段。' },
    { cat: '退貨', q: '收到商品不滿意可以退嗎？', a: '非瑕疵商品可於鑑賞期內申請退貨，商品需保持完整包裝。瑕疵商品請走「商品瑕疵」流程，我們會安排換貨或退款。' },
    { cat: '退貨', q: '退款要多久？', a: '商品寄回並完成檢查後開始退款作業，實際入帳時間依付款方式而定。' },
    { cat: '客服', q: '我可以查詢案件處理進度嗎？', a: '可以。登入後於「客服中心 → 案件紀錄」查看所有案件與往來訊息。' }
  ];

  /* ---- 後台：客服案件 ---- */
  var agents = ['林佩儀', '陳柏勳', '黃品瑄', '（未指派）'];

  var adminCases = [
    {
      id: 'CS-260827-0311', title: '商品瑕疵', subject: '螢幕收到時外箱破損', requester: '會員', priority: '高',
      assignee: '林佩儀', claimed: true, replied: false, closed: false,
      created: '08-27 10:24', last: '08-27 11:02', sla: '08-27 18:24', overdue: false,
      thread: [
        { who: 'customer', name: '會員', time: '08-27 10:24', text: '今天收到螢幕，外箱有壓痕，開箱後螢幕邊框有一小塊刮傷。' },
        { who: 'agent', name: '林佩儀', time: '08-27 11:02', text: '您好，方便再提供刮傷處的近照嗎？我們會依商品瑕疵流程協助換貨。' },
        { who: 'internal', name: '內部備註 · 林佩儀', time: '08-27 11:05', text: '已通知倉庫預留同型號一台，等顧客補照片後開換貨單。' }
      ]
    },
    {
      id: 'CS-260827-0309', title: '訂單問題', subject: '想更改收件地址', requester: '會員', priority: '中',
      assignee: '（未指派）', claimed: false, replied: false, closed: false,
      created: '08-27 09:40', last: '08-27 09:40', sla: '08-27 17:40', overdue: false,
      thread: [
        { who: 'customer', name: '會員', time: '08-27 09:40', text: '訂單 SO-260826-0142 想改寄到公司地址，還來得及嗎？' }
      ]
    },
    {
      id: 'CS-260826-0301', title: '保固諮詢', subject: '自行升級記憶體是否影響保固', requester: '會員', priority: '中',
      assignee: '陳柏勳', claimed: true, replied: true, closed: false,
      created: '08-26 14:10', last: '08-27 08:55', sla: '08-28 14:10', overdue: false,
      thread: [
        { who: 'customer', name: '會員', time: '08-26 14:10', text: '想自己加一條記憶體，會不會不能保固？' },
        { who: 'agent', name: '陳柏勳', time: '08-26 15:02', text: '加裝記憶體不影響主機保固，但因加裝造成的損壞不在範圍內。' },
        { who: 'customer', name: '會員', time: '08-27 08:55', text: '那可以請你們幫忙裝嗎？' }
      ]
    },
    {
      id: 'CS-260825-0294', title: '退貨諮詢', subject: '耳機用不習慣想退貨', requester: '會員', priority: '低',
      assignee: '黃品瑄', claimed: true, replied: true, closed: false,
      created: '08-25 16:22', last: '08-26 10:31', sla: '08-27 16:22', overdue: true,
      thread: [
        { who: 'customer', name: '會員', time: '08-25 16:22', text: '耳機戴起來會夾頭，可以退嗎？' },
        { who: 'agent', name: '黃品瑄', time: '08-25 17:00', text: '非瑕疵商品可於鑑賞期內申請退貨，需保持完整包裝。已提供退貨連結。' },
        { who: 'customer', name: '會員', time: '08-26 10:31', text: '包裝盒有拆開，這樣還可以嗎？' }
      ]
    },
    {
      id: 'CS-260821-0270', title: '訂單問題', subject: '折扣未套用', requester: '會員', priority: '中',
      assignee: '林佩儀', claimed: true, replied: true, closed: true,
      created: '08-21 09:12', last: '08-21 10:05', sla: null, overdue: false,
      thread: [
        { who: 'customer', name: '會員', time: '08-21 09:12', text: '結帳時看到滿額折抵，但明細沒有折到。' },
        { who: 'agent', name: '林佩儀', time: '08-21 10:05', text: '已確認為活動門檻未達成。已於訂單頁補上說明。' },
        { who: 'internal', name: '內部備註 · 林佩儀', time: '08-21 10:06', text: '結案；已回報行銷調整活動文案。' }
      ]
    }
  ];

  /* ---- 後台：檢舉 ---- */
  var reports = [
    { id: 'RP-260827-018', target: '商品評價 #4821', reason: '不實內容', reporter: '會員', level: '一般', status: 'in-progress', statusText: '待審核', assignee: '（未指派）', created: '08-27 08:10' },
    { id: 'RP-260826-015', target: '商品 PRD-1008', reason: '商品描述不符', reporter: '會員', level: '一般', status: 'in-progress', statusText: '審核中', assignee: '陳柏勳', created: '08-26 19:44' },
    { id: 'RP-260826-012', target: '客服對話 CS-260825-0294', reason: '服務態度', reporter: '會員', level: '需主管覆核', status: 'waiting', statusText: '待主管覆核', assignee: '主管 王品叡', created: '08-26 11:02' },
    { id: 'RP-260824-007', target: '商品評價 #4790', reason: '廣告灌水', reporter: '會員', level: '一般', status: 'complete', statusText: '已處理・移除', assignee: '黃品瑄', created: '08-24 15:33' },
    { id: 'RP-260822-003', target: '商品評價 #4755', reason: '不實內容', reporter: '會員', level: '一般', status: 'failed', statusText: '不成立', assignee: '林佩儀', created: '08-22 10:18' }
  ];

  /* ---- 後台：退貨退款 ---- */
  var returns = [
    { id: 'RMA-260826-014', order: 'SO-260826-0142', item: '創作繪圖組 C3', reason: '與描述不符', amount: 46800, status: 'in-progress', statusText: '待收貨', due: '09-02', assignee: '黃品瑄', stage: 2 },
    { id: 'RMA-260825-009', order: 'SO-260820-0098', item: '靜音鍵盤 K6', reason: '缺少配件', amount: 1590, status: 'waiting', statusText: '待客戶回覆', due: '08-30', assignee: '林佩儀', stage: 1 },
    { id: 'RMA-260824-051', order: 'SO-260812-0051', item: '學生筆電 L2', reason: '七日鑑賞期', amount: 21900, status: 'complete', statusText: '已退款', due: '—', assignee: '陳柏勳', stage: 5 },
    { id: 'RMA-260823-002', order: 'SO-260805-0017', item: '降噪耳機 H8', reason: '非瑕疵退貨', amount: 4290, status: 'failed', statusText: '逾期未寄回', due: '08-26', assignee: '黃品瑄', stage: 2 },
    { id: 'RMA-260820-088', order: 'SO-260812-0051', item: '大容量固態硬碟 S7', reason: '商品瑕疵', amount: 3280, status: 'in-progress', statusText: '檢查中', due: '08-29', assignee: '林佩儀', stage: 3 }
  ];

  var returnStages = ['申請成立', '待寄回 / 待收貨', '到貨檢查', '審核核准', '退款完成'];

  /* ---- 後台：會員 ---- */
  var members = [
    { id: 'M-000128', name: '王＊＊', mail: 'wa****@example.com', joined: '2026-03-11', orders: 8, spent: 132400, status: 'complete', statusText: '正常' },
    { id: 'M-000212', name: '李＊＊', mail: 'le****@example.com', joined: '2026-05-02', orders: 3, spent: 41200, status: 'complete', statusText: '正常' },
    { id: 'M-000377', name: '張＊＊', mail: 'zh****@example.com', joined: '2026-06-19', orders: 1, spent: 6980, status: 'waiting', statusText: '待驗證' },
    { id: 'M-000401', name: '陳＊＊', mail: 'ch****@example.com', joined: '2026-07-08', orders: 12, spent: 268900, status: 'complete', statusText: '正常' },
    { id: 'M-000455', name: '林＊＊', mail: 'li****@example.com', joined: '2026-08-01', orders: 0, spent: 0, status: 'stopped', statusText: '已停用' }
  ];

  /* ---- 後台：商品 ---- */
  var adminProducts = products.slice(0, 8).map(function (p, i) {
    return {
      id: p.id, name: p.name, category: p.category, price: p.price, stock: p.stock,
      status: p.stock === 0 ? 'stopped' : (i % 4 === 3 ? 'waiting' : 'complete'),
      statusText: p.stock === 0 ? '已下架' : (i % 4 === 3 ? '草稿' : '已上架'),
      updated: '2026-08-2' + ((i % 7) + 1)
    };
  });

  /* ---- 後台：訂單 ---- */
  var adminOrders = [
    { id: 'SO-260827-0155', member: '陳＊＊', date: '08-27', total: 62800, pay: '已付款', status: 'in-progress', statusText: '待出貨', ship: '宅配' },
    { id: 'SO-260826-0142', member: '王＊＊', date: '08-26', total: 46800, pay: '已付款', status: 'in-progress', statusText: '待出貨', ship: '宅配' },
    { id: 'SO-260826-0139', member: '李＊＊', date: '08-26', total: 12800, pay: '待付款', status: 'waiting', statusText: '待付款', ship: '超商' },
    { id: 'SO-260825-0121', member: '陳＊＊', date: '08-25', total: 3280, pay: '已付款', status: 'complete', statusText: '已送達', ship: '超商' },
    { id: 'SO-260820-0098', member: '王＊＊', date: '08-20', total: 8570, pay: '已付款', status: 'complete', statusText: '已完成', ship: '超商' },
    { id: 'SO-260805-0017', member: '王＊＊', date: '08-05', total: 4290, pay: '未付款', status: 'stopped', statusText: '已取消', ship: '超商' }
  ];

  global.PreviewData = {
    ICON: ICON,
    money: money,
    products: products,
    categories: categories,
    purposes: purposes,
    budgets: budgets,
    promotions: promotions,
    orders: orders,
    myTickets: myTickets,
    faqs: faqs,
    agents: agents,
    adminCases: adminCases,
    reports: reports,
    returns: returns,
    returnStages: returnStages,
    members: members,
    adminProducts: adminProducts,
    adminOrders: adminOrders
  };
})(window);
