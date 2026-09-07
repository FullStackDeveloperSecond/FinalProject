export interface AdminNavigationItem { title: string; to: string; icon: string; description: string }
export interface AdminNavigationGroup { id: string; title: string; icon: string; description: string; items: AdminNavigationItem[] }
export const adminNavigation: AdminNavigationGroup[] = [
  { id: 'support', title: '客服與售後', icon: 'support', description: '受理問題、追蹤案件與售後服務', items: [
    { title: '客服 SLA 佇列', to: '/support', icon: 'support', description: '受理待處理案件，掌握回覆時限。' },
    { title: '案件工作台', to: '/cases', icon: 'document', description: '依優先程度與進度追蹤案件。' },
    { title: '退貨案件', to: '/returns', icon: 'return', description: '審核退貨申請並追蹤收件進度。' },
    { title: '商品評價審核', to: '/reviews', icon: 'star', description: '審核已驗證購買的商品評價。' },
  ] },
  { id: 'catalog', title: '商品管理', icon: 'box', description: '維護商品目錄、規格與上架資訊', items: [
    { title: '商品管理', to: '/products', icon: 'box', description: '維護商品、價格與上下架狀態。' },
    { title: '品牌／分類／標籤管理', to: '/catalog/lookups', icon: 'tag', description: '整理品牌、分類與商品標籤。' },
    { title: '商品匯入', to: '/products/import', icon: 'upload', description: '批次匯入與檢查商品資料。' },
    { title: '分類規格範本', to: '/catalog/specifications', icon: 'layers', description: '設定各類商品的規格欄位。' },
    { title: '相容性規則', to: '/catalog/compatibility', icon: 'settings', description: '管理零件搭配的相容性規則。' },
  ] },
  { id: 'inventory', title: '庫存管理', icon: 'warehouse', description: '掌握存量、保留數量與對帳結果', items: [
    { title: '庫存管理', to: '/inventory', icon: 'warehouse', description: '查看庫存餘額與異動明細。' },
    { title: '庫存匯入', to: '/inventory/imports', icon: 'upload', description: '批次更新庫存並檢查匯入結果。' },
    { title: '庫存保留佇列', to: '/inventory/reservations', icon: 'clock', description: '追蹤訂單保留的商品庫存。' },
    { title: '庫存對帳案件', to: '/inventory/reconciliation-cases', icon: 'document', description: '檢查與處理庫存差異。' },
  ] },
  { id: 'operations', title: '營運與物流', icon: 'truck', description: '處理訂單、出貨與配送設定', items: [
    { title: '訂單管理', to: '/orders', icon: 'document', description: '查看訂單與後續處理進度。' },
    { title: '批次出貨', to: '/shipping/batches', icon: 'truck', description: '集中處理準備出貨的訂單。' },
    { title: '示範超商門市', to: '/shipping/stores', icon: 'home', description: '查看與管理取貨門市資訊。' },
    { title: '包裹限制版本', to: '/shipping/package-limits', icon: 'box', description: '維護配送包裹的限制設定。' },
  ] },
  { id: 'finance', title: '財務與報表', icon: 'chart', description: '管理優惠、退款、發票與營運分析', items: [
    { title: '退款管理', to: '/refunds', icon: 'wallet', description: '追蹤退款申請與處理狀態。' },
    { title: '優惠券管理', to: '/coupons', icon: 'tag', description: '設定優惠條件與使用規則。' },
    { title: '模擬發票管理', to: '/invoices', icon: 'document', description: '查看示範發票與開立狀態。' },
    { title: 'AI 用量與成本', to: '/ai/usage', icon: 'chart', description: '查看 AI 服務用量與成本。' },
    { title: '營運報表', to: '/reports/sales-overview', icon: 'chart', description: '查詢營運數據與匯出報表。' },
  ] },
]
