/* =============================================================================
   DoSelect 懂選 — 前台視覺預覽畫面
   ============================================================================= */
(function (global) {
  'use strict';

  var D = global.PreviewData;
  var money = D.money;

  /* ---------- 共用片段 ---------- */

  function esc(text) {
    return String(text).replace(/[&<>"]/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
    });
  }

  function pageHead(opts) {
    var crumbs = (opts.crumbs || []).map(function (c) { return '<li>' + esc(c) + '</li>'; }).join('');
    return '' +
      '<header class="page-head">' +
        (crumbs ? '<ol class="page-head__crumbs">' + crumbs + '</ol>' : '') +
        '<div class="page-head__bar">' +
          '<div>' +
            '<h1>' + esc(opts.title) + '</h1>' +
            (opts.lede ? '<p class="page-head__lede">' + esc(opts.lede) + '</p>' : '') +
          '</div>' +
          (opts.actions ? '<div class="page-head__actions">' + opts.actions + '</div>' : '') +
        '</div>' +
      '</header>';
  }

  function badge(status, text) {
    return '<span class="badge badge--' + status + '">' + esc(text) + '</span>';
  }

  function icon(name) {
    return D.ICON[name] || D.ICON.box;
  }

  function thumb(name, big) {
    return '<div class="thumb' + (big ? ' thumb--lg' : '') + '">' + icon(name) + '</div>';
  }

  /* Donggu：專案內已有的正式立繪只有一張，其餘姿勢以清楚標記的預留區呈現，不另行生成替代角色。 */
  function dongguOfficial(caption, small) {
    return '' +
      '<div class="donggu' + (small ? ' donggu--sm' : '') + '">' +
        '<img src="../donggu-blue-white/assets/images/donggu-official.png" alt="Donggu 懂懂，DoSelect 懂選吉祥物" />' +
        (caption ? '<p class="donggu__caption">' + esc(caption) + '</p>' : '') +
      '</div>';
  }

  function dongguSlot(pose, small) {
    return '' +
      '<div class="donggu' + (small ? ' donggu--sm' : '') + '">' +
        '<div class="donggu__slot" role="img" aria-label="Donggu 立繪預留區，預定動作：' + esc(pose) + '">' +
          '<span>' +
            '<span>Donggu 立繪預留區</span>' +
            '<span class="donggu__pose">' + esc(pose) + '</span>' +
          '</span>' +
        '</div>' +
        '<p class="donggu__caption">待正式素材匯入</p>' +
      '</div>';
  }

  function hint(pose, title, body) {
    return '' +
      '<div class="hint">' +
        dongguSlot(pose, true) +
        '<div class="hint__body"><strong>' + esc(title) + '</strong><p>' + esc(body) + '</p></div>' +
      '</div>';
  }

  function productCard(p) {
    return '' +
      '<article class="product">' +
        thumb(p.icon) +
        '<div class="product__body">' +
          '<h3 class="product__name">' + esc(p.name) + '</h3>' +
          '<p class="product__for">' + esc(p.forWho) + '</p>' +
          '<div class="tag-row">' + p.tags.map(function (t) { return '<span class="tag">' + esc(t) + '</span>'; }).join('') + '</div>' +
          '<p class="product__price">' + money(p.price) + '</p>' +
          (p.stock === 0
            ? '<button type="button" class="btn btn--primary btn--sm btn--block" disabled>已售完</button>'
            : '<button type="button" class="btn btn--primary btn--sm btn--block" data-go="store/product">看詳細</button>') +
        '</div>' +
      '</article>';
  }

  /* ---------- 1. 首頁 ---------- */

  function home() {
    var hot = D.products.filter(function (p) { return p.hot; });
    return '' +
      '<section class="hero">' +
        '<div class="hero__text">' +
          '<h1>說出需求，組出適合你的電腦</h1>' +
          '<p>不用先懂規格。回答「要拿來做什麼」和「預算多少」，Donggu 幫你篩掉不合適的，只留下買得起、搭得起來的組合。</p>' +
          '<div class="btn-row">' +
            '<button type="button" class="btn btn--primary" data-go="store/products">依用途挑選</button>' +
            '<button type="button" class="btn btn--secondary" data-go="store/products">依預算挑選</button>' +
            '<button type="button" class="btn btn--ghost" data-go="store/products">看熱門商品</button>' +
          '</div>' +
        '</div>' +
        dongguOfficial('Donggu 懂懂・招手引導') +
      '</section>' +

      '<section class="section">' +
        '<div class="section__head"><h2>三步驟就好</h2><p>每一步只做一個決定，不需要背規格名稱。</p></div>' +
        '<div class="entry-grid">' +
          '<button type="button" class="entry" data-go="store/products">' +
            '<span class="entry__step">第 1 步</span>' +
            '<span class="entry__icon">' + icon('purpose') + '</span>' +
            '<h3>說用途</h3><p>打電動、剪片、還是文書上網？我們把用途換算成需要的規格。</p>' +
          '</button>' +
          '<button type="button" class="entry" data-go="store/products">' +
            '<span class="entry__step">第 2 步</span>' +
            '<span class="entry__icon">' + icon('budget') + '</span>' +
            '<h3>給預算</h3><p>設定金額上限，只看買得起的組合，不會挑到超出預算的。</p>' +
          '</button>' +
          '<button type="button" class="entry" data-go="store/products">' +
            '<span class="entry__step">第 3 步</span>' +
            '<span class="entry__icon">' + icon('hot') + '</span>' +
            '<h3>看推薦</h3><p>每個推薦都附「為什麼適合你」，以及相容性檢查結果。</p>' +
          '</button>' +
        '</div>' +
      '</section>' +

      '<section class="section">' +
        '<div class="section__head"><h2>看看你需要哪一類</h2><p>用圖形快速找到方向，不確定也沒關係。</p></div>' +
        '<div class="entry-grid">' +
          D.categories.map(function (c) {
            return '<button type="button" class="entry" data-go="store/products">' +
              '<span class="entry__icon">' + icon(c.icon) + '</span>' +
              '<h3>' + esc(c.key) + '</h3><p>' + esc(c.note) + '</p></button>';
          }).join('') +
        '</div>' +
      '</section>' +

      hint('比出 OK 手勢', '第一次買電腦？', '從「依用途挑選」開始最快。挑完之後我們會告訴你這台適合做什麼、不適合做什麼，不用自己查規格表。') +

      '<section class="section">' +
        '<div class="section__head"><h2>本週熱門</h2>' +
          '<button type="button" class="btn btn--ghost btn--sm" data-go="store/products">看全部商品</button></div>' +
        '<div class="product-grid">' + hot.map(productCard).join('') + '</div>' +
      '</section>';
  }

  /* ---------- 2. 商品列表 ---------- */

  function products(state) {
    var s = state.products;
    var list = D.products.filter(function (p) {
      if (s.purpose && p.purpose !== s.purpose) { return false; }
      if (s.budget && p.budget !== s.budget) { return false; }
      if (s.category && p.category !== s.category) { return false; }
      if (s.q && p.name.indexOf(s.q) === -1 && p.forWho.indexOf(s.q) === -1) { return false; }
      return true;
    });

    if (s.sort === 'price-asc') { list = list.slice().sort(function (a, b) { return a.price - b.price; }); }
    if (s.sort === 'price-desc') { list = list.slice().sort(function (a, b) { return b.price - a.price; }); }

    var perPage = 6;
    var pageCount = Math.max(1, Math.ceil(list.length / perPage));
    var page = Math.min(s.page, pageCount);
    var slice = list.slice((page - 1) * perPage, page * perPage);

    var chips = function (group, items, current) {
      return items.map(function (it) {
        var on = current === it.key;
        return '<button type="button" class="chip" aria-pressed="' + on + '" data-filter="' + group + '" data-value="' + esc(it.key) + '">' + esc(it.label || it.key) + '</button>';
      }).join('');
    };

    var body = slice.length
      ? '<div class="product-grid">' + slice.map(productCard).join('') + '</div>' +
        '<div class="pager">' +
          '<span class="pager__status">共 ' + list.length + ' 項，顯示第 ' + ((page - 1) * perPage + 1) + '–' + Math.min(page * perPage, list.length) + ' 項</span>' +
          '<div class="pager__pages">' +
            Array.from({ length: pageCount }, function (_, i) {
              var n = i + 1;
              return '<button type="button" data-page="' + n + '"' + (n === page ? ' aria-current="page"' : '') + '>' + n + '</button>';
            }).join('') +
          '</div>' +
        '</div>'
      : '<div class="state">' +
          dongguSlot('歪頭思考', true) +
          '<h3>這個條件下目前沒有商品</h3>' +
          '<p>已保留你選的條件。可以放寬預算、換一個用途，或直接看熱門商品。</p>' +
          '<div class="btn-row">' +
            '<button type="button" class="btn btn--primary" data-clear-filters>清除全部條件</button>' +
            '<button type="button" class="btn btn--secondary" data-go="store/home">回首頁</button>' +
          '</div>' +
        '</div>';

    var hasFilters = Boolean(s.purpose || s.budget || s.category || s.q);

    return '' +
      pageHead({
        crumbs: ['首頁', '商品'],
        title: '商品列表',
        lede: '先選用途和預算，再看細節。看不懂的規格我們都翻成「適合做什麼」。'
      }) +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 240px">' +
          '<span class="field__label">關鍵字</span>' +
          '<input class="input" type="search" placeholder="例如：筆電、螢幕、剪片" value="' + esc(s.q) + '" data-filter-q />' +
        '</label>' +
        '<div class="field"><span class="field__label">用途</span><div class="chip-row">' + chips('purpose', D.purposes, s.purpose) + '</div></div>' +
        '<div class="field"><span class="field__label">預算</span><div class="chip-row">' + chips('budget', D.budgets, s.budget) + '</div></div>' +
        '<label class="field">' +
          '<span class="field__label">排序</span>' +
          '<select class="select" data-sort>' +
            '<option value="default"' + (s.sort === 'default' ? ' selected' : '') + '>推薦順序</option>' +
            '<option value="price-asc"' + (s.sort === 'price-asc' ? ' selected' : '') + '>價格由低到高</option>' +
            '<option value="price-desc"' + (s.sort === 'price-desc' ? ' selected' : '') + '>價格由高到低</option>' +
          '</select>' +
        '</label>' +
        (hasFilters ? '<button type="button" class="btn btn--ghost" data-clear-filters>清除全部條件</button>' : '') +
      '</div>' +
      '<div class="chip-row">' +
        D.categories.slice(0, 5).map(function (c) {
          var on = s.category === c.key;
          return '<button type="button" class="chip" aria-pressed="' + on + '" data-filter="category" data-value="' + esc(c.key) + '">' + esc(c.key) + '</button>';
        }).join('') +
      '</div>' +
      body;
  }

  /* ---------- 3. 商品詳細 ---------- */

  function product() {
    var p = D.products[2];
    return '' +
      pageHead({ crumbs: ['首頁', '商品', p.category], title: p.name, lede: p.forWho }) +
      '<div class="grid grid--2">' +
        '<div>' + thumb(p.icon, true) +
          '<div class="grid grid--4" style="margin-top:var(--space-3)">' +
            [p.icon, 'monitor', 'storage', 'keyboard'].map(function (i) { return thumb(i); }).join('') +
          '</div>' +
        '</div>' +
        '<div class="card">' +
          '<p class="product__price" style="font-size:var(--fs-h1)">' + money(p.price) + '</p>' +
          '<p class="muted tiny">含組裝與出廠測試，內含 2 年保固</p>' +
          '<div class="tag-row" style="margin:var(--space-3) 0">' + p.tags.map(function (t) { return '<span class="tag">' + esc(t) + '</span>'; }).join('') + '</div>' +
          '<h3 style="font-size:var(--fs-h3);margin-bottom:var(--space-2)">這台適合你嗎</h3>' +
          '<dl class="detail-list">' +
            '<dt>適合</dt><dd>修圖、剪 1080p～4K 影片、繪圖板創作</dd>' +
            '<dt>可以但不強</dt><dd>大型 3A 遊戲（可玩，畫質建議中等）</dd>' +
            '<dt>不建議</dt><dd>專業 3D 算圖、長時間直播轉檔</dd>' +
          '</dl>' +
          '<div class="btn-row" style="margin-top:var(--space-4)">' +
            '<button type="button" class="btn btn--primary" data-toast="已加入購物車（示意）">加入購物車</button>' +
            '<button type="button" class="btn btn--secondary" data-go="store/cart">直接結帳</button>' +
          '</div>' +
          '<p class="tiny muted" style="margin-top:var(--space-3)">庫存 ' + p.stock + ' 台・下單後 2 個工作天內出貨</p>' +
        '</div>' +
      '</div>' +

      hint('指著螢幕說明', '看不懂規格沒關係', '下方「白話規格」把每個零件換成一句話說明。想看原始型號可以展開「完整規格」。') +

      '<section class="section">' +
        '<div class="section__head"><h2>白話規格</h2></div>' +
        '<div class="grid grid--2">' +
          '<div class="card"><h3 style="font-size:var(--fs-h3)">處理器</h3><p class="muted tiny">同時開很多程式也不卡；剪片輸出比文書機快約一倍。</p></div>' +
          '<div class="card"><h3 style="font-size:var(--fs-h3)">顯示卡</h3><p class="muted tiny">修圖、影片特效會用到；也能玩大部分遊戲。</p></div>' +
          '<div class="card"><h3 style="font-size:var(--fs-h3)">記憶體 32GB</h3><p class="muted tiny">同時開 Photoshop 與剪輯軟體不用一直等。</p></div>' +
          '<div class="card"><h3 style="font-size:var(--fs-h3)">固態硬碟 1TB</h3><p class="muted tiny">開機快，大約可放 200 部手機影片或 10 萬張照片。</p></div>' +
        '</div>' +
        '<details class="card"><summary style="cursor:pointer;font-weight:700">完整規格（給懂的人看）</summary>' +
          '<div class="table-scroll" style="margin-top:var(--space-3)">' +
            '<table class="data"><tbody>' +
              '<tr><th>處理器</th><td>示意型號 CPU-8C16T</td></tr>' +
              '<tr><th>顯示卡</th><td>示意型號 GPU-12G</td></tr>' +
              '<tr><th>記憶體</th><td>32GB（16GB × 2）</td></tr>' +
              '<tr><th>儲存</th><td>1TB NVMe 固態硬碟</td></tr>' +
              '<tr><th>電源</th><td>750W 80+ 金牌</td></tr>' +
            '</tbody></table>' +
          '</div>' +
        '</details>' +
      '</section>';
  }

  /* ---------- 4. 購物車與結帳 ---------- */

  function cart(state) {
    var step = state.cartStep;
    var items = [
      { name: '創作繪圖組 C3', qty: 1, price: 46800, icon: 'gpu' },
      { name: '27 吋護眼螢幕 M5', qty: 1, price: 6980, icon: 'monitor' }
    ];
    var subtotal = items.reduce(function (a, b) { return a + b.price * b.qty; }, 0);
    var discount = 1500;
    var ship = 0;

    var steps = ['購物車', '填寫資料', '選擇付款', '完成'].map(function (label, i) {
      var n = i + 1;
      var attrs = n === step ? ' aria-current="step"' : (n < step ? ' data-done="true"' : '');
      return '<li' + attrs + '><span class="steps__no">' + n + '</span>' + esc(label) + '</li>';
    }).join('');

    var main;
    if (step === 4) {
      main = '<div class="state">' + dongguSlot('雙手比讚', true) +
        '<h3>訂單已成立（示意）</h3>' +
        '<p>訂單編號 SO-260828-0161。這是預覽畫面，不會真的成立訂單或扣款。</p>' +
        '<div class="btn-row"><button type="button" class="btn btn--primary" data-go="store/orders">查看訂單</button>' +
        '<button type="button" class="btn btn--secondary" data-go="store/home">回首頁</button></div></div>';
    } else if (step === 1) {
      main = '<div class="card card--flush"><div class="table-scroll"><table class="data">' +
        '<thead><tr><th>商品</th><th class="num">單價</th><th class="num">數量</th><th class="num">小計</th><th></th></tr></thead><tbody>' +
        items.map(function (it) {
          return '<tr><td><strong>' + esc(it.name) + '</strong></td>' +
            '<td class="num">' + money(it.price) + '</td>' +
            '<td class="num">' + it.qty + '</td>' +
            '<td class="num">' + money(it.price * it.qty) + '</td>' +
            '<td class="num"><button type="button" class="btn btn--danger btn--sm" data-toast="已移除（示意）">移除</button></td></tr>';
        }).join('') +
        '</tbody></table></div></div>';
    } else if (step === 2) {
      main = '<div class="card"><div class="grid grid--2">' +
        '<label class="field"><span class="field__label">收件人<span class="field__req">*</span></span><input class="input" value="王小明" /></label>' +
        '<label class="field"><span class="field__label">聯絡電話<span class="field__req">*</span></span><input class="input" value="09xx-xxx-xxx" /></label>' +
        '<label class="field" style="grid-column:1/-1"><span class="field__label">收件地址<span class="field__req">*</span></span><input class="input" value="台北市中正區示意路 1 號" /></label>' +
        '<label class="field"><span class="field__label">配送方式</span><select class="select"><option>宅配到府</option><option>超商取貨</option></select></label>' +
        '<label class="field"><span class="field__label">發票（模擬）</span><select class="select"><option>電子發票 · 會員載具</option><option>統一編號</option></select></label>' +
        '</div></div>';
    } else {
      main = '<div class="card"><div class="grid grid--3">' +
        ['信用卡（模擬）', 'ATM 轉帳（模擬）', '超商代碼（模擬）'].map(function (label, i) {
          return '<button type="button" class="entry' + (i === 0 ? ' is-selected' : '') + '" data-toast="已選擇 ' + esc(label) + '（示意）">' +
            '<span class="entry__icon">' + icon('budget') + '</span><h3>' + esc(label) + '</h3>' +
            '<p>此預覽不會實際請款或建立金流交易。</p></button>';
        }).join('') +
        '</div></div>';
    }

    return '' +
      pageHead({ crumbs: ['首頁', '購物車'], title: '購物車與結帳', lede: '每一步只有一個主要按鈕，可以隨時回上一步，資料不會不見。' }) +
      '<ol class="steps">' + steps + '</ol>' +
      '<div class="grid" style="grid-template-columns:minmax(0,2fr) minmax(260px,1fr)">' +
        '<div>' + main + '</div>' +
        '<aside class="card">' +
          '<h3 style="font-size:var(--fs-h3);margin-bottom:var(--space-3)">金額摘要</h3>' +
          '<div class="summary-row"><span>商品小計</span><span>' + money(subtotal) + '</span></div>' +
          '<div class="summary-row"><span>開學季折抵</span><span>−' + money(discount) + '</span></div>' +
          '<div class="summary-row"><span>運費</span><span>' + (ship === 0 ? '免運' : money(ship)) + '</span></div>' +
          '<div class="summary-row summary-row--total"><span>應付總額</span><span>' + money(subtotal - discount + ship) + '</span></div>' +
          '<div class="btn-row" style="margin-top:var(--space-4)">' +
            (step > 1 && step < 4 ? '<button type="button" class="btn btn--secondary btn--sm" data-cart-step="' + (step - 1) + '">上一步</button>' : '') +
            (step < 4 ? '<button type="button" class="btn btn--primary btn--block" data-cart-step="' + (step + 1) + '">' + (step === 3 ? '送出訂單' : '下一步') + '</button>' : '') +
          '</div>' +
          '<p class="tiny muted" style="margin-top:var(--space-3)">金額由後端重新計算，前端不送價格。</p>' +
        '</aside>' +
      '</div>';
  }

  /* ---------- 5. 優惠活動 ---------- */

  function promotions() {
    return '' +
      pageHead({ crumbs: ['首頁', '優惠活動'], title: '優惠活動', lede: '目前進行中的活動與適用條件。結帳時系統會自動判斷是否符合。' }) +
      '<div class="grid grid--2">' +
        D.promotions.map(function (p) {
          return '<article class="card">' +
            '<div style="display:flex;align-items:flex-start;justify-content:space-between;gap:var(--space-3)">' +
              '<div><h3 style="font-size:var(--fs-h3)">' + esc(p.name) + '</h3>' +
              '<p class="muted tiny">' + esc(p.period) + '</p></div>' +
              badge(p.status, p.statusText) +
            '</div>' +
            '<p style="margin:var(--space-3) 0">' + esc(p.rule) + '</p>' +
            '<div class="meter"><div class="meter__track"><div class="meter__fill" style="width:' + Math.round(p.used / p.quota * 100) + '%"></div></div>' +
            '<p class="tiny muted">已使用 ' + p.used + ' / ' + p.quota + ' 份</p></div>' +
            '<div class="btn-row" style="margin-top:var(--space-4)">' +
              (p.status === 'in-progress'
                ? '<button type="button" class="btn btn--primary btn--sm" data-go="store/products">去逛適用商品</button>'
                : '<button type="button" class="btn btn--secondary btn--sm" disabled>目前無法使用</button>') +
            '</div>' +
          '</article>';
        }).join('') +
      '</div>';
  }

  /* ---------- 6. 會員中心 ---------- */

  function account() {
    return '' +
      pageHead({ crumbs: ['首頁', '會員中心'], title: '會員中心', lede: '查看訂單、地址、通知與客服案件。' }) +
      '<div class="grid grid--4">' +
        '<div class="stat"><span class="stat__label">進行中訂單</span><span class="stat__value">1</span><span class="stat__note">待出貨 1 筆</span></div>' +
        '<div class="stat"><span class="stat__label">客服案件</span><span class="stat__value">1</span><span class="stat__note">待您回覆</span></div>' +
        '<div class="stat"><span class="stat__label">可用優惠</span><span class="stat__value">2</span><span class="stat__note">開學季、周邊買二送一</span></div>' +
        '<div class="stat"><span class="stat__label">未讀通知</span><span class="stat__value">3</span><span class="stat__note">出貨與案件更新</span></div>' +
      '</div>' +
      '<div class="grid grid--2">' +
        '<section class="card"><h2 style="font-size:var(--fs-h2);margin-bottom:var(--space-3)">基本資料</h2>' +
          '<dl class="detail-list">' +
            '<dt>姓名</dt><dd>王小明</dd>' +
            '<dt>電子郵件</dt><dd>wa****@example.com</dd>' +
            '<dt>手機</dt><dd>09xx-xxx-xxx</dd>' +
            '<dt>加入時間</dt><dd>2026-03-11</dd>' +
          '</dl>' +
          '<div class="btn-row" style="margin-top:var(--space-4)">' +
            '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：開啟編輯表單">編輯資料</button>' +
            '<button type="button" class="btn btn--ghost btn--sm" data-toast="示意：管理收件地址">收件地址</button>' +
          '</div>' +
        '</section>' +
        '<section class="card"><h2 style="font-size:var(--fs-h2);margin-bottom:var(--space-3)">快速入口</h2>' +
          '<div class="grid grid--2">' +
            '<button type="button" class="entry" data-go="store/orders"><span class="entry__icon">' + icon('box') + '</span><h3>我的訂單</h3><p>查看出貨與退貨狀態</p></button>' +
            '<button type="button" class="entry" data-go="store/support"><span class="entry__icon">' + icon('support') + '</span><h3>客服中心</h3><p>常見問題與案件紀錄</p></button>' +
            '<button type="button" class="entry" data-go="store/promotions"><span class="entry__icon">' + icon('tag') + '</span><h3>我的優惠</h3><p>可用活動與折扣</p></button>' +
            '<button type="button" class="entry" data-go="store/returns"><span class="entry__icon">' + icon('cart') + '</span><h3>退貨申請</h3><p>申請退貨與查看進度</p></button>' +
          '</div>' +
        '</section>' +
      '</div>';
  }

  /* ---------- 7. 訂單列表與詳細 ---------- */

  function orders(state) {
    var current = D.orders.filter(function (o) { return o.id === state.orderId; })[0];

    var rows = D.orders.map(function (o) {
      return '<tr' + (current && current.id === o.id ? ' aria-selected="true"' : '') + '>' +
        '<td class="mono">' + esc(o.id) + '</td>' +
        '<td>' + esc(o.date) + '</td>' +
        '<td>' + esc(o.items[0].name) + (o.items.length > 1 ? ' 等 ' + o.items.length + ' 項' : '') + '</td>' +
        '<td class="num">' + money(o.total) + '</td>' +
        '<td>' + badge(o.status, o.statusText) + '</td>' +
        '<td class="num"><button type="button" class="btn btn--ghost btn--sm" data-order="' + esc(o.id) + '">查看</button></td>' +
      '</tr>';
    }).join('');

    var detail = current ? '' +
      '<section class="card">' +
        '<div style="display:flex;flex-wrap:wrap;align-items:flex-start;justify-content:space-between;gap:var(--space-3)">' +
          '<div><h2 style="font-size:var(--fs-h2)">訂單 ' + esc(current.id) + '</h2>' +
          '<p class="muted tiny">下單日 ' + esc(current.date) + '</p></div>' +
          badge(current.status, current.statusText) +
        '</div>' +
        '<div class="grid grid--2" style="margin-top:var(--space-4)">' +
          '<div><h3 style="font-size:var(--fs-h3);margin-bottom:var(--space-2)">商品</h3>' +
            current.items.map(function (it) {
              return '<div style="display:flex;gap:var(--space-3);align-items:center;padding:var(--space-2) 0;border-bottom:1px solid var(--color-border-soft)">' +
                '<div style="width:56px;flex:none">' + thumb(it.icon) + '</div>' +
                '<div style="flex:1;min-width:0"><strong>' + esc(it.name) + '</strong><p class="muted tiny">數量 ' + it.qty + '</p></div>' +
                '<span class="num">' + money(it.price * it.qty) + '</span></div>';
            }).join('') +
            '<div class="summary-row summary-row--total"><span>訂單總額</span><span>' + money(current.total) + '</span></div>' +
          '</div>' +
          '<div><h3 style="font-size:var(--fs-h3);margin-bottom:var(--space-2)">配送與付款</h3>' +
            '<dl class="detail-list">' +
              '<dt>收件人</dt><dd>' + esc(current.recipient) + '</dd>' +
              '<dt>配送方式</dt><dd>' + esc(current.ship) + '</dd>' +
              '<dt>付款方式</dt><dd>' + esc(current.pay) + '</dd>' +
            '</dl>' +
            '<h3 style="font-size:var(--fs-h3);margin:var(--space-4) 0 var(--space-2)">處理進度</h3>' +
            '<ul class="timeline">' +
              '<li><strong>訂單成立</strong><span class="muted">' + esc(current.date) + ' 10:12</span></li>' +
              '<li><strong>付款完成</strong><span class="muted">' + esc(current.date) + ' 10:15</span></li>' +
              (current.status === 'complete' ? '<li><strong>已送達</strong><span class="muted">' + esc(current.date) + ' 18:40</span></li>' : '<li><strong>備貨中</strong><span class="muted">預計 2 個工作天內出貨</span></li>') +
            '</ul>' +
            '<div class="btn-row" style="margin-top:var(--space-4)">' +
              (current.status === 'complete' ? '<button type="button" class="btn btn--secondary btn--sm" data-go="store/returns">申請退貨</button>' : '') +
              '<button type="button" class="btn btn--ghost btn--sm" data-go="store/support">聯絡客服</button>' +
            '</div>' +
          '</div>' +
        '</div>' +
      '</section>'
      : '';

    return '' +
      pageHead({ crumbs: ['首頁', '會員中心', '訂單'], title: '我的訂單', lede: '點「查看」可展開該筆訂單的明細、配送與處理進度。' }) +
      '<div class="table-scroll"><table class="data">' +
        '<thead><tr><th>訂單編號</th><th>日期</th><th>商品</th><th class="num">金額</th><th>狀態</th><th></th></tr></thead>' +
        '<tbody>' + rows + '</tbody></table></div>' +
      detail;
  }

  /* ---------- 8. 客服中心 ---------- */

  function support(state) {
    var tab = state.supportTab;
    var tabs = [['faq', '常見問題'], ['contact', '聯絡客服'], ['cases', '案件紀錄']]
      .map(function (t) {
        return '<button type="button" role="tab" aria-selected="' + (tab === t[0]) + '" data-support-tab="' + t[0] + '">' + esc(t[1]) + '</button>';
      }).join('');

    var body;
    if (tab === 'faq') {
      var cats = ['購買前', '訂單', '退貨', '客服'];
      body = '<div class="filter-bar"><label class="field" style="flex:1"><span class="field__label">搜尋問題</span>' +
        '<input class="input" type="search" placeholder="輸入關鍵字，例如：退貨、保固" /></label></div>' +
        cats.map(function (c) {
          return '<section class="section"><div class="section__head"><h2 style="font-size:var(--fs-h3)">' + esc(c) + '</h2></div>' +
            '<div class="faq">' + D.faqs.filter(function (f) { return f.cat === c; }).map(function (f) {
              return '<details><summary>' + esc(f.q) + '</summary><p>' + esc(f.a) + '</p></details>';
            }).join('') + '</div></section>';
        }).join('');
    } else if (tab === 'contact') {
      body = '<div class="grid" style="grid-template-columns:minmax(0,2fr) minmax(240px,1fr)">' +
        '<form class="card" onsubmit="return false">' +
          '<h2 style="font-size:var(--fs-h2);margin-bottom:var(--space-4)">建立客服案件</h2>' +
          '<div class="grid grid--2">' +
            '<label class="field"><span class="field__label">問題分類<span class="field__req">*</span></span>' +
              '<select class="select"><option>訂單問題</option><option>商品瑕疵</option><option>退貨退款</option><option>保固諮詢</option><option>優惠活動</option><option>帳號問題</option><option>其他</option></select></label>' +
            '<label class="field"><span class="field__label">相關訂單（選填）</span>' +
              '<select class="select"><option>不指定</option>' + D.orders.map(function (o) { return '<option>' + esc(o.id) + '</option>'; }).join('') + '</select></label>' +
            '<label class="field" style="grid-column:1/-1"><span class="field__label">主旨<span class="field__req">*</span></span>' +
              '<input class="input" placeholder="用一句話說明你的問題" /></label>' +
            '<label class="field" style="grid-column:1/-1"><span class="field__label">問題說明<span class="field__req">*</span></span>' +
              '<textarea class="textarea" placeholder="發生什麼事、什麼時候發生、你已經試過什麼"></textarea>' +
              '<span class="field__hint">最多 2000 字。請不要填寫信用卡號等敏感資料。</span></label>' +
            '<div class="field" style="grid-column:1/-1"><span class="field__label">附件（最多 3 個）</span>' +
              '<input class="input" type="file" />' +
              '<span class="field__hint">支援 JPG／PNG／PDF，單檔 10MB 以內。</span></div>' +
          '</div>' +
          '<div class="btn-row" style="margin-top:var(--space-4)">' +
            '<button type="button" class="btn btn--primary" data-toast="已送出案件（示意）">送出案件</button>' +
            '<button type="button" class="btn btn--ghost">取消</button>' +
          '</div>' +
        '</form>' +
        '<aside class="card">' + dongguSlot('拿著耳機的客服姿勢') +
          '<h3 style="font-size:var(--fs-h3);margin-top:var(--space-3)">送出後會怎樣</h3>' +
          '<ul class="timeline" style="margin-top:var(--space-3)">' +
            '<li><strong>系統建立案件</strong><span class="muted">會給你一組案件編號</span></li>' +
            '<li><strong>客服承接</strong><span class="muted">一個工作日內首次回覆</span></li>' +
            '<li><strong>往來處理</strong><span class="muted">可在案件紀錄補充訊息</span></li>' +
          '</ul>' +
        '</aside>' +
      '</div>';
    } else {
      var t = D.myTickets.filter(function (x) { return x.id === state.ticketId; })[0];
      body = '<div class="split" data-detail-open="' + Boolean(t) + '">' +
        '<div class="split__list"><div class="case-list">' +
          D.myTickets.map(function (x) {
            return '<button type="button" class="case-item" aria-current="' + (t && t.id === x.id) + '" data-ticket="' + esc(x.id) + '">' +
              '<span class="case-item__top"><span class="case-item__no">' + esc(x.id) + '</span>' + badge(x.status, x.statusText) + '</span>' +
              '<span class="case-item__title">' + esc(x.subject) + '</span>' +
              '<span class="case-item__meta"><span>' + esc(x.category) + '</span><span>' + esc(x.created) + '</span></span>' +
            '</button>';
          }).join('') +
        '</div></div>' +
        (t ? '<div class="split__detail">' +
          '<div class="split__detail-head">' +
            '<div><h3>' + esc(t.subject) + '</h3><p class="muted tiny">' + esc(t.id) + '・' + esc(t.category) + '・關聯訂單 ' + esc(t.order) + '</p></div>' +
            '<button type="button" class="split__close" data-ticket-close aria-label="關閉案件詳細">×</button>' +
          '</div>' +
          '<div class="split__detail-body">' +
            '<div class="thread">' + t.thread.map(function (m) {
              return '<div class="msg' + (m.who === 'agent' ? ' msg--agent' : '') + '">' +
                '<span class="msg__meta">' + esc(m.name) + '・' + esc(m.time) + '</span><span>' + esc(m.text) + '</span></div>';
            }).join('') + '</div>' +
            (t.attachments.length ? '<div><h4 class="tiny" style="margin-bottom:var(--space-2)">附件</h4><div class="tag-row">' +
              t.attachments.map(function (a) { return '<span class="tag">' + esc(a) + '</span>'; }).join('') + '</div></div>' : '') +
            (t.status === 'complete'
              ? '<div class="locked-note">此案件已結案，無法再回覆。若還有問題，請建立新的案件並附上本案件編號。</div>'
              : '<div class="reply-box"><label class="field"><span class="field__label">回覆客服</span>' +
                '<textarea class="textarea" placeholder="補充說明或提供照片"></textarea></label>' +
                '<div class="btn-row"><button type="button" class="btn btn--primary btn--sm" data-toast="已送出回覆（示意）">送出回覆</button>' +
                '<button type="button" class="btn btn--ghost btn--sm" data-toast="已取消案件（示意）">取消案件</button></div></div>') +
          '</div>' +
        '</div>' : '') +
      '</div>';
    }

    return '' +
      pageHead({
        crumbs: ['首頁', '客服中心'],
        title: '客服中心',
        lede: '先看常見問題，找不到答案再建立案件。所有往來紀錄都會留在案件裡。'
      }) +
      '<div class="tabs" role="tablist">' + tabs + '</div>' +
      body;
  }

  /* ---------- 9. 退貨申請 ---------- */

  function returns(state) {
    var step = state.returnStep;
    var steps = ['選擇訂單與商品', '填寫原因', '寄回方式', '確認送出'].map(function (label, i) {
      var n = i + 1;
      var attrs = n === step ? ' aria-current="step"' : (n < step ? ' data-done="true"' : '');
      return '<li' + attrs + '><span class="steps__no">' + n + '</span>' + esc(label) + '</li>';
    }).join('');

    var body;
    if (step === 1) {
      body = '<div class="card"><label class="field" style="max-width:420px"><span class="field__label">選擇訂單<span class="field__req">*</span></span>' +
        '<select class="select">' + D.orders.filter(function (o) { return o.status === 'complete'; }).map(function (o) {
          return '<option>' + esc(o.id) + '（' + esc(o.date) + '）</option>';
        }).join('') + '</select></label>' +
        '<h3 style="font-size:var(--fs-h3);margin:var(--space-4) 0 var(--space-2)">選擇要退的商品</h3>' +
        '<div class="table-scroll"><table class="data"><thead><tr><th>退貨</th><th>商品</th><th class="num">可退數量</th><th class="num">單價</th></tr></thead><tbody>' +
        '<tr><td><input type="checkbox" checked /></td><td>27 吋護眼螢幕 M5</td><td class="num">1</td><td class="num">' + money(6980) + '</td></tr>' +
        '<tr><td><input type="checkbox" /></td><td>靜音鍵盤 K6</td><td class="num">1</td><td class="num">' + money(1590) + '</td></tr>' +
        '</tbody></table></div></div>';
    } else if (step === 2) {
      body = '<div class="card"><div class="grid grid--2">' +
        '<label class="field"><span class="field__label">退貨原因<span class="field__req">*</span></span>' +
          '<select class="select"><option>商品瑕疵</option><option>與描述不符</option><option>缺少配件</option><option>七日鑑賞期</option><option>其他</option></select></label>' +
        '<label class="field"><span class="field__label">商品狀態</span>' +
          '<select class="select"><option>包裝完整未拆</option><option>已拆封、配件齊全</option><option>已拆封、缺配件</option></select></label>' +
        '<label class="field" style="grid-column:1/-1"><span class="field__label">詳細說明<span class="field__req">*</span></span>' +
          '<textarea class="textarea" placeholder="請說明發生的狀況"></textarea>' +
          '<span class="field__hint">最多 500 字。若為瑕疵，請一併上傳照片以加速審核。</span></label>' +
        '<div class="field" style="grid-column:1/-1"><span class="field__label">照片（最多 3 張）</span><input class="input" type="file" /></div>' +
        '</div></div>';
    } else if (step === 3) {
      body = '<div class="card"><div class="grid grid--2">' +
        ['超商寄回（免運）', '宅配收件（到府取件）'].map(function (label, i) {
          return '<button type="button" class="entry' + (i === 0 ? ' is-selected' : '') + '" data-toast="已選擇 ' + esc(label) + '（示意）">' +
            '<span class="entry__icon">' + icon('box') + '</span><h3>' + esc(label) + '</h3>' +
            '<p>核准後會提供寄回代碼；請於 7 天內完成寄回。</p></button>';
        }).join('') + '</div></div>';
    } else {
      body = '<div class="state">' + dongguSlot('打勾確認手勢', true) +
        '<h3>退貨申請已送出（示意）</h3>' +
        '<p>申請編號 RMA-260828-021。客服會在一個工作日內審核，狀態可在「我的訂單」或案件紀錄查看。</p>' +
        '<div class="btn-row"><button type="button" class="btn btn--primary" data-go="store/orders">回訂單列表</button>' +
        '<button type="button" class="btn btn--secondary" data-go="store/support">查看案件</button></div></div>';
    }

    return '' +
      pageHead({ crumbs: ['首頁', '會員中心', '退貨申請'], title: '退貨申請', lede: '一次只做一個決定；每一步都可以回上一步修改。' }) +
      '<ol class="steps">' + steps + '</ol>' +
      body +
      (step < 4 ? '<div class="btn-row">' +
        (step > 1 ? '<button type="button" class="btn btn--secondary" data-return-step="' + (step - 1) + '">上一步</button>' : '') +
        '<button type="button" class="btn btn--primary" data-return-step="' + (step + 1) + '">' + (step === 3 ? '確認送出' : '下一步') + '</button>' +
      '</div>' : '') +
      (step === 1 ? hint('指著清單', '哪些可以退？', '只會列出已送達、且還在可退期限內的商品。已使用消耗品與客製化商品不在退貨範圍。') : '');
  }

  global.StoreViews = {
    esc: esc,
    pageHead: pageHead,
    badge: badge,
    icon: icon,
    thumb: thumb,
    dongguSlot: dongguSlot,
    dongguOfficial: dongguOfficial,
    hint: hint,
    pages: {
      home: home,
      products: products,
      product: product,
      cart: cart,
      promotions: promotions,
      account: account,
      orders: orders,
      support: support,
      returns: returns
    }
  };
})(window);
