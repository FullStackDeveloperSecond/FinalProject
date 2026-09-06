/* =============================================================================
   DoSelect 懂選 — 視覺預覽：Hash 路由、導覽與示意互動
   狀態存於記憶體，重新整理即重置。不發送任何網路請求。
   ============================================================================= */
(function (global) {
  'use strict';

  var esc = global.StoreViews.esc;

  var NAV = {
    store: {
      label: '前台畫面',
      items: [
        ['home', '首頁'],
        ['products', '商品列表'],
        ['product', '商品詳細'],
        ['cart', '購物車與結帳'],
        ['promotions', '優惠活動'],
        ['account', '會員中心'],
        ['orders', '訂單列表與詳細'],
        ['support', '客服中心'],
        ['returns', '退貨申請']
      ]
    },
    admin: {
      label: '後台畫面',
      items: [
        ['dashboard', '後台首頁 / 儀表板'],
        ['members', '會員管理'],
        ['products', '商品管理'],
        ['orders', '訂單管理'],
        ['promotions', '優惠活動管理'],
        ['cases', '客服案件'],
        ['reports', '檢舉審核'],
        ['returns', '退貨退款審核'],
        ['analytics', '營運報表']
      ]
    }
  };

  var state = {
    mode: 'store',
    page: 'home',
    products: { q: '', purpose: '', budget: '', category: '', sort: 'default', page: 1 },
    cartStep: 1,
    returnStep: 1,
    supportTab: 'faq',
    ticketId: null,
    orderId: null,
    caseId: null,
    caseClaimedByMe: null,
    returnId: null
  };

  var main = document.getElementById('preview-main');
  var nav = document.getElementById('page-nav');
  var announcer = document.getElementById('route-announcer');
  var sessionBox = document.getElementById('topbar-session');
  var navToggle = document.getElementById('nav-toggle');

  function parseHash() {
    var raw = (location.hash || '#/store/home').replace(/^#\/?/, '');
    var parts = raw.split('/');
    var mode = parts[0] === 'admin' ? 'admin' : 'store';
    var page = parts[1] || (mode === 'admin' ? 'dashboard' : 'home');
    if (!NAV[mode].items.some(function (i) { return i[0] === page; })) {
      page = NAV[mode].items[0][0];
    }
    return { mode: mode, page: page };
  }

  function renderNav() {
    var group = NAV[state.mode];
    nav.innerHTML = '<p class="page-nav__title">' + esc(group.label) + '</p>' +
      group.items.map(function (i) {
        var href = '#/' + state.mode + '/' + i[0];
        var current = i[0] === state.page ? ' aria-current="page"' : '';
        return '<a href="' + href + '"' + current + '>' + esc(i[1]) + '</a>';
      }).join('');

    Array.prototype.forEach.call(document.querySelectorAll('[data-mode-link]'), function (a) {
      a.setAttribute('aria-current', String(a.getAttribute('data-mode-link') === state.mode));
    });

    sessionBox.innerHTML = state.mode === 'admin'
      ? '<span class="chip-demo">DEMO DATA</span><span>林佩儀（客服）</span><span>登出</span>'
      : '<span>王小明</span><span>通知 3</span><span>登出</span>';
  }

  function render() {
    var views = state.mode === 'admin' ? global.AdminViews : global.StoreViews;
    var fn = views.pages[state.page];
    main.innerHTML = fn ? fn(state) : '';
    renderNav();
    var label = (NAV[state.mode].items.filter(function (i) { return i[0] === state.page; })[0] || [])[1] || '';
    announcer.textContent = '目前畫面：' + (state.mode === 'admin' ? '後台' : '前台') + ' ' + label;
    document.title = 'DoSelect 懂選｜' + label + '（視覺預覽）';
    window.scrollTo({ top: 0, behavior: 'auto' });
  }

  function go(path) {
    location.hash = '#/' + path;
  }

  function onRoute() {
    var r = parseHash();
    var changedPage = r.mode !== state.mode || r.page !== state.page;
    state.mode = r.mode;
    state.page = r.page;
    if (changedPage) {
      state.ticketId = null;
      state.orderId = null;
      state.caseId = null;
      state.caseClaimedByMe = null;
      state.returnId = null;
      state.supportTab = 'faq';
      state.cartStep = 1;
      state.returnStep = 1;
    }
    render();
  }

  /* ---------- 示意用小提示（不使用遮罩、不阻擋操作） ---------- */

  var toastTimer = null;
  function toast(message) {
    var box = document.getElementById('preview-toast');
    if (!box) {
      box = document.createElement('div');
      box.id = 'preview-toast';
      box.setAttribute('role', 'status');
      box.setAttribute('aria-live', 'polite');
      box.style.position = 'fixed';
      box.style.insetInlineEnd = 'var(--space-5)';
      box.style.insetBlockEnd = 'var(--space-5)';
      box.style.zIndex = 'var(--z-toast)';
      box.style.maxWidth = '320px';
      box.style.padding = 'var(--space-3) var(--space-4)';
      box.style.border = '1px solid var(--color-mint-line)';
      box.style.borderRadius = 'var(--radius-md)';
      box.style.background = 'var(--color-mint-soft)';
      box.style.color = 'var(--color-navy)';
      box.style.fontSize = 'var(--fs-caption)';
      box.style.boxShadow = 'var(--shadow-md)';
      document.body.appendChild(box);
    }
    box.textContent = message;
    box.hidden = false;
    if (toastTimer) { clearTimeout(toastTimer); }
    toastTimer = setTimeout(function () { box.hidden = true; }, 2600);
  }

  /* ---------- 事件委派 ---------- */

  document.addEventListener('click', function (event) {
    var t = event.target.closest('[data-go],[data-toast],[data-filter],[data-page],[data-clear-filters],' +
      '[data-sort],[data-cart-step],[data-return-step],[data-support-tab],[data-ticket],[data-ticket-close],' +
      '[data-order],[data-case],[data-case-close],[data-case-claim],[data-return],[data-return-close]');
    if (!t) { return; }

    if (t.hasAttribute('data-go')) { go(t.getAttribute('data-go')); return; }

    if (t.hasAttribute('data-filter')) {
      var group = t.getAttribute('data-filter');
      var value = t.getAttribute('data-value');
      state.products[group] = state.products[group] === value ? '' : value;
      state.products.page = 1;
      render();
      return;
    }

    if (t.hasAttribute('data-clear-filters')) {
      state.products = { q: '', purpose: '', budget: '', category: '', sort: 'default', page: 1 };
      render();
      return;
    }

    if (t.hasAttribute('data-page')) {
      state.products.page = Number(t.getAttribute('data-page'));
      render();
      return;
    }

    if (t.hasAttribute('data-cart-step')) {
      var cs = Number(t.getAttribute('data-cart-step'));
      if (cs === 4) {
        t.classList.add('is-loading');
        t.textContent = '處理中';
        setTimeout(function () { state.cartStep = 4; render(); }, 700);
      } else {
        state.cartStep = cs;
        render();
      }
      return;
    }

    if (t.hasAttribute('data-return-step')) {
      state.returnStep = Number(t.getAttribute('data-return-step'));
      render();
      return;
    }

    if (t.hasAttribute('data-support-tab')) {
      state.supportTab = t.getAttribute('data-support-tab');
      state.ticketId = null;
      render();
      return;
    }

    if (t.hasAttribute('data-ticket')) { state.ticketId = t.getAttribute('data-ticket'); render(); focusDetail(); return; }
    if (t.hasAttribute('data-ticket-close')) { state.ticketId = null; render(); return; }
    if (t.hasAttribute('data-order')) { state.orderId = t.getAttribute('data-order'); render(); return; }

    if (t.hasAttribute('data-case')) {
      state.caseId = t.getAttribute('data-case');
      state.caseClaimedByMe = null;
      render();
      focusDetail();
      return;
    }
    if (t.hasAttribute('data-case-close')) { state.caseId = null; state.caseClaimedByMe = null; render(); return; }
    if (t.hasAttribute('data-case-claim')) {
      state.caseClaimedByMe = t.getAttribute('data-case-claim') === 'true';
      render();
      toast(state.caseClaimedByMe ? '已承接此案件（示意）' : '已取消承接（示意）');
      return;
    }

    if (t.hasAttribute('data-return')) { state.returnId = t.getAttribute('data-return'); render(); return; }
    if (t.hasAttribute('data-return-close')) { state.returnId = null; render(); return; }

    if (t.hasAttribute('data-toast')) { toast(t.getAttribute('data-toast')); }
  });

  /* 開啟詳細面板時把焦點移到詳細標題，關閉後焦點回主內容區。 */
  function focusDetail() {
    var heading = main.querySelector('.split__detail-head h3');
    if (heading) {
      heading.setAttribute('tabindex', '-1');
      heading.focus();
    }
  }

  document.addEventListener('input', function (event) {
    var el = event.target;
    if (el.matches('[data-filter-q]')) {
      state.products.q = el.value;
      state.products.page = 1;
      render();
      var again = main.querySelector('[data-filter-q]');
      if (again) {
        again.focus();
        again.setSelectionRange(again.value.length, again.value.length);
      }
    }
  });

  document.addEventListener('change', function (event) {
    if (event.target.matches('[data-sort]')) {
      state.products.sort = event.target.value;
      render();
    }
  });

  navToggle.addEventListener('click', function () {
    var open = navToggle.getAttribute('aria-expanded') === 'true';
    navToggle.setAttribute('aria-expanded', String(!open));
    nav.hidden = open;
  });

  window.addEventListener('hashchange', onRoute);

  if (!location.hash) { location.hash = '#/store/home'; }
  onRoute();
})(window);
