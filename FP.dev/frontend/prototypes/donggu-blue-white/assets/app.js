/* DoSelect 懂選 — UI 操作展示原型
 * Plain HTML/CSS/JS, no build step, no network requests, no external modules.
 * All data below is mock/demo data only. Nothing here is submitted anywhere.
 */
(function () {
  "use strict";

  var MASCOT = "assets/images/donggu-official.png";

  /* ---------------------------------------------------------------------
   * Route registry
   * ------------------------------------------------------------------- */

  var ROUTES = [
    { id: "home", label: "首頁", group: "shopping" },
    { id: "purpose", label: "用途選擇", group: "shopping" },
    { id: "budget", label: "預算設定", group: "shopping" },
    { id: "recommendations", label: "推薦結果", group: "shopping" },
    { id: "products", label: "商品列表", group: "shopping" },
    { id: "product-detail", label: "商品詳情", group: "shopping" },
    { id: "compare", label: "比較商品", group: "shopping" },
    { id: "cart", label: "購物車／結帳步驟", group: "shopping" },
    { id: "member", label: "會員中心", group: "shopping" },
    { id: "support-home", label: "客服中心", group: "support" },
    { id: "faq", label: "FAQ", group: "support" },
    { id: "contact", label: "聯絡客服／建立案件", group: "support" },
    { id: "tickets", label: "客服紀錄／案件詳情", group: "support" },
    { id: "return", label: "退貨退款申請", group: "support" },
    { id: "report", label: "檢舉／申訴入口", group: "support" }
  ];

  var ROUTE_IDS = ROUTES.map(function (r) { return r.id; });

  /* ---------------------------------------------------------------------
   * Mock data
   * ------------------------------------------------------------------- */

  var PURPOSES = [
    { id: "office", label: "文書上網", icon: "💻", desc: "日常瀏覽、文書處理、線上會議" },
    { id: "entertainment", label: "追劇娛樂", icon: "🎬", desc: "影音串流、遊戲娛樂、聽歌追劇" },
    { id: "creative", label: "攝影創作", icon: "📷", desc: "拍照錄影、修圖剪輯、內容創作" },
    { id: "smarthome", label: "智慧家電", icon: "🏠", desc: "居家自動化與生活便利小物" }
  ];

  var BUDGETS = [
    { id: "b1", label: "3,000 元以下", min: 0, max: 3000 },
    { id: "b2", label: "3,000～8,000 元", min: 3000, max: 8000 },
    { id: "b3", label: "8,000～15,000 元", min: 8000, max: 15000 },
    { id: "b4", label: "15,000 元以上", min: 15000, max: Infinity }
  ];

  var CATEGORIES = ["筆電", "耳機", "相機", "智慧家電"];

  var PRODUCTS = [
    {
      id: "p1", name: "晴空 Air 13 輕薄筆電", category: "筆電", price: 24900,
      purposes: ["office", "entertainment"], rating: 4.6, icon: "💻",
      summary: "1.15kg 輕巧機身，續航約 14 小時，適合外出辦公與線上會議。",
      specs: [["處理器", "八核心 節能版"], ["記憶體", "16GB"], ["儲存", "512GB SSD"], ["重量", "1.15kg"], ["電池續航", "約 14 小時"]]
    },
    {
      id: "p2", name: "銳視 Pro 15 創作筆電", category: "筆電", price: 42900,
      purposes: ["creative", "office"], rating: 4.8, icon: "💻",
      summary: "高色準螢幕與獨立顯示晶片，勝任修圖、剪輯與多工作業。",
      specs: [["處理器", "十核心 效能版"], ["記憶體", "32GB"], ["儲存", "1TB SSD"], ["螢幕", "15.6 吋 100% sRGB"], ["顯示晶片", "獨立顯示晶片 8GB"]]
    },
    {
      id: "p3", name: "輕巧 Go 11 平價筆電", category: "筆電", price: 13900,
      purposes: ["office", "entertainment"], rating: 4.2, icon: "💻",
      summary: "入門好攜帶，滿足瀏覽網頁、文書處理與追劇需求。",
      specs: [["處理器", "四核心 入門版"], ["記憶體", "8GB"], ["儲存", "256GB SSD"], ["重量", "1.3kg"], ["電池續航", "約 10 小時"]]
    },
    {
      id: "p4", name: "悅聽 Buds 主動降噪耳機", category: "耳機", price: 4990,
      purposes: ["entertainment", "office"], rating: 4.5, icon: "🎧",
      summary: "主動降噪配合通透模式，通勤與會議都適用。",
      specs: [["類型", "真無線入耳式"], ["降噪", "主動降噪 + 通透模式"], ["續航", "單次 6 小時／含充電盒 24 小時"], ["防水", "IPX4"]]
    },
    {
      id: "p5", name: "臨場 Studio 耳罩耳機", category: "耳機", price: 8990,
      purposes: ["entertainment", "creative"], rating: 4.6, icon: "🎧",
      summary: "寬廣音場與精準低頻，適合影音娛樂與混音參考。",
      specs: [["類型", "耳罩式，有線／藍牙雙模"], ["降噪", "混合式主動降噪"], ["續航", "約 30 小時"], ["重量", "260g"]]
    },
    {
      id: "p6", name: "晴視 Snap 隨行相機", category: "相機", price: 18900,
      purposes: ["creative"], rating: 4.7, icon: "📷",
      summary: "一鍵美肌與穩定防手震，隨手拍出質感內容。",
      specs: [["感光元件", "1 吋"], ["變焦", "3 倍光學"], ["錄影", "4K 30fps"], ["防手震", "5 軸機身防震"]]
    },
    {
      id: "p7", name: "掌鏡 Vlog 4K 相機", category: "相機", price: 26900,
      purposes: ["creative", "entertainment"], rating: 4.5, icon: "📷",
      summary: "翻轉螢幕與麥克風收音優化，創作者的隨行夥伴。",
      specs: [["感光元件", "1 吋"], ["翻轉螢幕", "180 度自拍翻轉"], ["錄影", "4K 30fps"], ["收音", "內建指向性麥克風"]]
    },
    {
      id: "p8", name: "涼夏 Cool 智慧循環扇", category: "智慧家電", price: 2490,
      purposes: ["smarthome"], rating: 4.3, icon: "🏠",
      summary: "手機 App 遠端控制，睡眠模式自動降低噪音。",
      specs: [["控制方式", "App／語音"], ["風量段數", "12 段"], ["噪音", "睡眠模式低於 30 分貝"], ["定時", "1-12 小時"]]
    },
    {
      id: "p9", name: "安心 Home 智慧插座三入組", category: "智慧家電", price: 1290,
      purposes: ["smarthome", "office"], rating: 4.4, icon: "🏠",
      summary: "遠端開關與用電量監測，居家辦公更安心。",
      specs: [["控制方式", "App 遠端控制"], ["監測", "即時用電量"], ["相容", "語音助理相容"], ["安裝", "免施工插座式"]]
    }
  ];

  var FAQ_CATEGORIES = ["全部", "寄送物流", "退換貨", "帳號會員", "商品保固"];

  var FAQ_ITEMS = [
    { id: "f1", category: "寄送物流", q: "訂單什麼時候會出貨？", a: "一般商品於付款完成後 1-2 個工作天內出貨，實際到貨時間依配送地區而定（此為示意內容）。" },
    { id: "f2", category: "寄送物流", q: "可以指定到店取貨嗎？", a: "結帳步驟中可選擇門市取貨或宅配到府，本原型僅示意選項，不會實際建立配送單。" },
    { id: "f3", category: "退換貨", q: "退貨期限是幾天？", a: "商品到貨後 7 天內可申請退貨，詳細條件請見退貨退款申請頁面的示意流程。" },
    { id: "f4", category: "退換貨", q: "退款會退到哪裡？", a: "退款將原路退回原付款方式，實際天數依付款機構而定（此為示意內容）。" },
    { id: "f5", category: "帳號會員", q: "忘記密碼怎麼辦？", a: "可於登入頁面使用「忘記密碼」重設，本原型未串接真實帳號系統。" },
    { id: "f6", category: "帳號會員", q: "會員等級如何計算？", a: "依近 12 個月累計消費金額計算等級，等級對應之權益顯示於會員中心（示意資料）。" },
    { id: "f7", category: "商品保固", q: "商品保固期多久？", a: "多數 3C 商品享 1 年保固，詳細保固條件請見各商品詳情頁的規格說明。" },
    { id: "f8", category: "商品保固", q: "保固內容包含人為損壞嗎？", a: "保固範圍不含人為損壞、進水或不當使用造成的故障（此為示意內容）。" }
  ];

  var TICKETS = [
    {
      id: "T001", subject: "訂單遲遲未出貨，想確認進度", status: "unreplied", updatedAt: "2026-08-18",
      preview: "訂單已經下單三天了還沒出貨，想請客服協助確認。",
      messages: [
        { from: "customer", text: "您好，我的訂單已經下單三天了還沒出貨，想請客服協助確認狀態，謝謝。", time: "2026-08-18 10:12" }
      ]
    },
    {
      id: "T002", subject: "商品到貨外盒破損", status: "replied", updatedAt: "2026-08-17",
      preview: "收到商品時外盒有明顯破損，想詢問可否更換。",
      messages: [
        { from: "customer", text: "您好，今天收到的商品外盒有明顯壓損，內容物看起來完好，但想先確認是否需要退換。", time: "2026-08-16 09:40" },
        { from: "agent", text: "您好，感謝您的告知，建議先拍照保留紀錄，我們可以為您安排換貨，將由客服人員與您聯繫。", time: "2026-08-17 14:05" }
      ]
    },
    {
      id: "T003", subject: "詢問智慧插座是否支援語音助理", status: "closed", updatedAt: "2026-08-10",
      preview: "想確認安心 Home 智慧插座是否能與常見語音助理搭配使用。",
      messages: [
        { from: "customer", text: "請問安心 Home 智慧插座三入組可以搭配常見語音助理使用嗎？", time: "2026-08-09 20:22" },
        { from: "agent", text: "您好，該商品支援主流語音助理整合，設定方式可參考商品詳情頁的規格說明。", time: "2026-08-10 11:15" },
        { from: "customer", text: "了解，謝謝說明，這樣我可以放心下單了。", time: "2026-08-10 11:40" }
      ]
    },
    {
      id: "T004", subject: "想更改收件地址", status: "replied", updatedAt: "2026-08-15",
      preview: "訂單尚未出貨，想請客服協助更改收件地址。",
      messages: [
        { from: "customer", text: "您好，訂單目前應該還沒出貨，可以協助我更改收件地址嗎？", time: "2026-08-14 16:02" },
        { from: "agent", text: "您好，已為您確認訂單仍在備貨中，可以協助更改，請提供新的收件地址即可。", time: "2026-08-15 09:30" }
      ]
    },
    {
      id: "T005", subject: "詢問筆電是否有教育優惠", status: "closed", updatedAt: "2026-08-05",
      preview: "想詢問晴空 Air 13 是否有學生教育優惠方案。",
      messages: [
        { from: "customer", text: "請問晴空 Air 13 輕薄筆電有學生教育優惠嗎？", time: "2026-08-04 13:10" },
        { from: "agent", text: "您好，目前該機型未提供額外教育優惠，建議留意站上不定期的促銷活動。", time: "2026-08-05 10:00" }
      ]
    }
  ];

  var MOCK_ORDERS = [
    { id: "O2026081201", date: "2026-08-12", item: "晴空 Air 13 輕薄筆電", amount: 24900, status: "已送達" },
    { id: "O2026080502", date: "2026-08-05", item: "悅聽 Buds 主動降噪耳機", amount: 4990, status: "已送達" },
    { id: "O2026072001", date: "2026-07-20", item: "涼夏 Cool 智慧循環扇", amount: 2490, status: "已完成" }
  ];

  var RETURN_STEP_LABELS = ["選擇訂單", "填寫原因", "出貨方式", "確認送出"];
  var RETURN_REASONS = ["商品瑕疵", "與描述不符", "不符合需求", "其他原因"];
  var RETURN_METHODS = [
    { id: "pickup", label: "到府收件" },
    { id: "store", label: "超商寄件" }
  ];

  var REPORT_TYPES = ["商品描述不實", "疑似仿冒商品", "客服應對不當", "其他問題"];

  /* ---------------------------------------------------------------------
   * State
   * ------------------------------------------------------------------- */

  var state = {
    currentView: "home",
    currentParams: new URLSearchParams(),
    selectedPurpose: null,
    selectedBudget: null,
    budgetRangeValue: 8000,
    productFilters: { category: "全部", sort: "popular" },
    cartItems: [],
    cartStep: 1,
    cartDelivery: "standard",
    cartPayment: "card",
    compareIds: [],
    faqOpenIds: {},
    faqCategory: "全部",
    faqQuery: "",
    selectedTicketId: null,
    ticketFilter: "全部",
    returnStep: 1,
    returnOrderId: null,
    returnReason: null,
    returnNote: "",
    returnMethod: null,
    returnSubmitted: false,
    memberTab: "orders"
  };

  var historyStack = [];

  /* ---------------------------------------------------------------------
   * Helpers
   * ------------------------------------------------------------------- */

  function esc(str) {
    return String(str == null ? "" : str).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  function formatPrice(n) {
    return "NT$ " + n.toLocaleString("zh-Hant-TW");
  }

  function findProduct(id) {
    return PRODUCTS.filter(function (p) { return p.id === id; })[0];
  }

  function findPurpose(id) {
    return PURPOSES.filter(function (p) { return p.id === id; })[0];
  }

  function findBudget(id) {
    return BUDGETS.filter(function (b) { return b.id === id; })[0];
  }

  function findTicket(id) {
    return TICKETS.filter(function (t) { return t.id === id; })[0];
  }

  function nearestBudgetTier(value) {
    for (var i = 0; i < BUDGETS.length; i++) {
      if (value >= BUDGETS[i].min && value < (BUDGETS[i].max === Infinity ? Infinity : BUDGETS[i].max)) {
        return BUDGETS[i].id;
      }
    }
    return BUDGETS[BUDGETS.length - 1].id;
  }

  function getRecommendations() {
    var purposeId = state.selectedPurpose;
    var budget = findBudget(state.selectedBudget) || BUDGETS[1];
    var pool = PRODUCTS.slice();

    var matches = pool.filter(function (p) {
      var purposeOk = !purposeId || p.purposes.indexOf(purposeId) !== -1;
      var budgetOk = p.price >= budget.min && p.price <= budget.max;
      return purposeOk && budgetOk;
    });

    if (matches.length < 3) {
      var purposeOnly = pool.filter(function (p) {
        return (!purposeId || p.purposes.indexOf(purposeId) !== -1) && matches.indexOf(p) === -1;
      });
      matches = matches.concat(purposeOnly);
    }

    if (matches.length < 3) {
      var rest = pool
        .slice()
        .sort(function (a, b) { return b.rating - a.rating; })
        .filter(function (p) { return matches.indexOf(p) === -1; });
      matches = matches.concat(rest);
    }

    return matches.slice(0, 3);
  }

  function cartTotalCount() {
    return state.cartItems.reduce(function (sum, item) { return sum + item.qty; }, 0);
  }

  function cartTotalAmount() {
    return state.cartItems.reduce(function (sum, item) {
      var p = findProduct(item.productId);
      return sum + (p ? p.price * item.qty : 0);
    }, 0);
  }

  function addToCart(productId) {
    var existing = state.cartItems.filter(function (i) { return i.productId === productId; })[0];
    if (existing) {
      existing.qty += 1;
    } else {
      state.cartItems.push({ productId: productId, qty: 1 });
    }
  }

  function toggleCompare(productId) {
    var idx = state.compareIds.indexOf(productId);
    if (idx !== -1) {
      state.compareIds.splice(idx, 1);
      return "removed";
    }
    if (state.compareIds.length >= 3) {
      return "full";
    }
    state.compareIds.push(productId);
    return "added";
  }

  /* ---------------------------------------------------------------------
   * Toast
   * ------------------------------------------------------------------- */

  var toastTimer = null;
  function showToast(message) {
    var el = document.getElementById("toast");
    if (!el) return;
    el.textContent = message;
    el.classList.add("is-visible");
    if (toastTimer) window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(function () {
      el.classList.remove("is-visible");
    }, 3200);
  }

  /* ---------------------------------------------------------------------
   * Routing
   * ------------------------------------------------------------------- */

  function parseHash() {
    var raw = window.location.hash.replace(/^#\/?/, "");
    var parts = raw.split("?");
    var view = parts[0] || "home";
    if (ROUTE_IDS.indexOf(view) === -1) view = "home";
    var params = new URLSearchParams(parts[1] || "");
    return { view: view, params: params };
  }

  function buildHash(view, params) {
    var qs = params && Object.keys(params).length ? "?" + new URLSearchParams(params).toString() : "";
    return "#/" + view + qs;
  }

  function goTo(view, params) {
    params = params || {};
    if (ROUTE_IDS.indexOf(view) === -1) view = "home";
    if (state.currentView && state.currentView !== view) {
      historyStack.push({ view: state.currentView, params: paramsToObject(state.currentParams) });
      if (historyStack.length > 30) historyStack.shift();
    }
    var next = buildHash(view, params);
    if (window.location.hash === next) {
      renderRoute();
    } else {
      window.location.hash = next;
    }
  }

  function goBack() {
    var prev = historyStack.pop();
    if (prev) {
      var next = buildHash(prev.view, prev.params);
      if (window.location.hash === next) {
        renderRoute();
      } else {
        window.location.hash = next;
      }
    } else {
      window.location.hash = buildHash("home", {});
    }
  }

  function paramsToObject(params) {
    var obj = {};
    params.forEach(function (value, key) { obj[key] = value; });
    return obj;
  }

  function renderRoute() {
    var parsed = parseHash();
    state.currentView = parsed.view;
    state.currentParams = parsed.params;

    if (parsed.view === "tickets" && parsed.params.get("id")) {
      state.selectedTicketId = parsed.params.get("id");
    }
    if (parsed.view === "product-detail" && !parsed.params.get("id") && PRODUCTS.length) {
      parsed.params.set("id", PRODUCTS[0].id);
    }

    var route = ROUTES.filter(function (r) { return r.id === parsed.view; })[0] || ROUTES[0];
    var root = document.getElementById("view-root");
    root.innerHTML = renderView(route.id, parsed.params);

    updateChrome(route);
    runAfterRender(route.id, parsed.params);

    var main = document.getElementById("main-content");
    if (main) main.focus();
    window.scrollTo(0, 0);
  }

  function updateChrome(route) {
    document.title = "DoSelect 懂選｜" + route.label;

    var label = document.getElementById("current-view-label");
    if (label) label.textContent = "目前畫面：" + route.label;

    var announcer = document.getElementById("route-announcer");
    if (announcer) announcer.textContent = "已切換至 " + route.label + " 畫面";

    var backBtn = document.getElementById("back-btn");
    if (backBtn) backBtn.disabled = historyStack.length === 0;

    var navButtons = document.querySelectorAll(".primary-nav [data-nav]");
    for (var i = 0; i < navButtons.length; i++) {
      var el = navButtons[i];
      if (el.getAttribute("data-nav") === route.id) {
        el.setAttribute("aria-current", "page");
      } else {
        el.removeAttribute("aria-current");
      }
    }

    var badge = document.getElementById("cart-badge");
    var cartIconBtn = document.getElementById("cart-icon-btn");
    var count = cartTotalCount();
    if (badge) badge.textContent = String(count);
    if (cartIconBtn) cartIconBtn.setAttribute("aria-label", "購物車，目前 " + count + " 件商品");
  }

  /* ---------------------------------------------------------------------
   * View renderers
   * ------------------------------------------------------------------- */

  function renderView(id, params) {
    switch (id) {
      case "home": return viewHome();
      case "purpose": return viewPurpose();
      case "budget": return viewBudget();
      case "recommendations": return viewRecommendations();
      case "products": return viewProducts();
      case "product-detail": return viewProductDetail(params.get("id"));
      case "compare": return viewCompare();
      case "cart": return viewCart();
      case "member": return viewMember();
      case "support-home": return viewSupportHome();
      case "faq": return viewFaq();
      case "contact": return viewContact();
      case "tickets": return viewTickets();
      case "return": return viewReturn();
      case "report": return viewReport();
      default: return viewHome();
    }
  }

  function runAfterRender(id) {
    if (id === "faq") setupFaqSearch();
  }

  function productCard(p, options) {
    options = options || {};
    var inCompare = state.compareIds.indexOf(p.id) !== -1;
    return (
      '<div class="card product-card">' +
      '<div class="product-thumb" aria-hidden="true">' + p.icon + "</div>" +
      '<span class="tag">' + esc(p.category) + "</span>" +
      '<h3 class="product-name">' + esc(p.name) + "</h3>" +
      '<p class="inline-note">' + esc(p.summary) + "</p>" +
      '<p class="product-price">' + formatPrice(p.price) + "</p>" +
      (options.reason ? '<p class="reason-box">' + esc(options.reason) + "</p>" : "") +
      '<div class="product-actions">' +
      '<button type="button" class="btn btn-secondary" data-nav="product-detail" data-id="' + p.id + '">查看詳情</button>' +
      '<button type="button" class="btn btn-primary" data-action="add-to-cart" data-id="' + p.id + '">加入購物車</button>' +
      '<button type="button" class="btn btn-ghost" data-action="toggle-compare" data-id="' + p.id + '" aria-pressed="' + inCompare + '">' +
      (inCompare ? "✓ 已加入比較" : "加入比較") +
      "</button>" +
      "</div>" +
      "</div>"
    );
  }

  function viewHome() {
    return (
      '<section class="hero">' +
      "<div>" +
      "<h1>不知道怎麼選？讓懂懂幫你一步步找到適合的商品</h1>" +
      '<p class="view-lede">DoSelect 懂選是一站式選購小幫手，先告訴我們用途與預算，就能得到清楚易懂的推薦理由。</p>' +
      '<button type="button" class="btn btn-primary" data-nav="purpose">立即開始選購</button> ' +
      '<button type="button" class="btn btn-secondary" data-nav="products">直接逛商品</button>' +
      "</div>" +
      '<img class="hero-mascot" src="' + MASCOT + '" alt="Donggu 懂懂，DoSelect 懂選吉祥物，微笑揮手歡迎" />' +
      "</section>" +
      '<section aria-labelledby="steps-h">' +
      '<h2 id="steps-h">三步驟找到適合的商品</h2>' +
      '<div class="step-flow">' +
      '<div class="step-flow-item"><span class="step-num">1</span><h3>我想用來做什麼</h3><p class="inline-note">選擇文書、娛樂、創作或居家用途。</p></div>' +
      '<div class="step-flow-item"><span class="step-num">2</span><h3>我的預算是多少</h3><p class="inline-note">用區間或滑桿快速設定預算範圍。</p></div>' +
      '<div class="step-flow-item"><span class="step-num">3</span><h3>查看適合推薦</h3><p class="inline-note">獲得附理由的推薦商品清單。</p></div>' +
      "</div>" +
      '<button type="button" class="btn btn-primary" data-nav="purpose">開始選購流程</button>' +
      "</section>" +
      '<section aria-labelledby="cat-h">' +
      '<h2 id="cat-h">熱門分類</h2>' +
      '<div class="category-grid">' +
      CATEGORIES.map(function (c) {
        var icon = { 筆電: "💻", 耳機: "🎧", 相機: "📷", 智慧家電: "🏠" }[c];
        return (
          '<button type="button" class="category-card" data-action="filter-category-nav" data-category="' + esc(c) + '">' +
          '<span class="cat-icon" aria-hidden="true">' + icon + "</span>" + esc(c) +
          "</button>"
        );
      }).join("") +
      "</div>" +
      "</section>" +
      '<section aria-labelledby="reassure-h">' +
      '<h2 id="reassure-h">安心購物保障</h2>' +
      '<div class="reassurance-strip">' +
      '<div class="reassurance-item"><span aria-hidden="true">🚚</span><div><strong>快速到貨</strong><p class="inline-note">多數地區 1-2 個工作天送達（示意內容）。</p></div></div>' +
      '<div class="reassurance-item"><span aria-hidden="true">↩️</span><div><strong>7 天鑑賞期</strong><p class="inline-note">不符期待可申請退貨退款。</p></div></div>' +
      '<div class="reassurance-item"><span aria-hidden="true">🛟</span><div><strong>真人客服</strong><p class="inline-note">遇到問題可隨時建立案件詢問。</p></div></div>' +
      "</div>" +
      "</section>"
    );
  }

  function viewPurpose() {
    return (
      "<h1>用途選擇</h1>" +
      '<p class="view-lede">先告訴懂懂你主要想用來做什麼，我們會依此推薦合適的商品。</p>' +
      '<div class="purpose-grid" role="group" aria-label="選擇使用用途">' +
      PURPOSES.map(function (p) {
        var pressed = state.selectedPurpose === p.id;
        return (
          '<button type="button" class="selectable-card" data-action="select-purpose" data-id="' + p.id + '" aria-pressed="' + pressed + '">' +
          '<span class="sc-icon" aria-hidden="true">' + p.icon + "</span>" +
          "<strong>" + esc(p.label) + "</strong>" +
          '<p class="inline-note">' + esc(p.desc) + "</p>" +
          "</button>"
        );
      }).join("") +
      "</div>" +
      '<div class="wizard-actions">' +
      '<button type="button" class="btn btn-secondary" data-nav="home">回首頁</button>' +
      '<button type="button" class="btn btn-primary" data-nav="budget"' + (state.selectedPurpose ? "" : " disabled") + ">下一步：設定預算</button>" +
      "</div>"
    );
  }

  function viewBudget() {
    var purpose = findPurpose(state.selectedPurpose);
    return (
      "<h1>預算設定</h1>" +
      '<p class="view-lede">選擇一個大概的預算範圍，懂懂會依用途與預算篩選最合適的商品。</p>' +
      (purpose
        ? '<div class="selection-summary"><span aria-hidden="true">' + purpose.icon + '</span><span>已選用途：<strong>' + esc(purpose.label) + "</strong></span></div>"
        : '<div class="selection-summary">尚未選擇用途，<button type="button" class="btn btn-ghost" data-nav="purpose">回上一步選擇</button></div>') +
      '<div class="budget-grid" role="group" aria-label="選擇預算範圍">' +
      BUDGETS.map(function (b) {
        var pressed = state.selectedBudget === b.id;
        return (
          '<button type="button" class="selectable-card" data-action="select-budget" data-id="' + b.id + '" aria-pressed="' + pressed + '">' +
          '<span class="sc-icon" aria-hidden="true">💰</span>' +
          "<strong>" + esc(b.label) + "</strong>" +
          "</button>"
        );
      }).join("") +
      "</div>" +
      '<div class="card range-block">' +
      '<label for="budget-range">或用滑桿快速設定預算（約 NT$ <span id="budget-range-output">' + state.budgetRangeValue.toLocaleString("zh-Hant-TW") + "</span>）</label>" +
      '<input type="range" id="budget-range" min="0" max="30000" step="500" value="' + state.budgetRangeValue + '" />' +
      "</div>" +
      '<div class="wizard-actions">' +
      '<button type="button" class="btn btn-secondary" data-nav="purpose">上一步</button>' +
      '<button type="button" class="btn btn-primary" data-nav="recommendations"' + (state.selectedBudget ? "" : " disabled") + ">下一步：查看推薦</button>" +
      "</div>"
    );
  }

  function viewRecommendations() {
    var purpose = findPurpose(state.selectedPurpose);
    var budget = findBudget(state.selectedBudget);
    var list = getRecommendations();
    return (
      "<h1>推薦結果</h1>" +
      '<p class="view-lede">依你選擇的' +
      (purpose ? "「" + esc(purpose.label) + "」" : "用途") +
      "與" +
      (budget ? "「" + esc(budget.label) + "」" : "預算") +
      "，懂懂為你精選以下商品。</p>" +
      '<div class="product-grid">' +
      list
        .map(function (p) {
          var reason = "因為你選擇了「" + (purpose ? purpose.label : "一般用途") + "」與「" + (budget ? budget.label : "彈性預算") + "」，這款商品符合你的需求方向。";
          return productCard(p, { reason: reason });
        })
        .join("") +
      "</div>" +
      '<div class="wizard-actions">' +
      '<button type="button" class="btn btn-secondary" data-nav="budget">上一步：調整預算</button>' +
      '<button type="button" class="btn btn-secondary" data-nav="compare">前往比較商品</button>' +
      '<button type="button" class="btn btn-primary" data-nav="products">瀏覽完整商品列表</button>' +
      "</div>"
    );
  }

  function viewProducts() {
    var category = state.productFilters.category;
    var sort = state.productFilters.sort;
    var list = PRODUCTS.filter(function (p) { return category === "全部" || p.category === category; });

    if (sort === "price-asc") list = list.slice().sort(function (a, b) { return a.price - b.price; });
    else if (sort === "price-desc") list = list.slice().sort(function (a, b) { return b.price - a.price; });
    else list = list.slice().sort(function (a, b) { return b.rating - a.rating; });

    var categoryChips = ["全部"].concat(CATEGORIES);

    return (
      "<h1>商品列表</h1>" +
      '<p class="view-lede">瀏覽全部商品，並使用篩選與排序找到想要的類型。</p>' +
      '<div class="chip-row" role="group" aria-label="依分類篩選">' +
      categoryChips
        .map(function (c) {
          return (
            '<button type="button" class="chip" data-action="filter-category" data-category="' + esc(c) + '" aria-pressed="' + (category === c) + '">' +
            esc(c) +
            "</button>"
          );
        })
        .join("") +
      "</div>" +
      '<div class="chip-row" role="group" aria-label="排序方式">' +
      '<button type="button" class="chip" data-action="sort-products" data-sort="popular" aria-pressed="' + (sort === "popular") + '">熱門優先</button>' +
      '<button type="button" class="chip" data-action="sort-products" data-sort="price-asc" aria-pressed="' + (sort === "price-asc") + '">價格由低到高</button>' +
      '<button type="button" class="chip" data-action="sort-products" data-sort="price-desc" aria-pressed="' + (sort === "price-desc") + '">價格由高到低</button>' +
      "</div>" +
      (list.length
        ? '<div class="product-grid">' + list.map(function (p) { return productCard(p); }).join("") + "</div>"
        : '<div class="empty-state"><img src="' + MASCOT + '" alt="" /><p>這個分類目前沒有商品，換個分類看看吧。</p></div>')
    );
  }

  function viewProductDetail(id) {
    var p = findProduct(id) || PRODUCTS[0];
    var purposeLabels = p.purposes.map(function (id2) { var pu = findPurpose(id2); return pu ? pu.label : id2; }).join("、");
    var inCompare = state.compareIds.indexOf(p.id) !== -1;

    return (
      '<nav class="inline-note" aria-label="返回商品列表">' +
      '<button type="button" class="btn btn-ghost" data-nav="products">← 回商品列表</button>' +
      "</nav>" +
      '<div class="detail-layout">' +
      '<div class="detail-gallery" aria-hidden="true">' + p.icon + "</div>" +
      "<div>" +
      '<span class="tag">' + esc(p.category) + "</span>" +
      "<h1>" + esc(p.name) + "</h1>" +
      '<p class="product-price">' + formatPrice(p.price) + "</p>" +
      '<p class="view-lede">' + esc(p.summary) + "</p>" +
      '<p><strong>適合族群：</strong>' + esc(purposeLabels || "多用途") + "</p>" +
      '<div class="product-actions">' +
      '<button type="button" class="btn btn-primary" data-action="add-to-cart" data-id="' + p.id + '">加入購物車</button>' +
      '<button type="button" class="btn btn-secondary" data-action="toggle-compare" data-id="' + p.id + '" aria-pressed="' + inCompare + '">' +
      (inCompare ? "✓ 已加入比較" : "加入比較") +
      "</button>" +
      "</div>" +
      "<details>" +
      "<summary>查看完整規格</summary>" +
      '<div class="table-scroll">' +
      '<table class="spec-table"><tbody>' +
      p.specs.map(function (s) { return "<tr><th>" + esc(s[0]) + "</th><td>" + esc(s[1]) + "</td></tr>"; }).join("") +
      "</tbody></table>" +
      "</div>" +
      "</details>" +
      "</div>" +
      "</div>"
    );
  }

  function viewCompare() {
    var items = state.compareIds.map(findProduct).filter(Boolean);

    if (items.length < 2) {
      return (
        "<h1>比較商品</h1>" +
        '<p class="view-lede">從商品列表或推薦結果選擇 2～3 項商品加入比較，即可在此並排檢視。</p>' +
        '<div class="empty-state"><img src="' + MASCOT + '" alt="" /><p>目前已選 ' + items.length + ' 項，至少需要 2 項才能比較喔。</p>' +
        '<button type="button" class="btn btn-primary" data-nav="products">前往商品列表挑選</button>' +
        "</div>"
      );
    }

    var fieldLabels = ["分類", "價格", "評分", "適合用途"].concat(items[0].specs.map(function (s) { return s[0]; }));

    function fieldValue(p, label, index) {
      if (label === "分類") return p.category;
      if (label === "價格") return formatPrice(p.price);
      if (label === "評分") return p.rating.toFixed(1) + " / 5";
      if (label === "適合用途") return p.purposes.map(function (id2) { var pu = findPurpose(id2); return pu ? pu.label : id2; }).join("、");
      var spec = p.specs.filter(function (s) { return s[0] === label; })[0];
      return spec ? spec[1] : "—";
    }

    return (
      "<h1>比較商品</h1>" +
      '<p class="view-lede">並排檢視已選商品的重點規格，方便快速決定。</p>' +
      '<div class="chip-row">' +
      items
        .map(function (p) {
          return '<span class="chip" aria-pressed="true">' + esc(p.name) + ' <button type="button" class="btn-ghost" data-action="toggle-compare" data-id="' + p.id + '" aria-label="移除 ' + esc(p.name) + '">✕</button></span>';
        })
        .join("") +
      "</div>" +
      '<div class="table-scroll">' +
      '<table class="compare-table">' +
      "<thead><tr><th>比較項目</th>" +
      items.map(function (p) { return "<th>" + esc(p.name) + "</th>"; }).join("") +
      "</tr></thead>" +
      "<tbody>" +
      fieldLabels
        .map(function (label) {
          return "<tr><th>" + esc(label) + "</th>" + items.map(function (p) { return "<td>" + esc(fieldValue(p, label)) + "</td>"; }).join("") + "</tr>";
        })
        .join("") +
      "</tbody>" +
      "</table>" +
      "</div>"
    );
  }

  function viewCart() {
    var step = state.cartStep;
    var items = state.cartItems;

    var stepper =
      '<div class="stepper" role="list" aria-label="結帳步驟">' +
      ["購物車", "選擇付款", "完成確認"]
        .map(function (label, i) {
          var n = i + 1;
          var cls = n === step ? "is-active" : n < step ? "is-done" : "";
          return '<div class="stepper-item ' + cls + '" role="listitem">步驟 ' + n + "．" + label + "</div>";
        })
        .join("") +
      "</div>";

    if (!items.length) {
      return (
        "<h1>購物車／結帳步驟</h1>" +
        stepper +
        '<div class="empty-state"><img src="' + MASCOT + '" alt="" /><p>購物車目前是空的，先去商品列表逛逛吧。</p>' +
        '<button type="button" class="btn btn-primary" data-nav="products">前往商品列表</button>' +
        "</div>"
      );
    }

    if (step === 1) {
      return (
        "<h1>購物車／結帳步驟</h1>" +
        stepper +
        '<div class="card">' +
        items
          .map(function (item) {
            var p = findProduct(item.productId);
            if (!p) return "";
            return (
              '<div class="cart-line">' +
              '<div class="cart-line-thumb" aria-hidden="true">' + p.icon + "</div>" +
              '<div style="flex:1">' +
              "<strong>" + esc(p.name) + "</strong>" +
              '<p class="inline-note">' + formatPrice(p.price) + "</p>" +
              "</div>" +
              '<div class="qty-control">' +
              '<button type="button" data-action="cart-qty" data-id="' + p.id + '" data-delta="-1" aria-label="減少 ' + esc(p.name) + ' 數量">－</button>' +
              '<span aria-live="polite">' + item.qty + "</span>" +
              '<button type="button" data-action="cart-qty" data-id="' + p.id + '" data-delta="1" aria-label="增加 ' + esc(p.name) + ' 數量">＋</button>' +
              "</div>" +
              '<button type="button" class="btn btn-ghost" data-action="cart-remove" data-id="' + p.id + '">移除</button>' +
              "</div>"
            );
          })
          .join("") +
        '<p style="text-align:right;margin-top:14px;font-weight:800">小計：' + formatPrice(cartTotalAmount()) + "</p>" +
        "</div>" +
        '<div class="wizard-actions">' +
        '<button type="button" class="btn btn-secondary" data-nav="products">繼續購物</button>' +
        '<button type="button" class="btn btn-primary" data-action="cart-next">下一步：選擇付款</button>' +
        "</div>"
      );
    }

    if (step === 2) {
      return (
        "<h1>購物車／結帳步驟</h1>" +
        stepper +
        '<div class="card">' +
        "<h2>配送方式</h2>" +
        '<div class="radio-row" role="radiogroup" aria-label="配送方式">' +
        '<label class="radio-option"><input type="radio" name="delivery" data-action="select-delivery" value="standard"' + (state.cartDelivery === "standard" ? " checked" : "") + " /> 宅配到府</label>" +
        '<label class="radio-option"><input type="radio" name="delivery" data-action="select-delivery" value="store"' + (state.cartDelivery === "store" ? " checked" : "") + " /> 超商取貨</label>" +
        "</div>" +
        "<h2>付款方式</h2>" +
        '<div class="radio-row" role="radiogroup" aria-label="付款方式">' +
        '<label class="radio-option"><input type="radio" name="payment" data-action="select-payment" value="card"' + (state.cartPayment === "card" ? " checked" : "") + " /> 信用卡</label>" +
        '<label class="radio-option"><input type="radio" name="payment" data-action="select-payment" value="transfer"' + (state.cartPayment === "transfer" ? " checked" : "") + " /> 銀行轉帳</label>" +
        '<label class="radio-option"><input type="radio" name="payment" data-action="select-payment" value="cod"' + (state.cartPayment === "cod" ? " checked" : "") + " /> 貨到付款</label>" +
        "</div>" +
        '<p class="inline-note">此步驟僅為示意，不會實際請款。</p>' +
        "</div>" +
        '<div class="wizard-actions">' +
        '<button type="button" class="btn btn-secondary" data-action="cart-prev">上一步</button>' +
        '<button type="button" class="btn btn-primary" data-action="cart-next">下一步：完成確認</button>' +
        "</div>"
      );
    }

    return (
      "<h1>購物車／結帳步驟</h1>" +
      stepper +
      '<div class="card">' +
      '<h2>訂單確認</h2>' +
      "<p>共 " + cartTotalCount() + " 件商品，總金額 " + formatPrice(cartTotalAmount()) + "。</p>" +
      "<p>配送方式：" + (state.cartDelivery === "standard" ? "宅配到府" : "超商取貨") + "</p>" +
      "<p>付款方式：" + { card: "信用卡", transfer: "銀行轉帳", cod: "貨到付款" }[state.cartPayment] + "</p>" +
      '<p class="inline-note">這是 UI 操作展示，送出後不會產生真實訂單。</p>' +
      "</div>" +
      '<div class="wizard-actions">' +
      '<button type="button" class="btn btn-secondary" data-action="cart-prev">上一步</button>' +
      '<button type="button" class="btn btn-primary" data-action="cart-confirm">確認送出（示意）</button>' +
      "</div>"
    );
  }

  function viewMember() {
    var tab = state.memberTab;
    return (
      "<h1>會員中心</h1>" +
      '<div class="card" style="margin-bottom:24px;display:flex;gap:16px;align-items:center">' +
      '<img src="' + MASCOT + '" alt="" style="width:56px;height:56px;border-radius:50%;object-fit:cover;object-position:top center" />' +
      "<div><strong>會員：懂選的顧客</strong><p class=\"inline-note\">會員等級：金卡會員（示意資料）</p></div>" +
      "</div>" +
      '<div class="chip-row" role="tablist" aria-label="會員中心分頁">' +
      '<button type="button" class="chip" role="tab" aria-selected="' + (tab === "orders") + '" data-action="member-tab" data-tab="orders">訂單紀錄</button>' +
      '<button type="button" class="chip" role="tab" aria-selected="' + (tab === "favorites") + '" data-action="member-tab" data-tab="favorites">收藏商品</button>' +
      '<button type="button" class="chip" role="tab" aria-selected="' + (tab === "service") + '" data-action="member-tab" data-tab="service">客服捷徑</button>' +
      "</div>" +
      (tab === "orders" ? memberOrdersPanel() : tab === "favorites" ? memberFavoritesPanel() : memberServicePanel())
    );
  }

  function memberOrdersPanel() {
    return (
      '<div class="card">' +
      MOCK_ORDERS.map(function (o) {
        return (
          '<div class="cart-line">' +
          "<div style=\"flex:1\">" +
          "<strong>" + esc(o.item) + "</strong>" +
          '<p class="inline-note">訂單編號 ' + esc(o.id) + "．" + esc(o.date) + "</p>" +
          "</div>" +
          '<span class="status-pill status-closed">' + esc(o.status) + "</span>" +
          '<span style="font-weight:700">' + formatPrice(o.amount) + "</span>" +
          "</div>"
        );
      }).join("") +
      "</div>"
    );
  }

  function memberFavoritesPanel() {
    var favorites = PRODUCTS.slice(0, 3);
    return '<div class="product-grid">' + favorites.map(function (p) { return productCard(p); }).join("") + "</div>";
  }

  function memberServicePanel() {
    return (
      '<div class="support-grid">' +
      '<div class="card support-card"><h3>客服紀錄</h3><p class="inline-note">查看已建立的案件與客服回覆。</p><button type="button" class="btn btn-secondary" data-nav="tickets">前往查看</button></div>' +
      '<div class="card support-card"><h3>退貨退款</h3><p class="inline-note">申請退貨或追蹤退款進度。</p><button type="button" class="btn btn-secondary" data-nav="return">前往申請</button></div>' +
      '<div class="card support-card"><h3>聯絡客服</h3><p class="inline-note">建立新案件詢問問題。</p><button type="button" class="btn btn-secondary" data-nav="contact">建立案件</button></div>' +
      "</div>"
    );
  }

  function viewSupportHome() {
    return (
      "<h1>客服中心</h1>" +
      '<p class="view-lede">有任何購物或商品問題，都可以在這裡找到協助。</p>' +
      '<div class="donggu-tip">' +
      '<img src="' + MASCOT + '" alt="" />' +
      "<p>嗨，我是懂懂！先看看 FAQ 或許就能找到答案，找不到的話也可以直接建立案件詢問客服喔。</p>" +
      "</div>" +
      '<div class="support-grid">' +
      '<div class="card support-card"><h2>常見問題 FAQ</h2><p class="inline-note">寄送、退換貨、帳號等常見問題快速解答。</p><button type="button" class="btn btn-primary" data-nav="faq">查看 FAQ</button></div>' +
      '<div class="card support-card"><h2>聯絡客服</h2><p class="inline-note">找不到答案時，建立案件讓客服協助你。</p><button type="button" class="btn btn-primary" data-nav="contact">建立案件</button></div>' +
      '<div class="card support-card"><h2>客服紀錄</h2><p class="inline-note">查看已送出案件與客服回覆進度。</p><button type="button" class="btn btn-primary" data-nav="tickets">查看紀錄</button></div>' +
      '<div class="card support-card"><h2>退貨退款申請</h2><p class="inline-note">依步驟完成退貨退款申請流程。</p><button type="button" class="btn btn-primary" data-nav="return">開始申請</button></div>' +
      '<div class="card support-card"><h2>檢舉／申訴</h2><p class="inline-note">回報商品內容或服務相關問題。</p><button type="button" class="btn btn-primary" data-nav="report">前往檢舉／申訴</button></div>' +
      "</div>"
    );
  }

  function viewFaq() {
    return (
      "<h1>常見問題 FAQ</h1>" +
      '<p class="view-lede">可用分類或關鍵字搜尋，點擊問題即可展開詳細解答。</p>' +
      '<div class="faq-toolbar">' +
      '<input type="text" id="faq-search" class="faq-search" placeholder="輸入關鍵字搜尋常見問題" value="' + esc(state.faqQuery) + '" aria-label="搜尋常見問題" />' +
      "</div>" +
      '<div class="chip-row" role="group" aria-label="FAQ 分類">' +
      FAQ_CATEGORIES.map(function (c) {
        return (
          '<button type="button" class="chip" data-action="faq-category" data-category="' + esc(c) + '" aria-pressed="' + (state.faqCategory === c) + '">' +
          esc(c) +
          "</button>"
        );
      }).join("") +
      "</div>" +
      '<div id="faq-list">' +
      FAQ_ITEMS.filter(function (item) { return state.faqCategory === "全部" || item.category === state.faqCategory; })
        .map(function (item) {
          var open = !!state.faqOpenIds[item.id];
          return (
            '<div class="faq-item" data-open="' + open + '" data-question="' + esc(item.q.toLowerCase()) + '">' +
            '<button type="button" class="faq-question" data-action="toggle-faq" data-id="' + item.id + '" aria-expanded="' + open + '" aria-controls="faq-answer-' + item.id + '">' +
            "<span>" + esc(item.q) + "</span>" +
            '<span class="faq-caret" aria-hidden="true">⌄</span>' +
            "</button>" +
            '<div class="faq-answer" id="faq-answer-' + item.id + '"' + (open ? "" : " hidden") + ">" + esc(item.a) + "</div>" +
            "</div>"
          );
        }).join("") +
      "</div>" +
      '<p class="inline-note" id="faq-empty" hidden>找不到符合的常見問題，可以直接聯絡客服詢問。</p>'
    );
  }

  function setupFaqSearch() {
    var input = document.getElementById("faq-search");
    if (!input) return;
    input.addEventListener("input", function () {
      state.faqQuery = input.value;
      applyFaqFilter();
    });
    applyFaqFilter();
  }

  function applyFaqFilter() {
    var query = state.faqQuery.trim().toLowerCase();
    var items = document.querySelectorAll("#faq-list .faq-item");
    var visibleCount = 0;
    items.forEach(function (el) {
      var match = !query || el.getAttribute("data-question").indexOf(query) !== -1;
      el.hidden = !match;
      if (match) visibleCount += 1;
    });
    var empty = document.getElementById("faq-empty");
    if (empty) empty.hidden = visibleCount !== 0;
  }

  function viewContact() {
    return (
      "<h1>聯絡客服／建立案件</h1>" +
      '<p class="view-lede">請填寫問題內容，客服會盡快回覆（此為示意表單，不會實際送出）。</p>' +
      '<form id="contact-form" class="card" novalidate>' +
      '<div class="form-field">' +
      '<label for="contact-subject">主旨</label>' +
      '<input type="text" id="contact-subject" name="subject" placeholder="請簡述您的問題" />' +
      "</div>" +
      '<div class="form-field">' +
      '<label for="contact-category">問題類別</label>' +
      '<select id="contact-category" name="category">' +
      '<option value="order">訂單相關</option>' +
      '<option value="product">商品相關</option>' +
      '<option value="account">帳號相關</option>' +
      '<option value="other">其他</option>' +
      "</select>" +
      "</div>" +
      '<div class="form-field">' +
      '<label for="contact-message">問題內容</label>' +
      '<textarea id="contact-message" name="message" placeholder="請描述詳細狀況"></textarea>' +
      "</div>" +
      '<div class="form-field">' +
      '<label for="contact-pref">偏好聯絡方式</label>' +
      '<select id="contact-pref" name="pref">' +
      '<option value="email">電子郵件</option>' +
      '<option value="phone">電話</option>' +
      "</select>" +
      "</div>" +
      '<div class="form-field">' +
      '<label for="contact-attachment">附件（原型展示，不會實際上傳）</label>' +
      '<input type="file" id="contact-attachment" name="attachment" disabled />' +
      "</div>" +
      '<p class="form-error" id="contact-error" hidden>請填寫主旨後再送出。</p>' +
      '<button type="submit" class="btn btn-primary">送出案件（示意）</button>' +
      "</form>"
    );
  }

  function viewTickets() {
    var filters = ["全部", "已回覆顧客", "未回覆顧客", "已結案"];
    var statusMap = { replied: "已回覆顧客", unreplied: "未回覆顧客", closed: "已結案" };
    var list = TICKETS.filter(function (t) { return state.ticketFilter === "全部" || statusMap[t.status] === state.ticketFilter; });
    var selected = state.selectedTicketId ? findTicket(state.selectedTicketId) : null;

    return (
      "<h1>客服紀錄／案件詳情</h1>" +
      '<p class="view-lede">查看已建立的客服案件與對話紀錄。</p>' +
      '<div class="chip-row" role="group" aria-label="依狀態篩選案件">' +
      filters
        .map(function (f) {
          return '<button type="button" class="chip" data-action="ticket-filter" data-filter="' + esc(f) + '" aria-pressed="' + (state.ticketFilter === f) + '">' + esc(f) + "</button>";
        })
        .join("") +
      "</div>" +
      '<div class="ticket-layout' + (selected ? " has-selection" : "") + '">' +
      '<div class="ticket-list" role="list" aria-label="案件清單">' +
      (list.length
        ? list
            .map(function (t) {
              var isSelected = state.selectedTicketId === t.id;
              return (
                '<button type="button" class="ticket-list-item" role="listitem" data-action="select-ticket" data-id="' + t.id + '" aria-current="' + isSelected + '">' +
                '<span class="status-pill status-' + t.status + '">' + esc(statusMap[t.status]) + "</span>" +
                '<p class="tl-subject">' + esc(t.subject) + "</p>" +
                '<p class="inline-note">' + esc(t.id) + "．最後更新 " + esc(t.updatedAt) + "</p>" +
                "</button>"
              );
            })
            .join("")
        : '<p class="inline-note">此篩選條件下沒有案件。</p>') +
      "</div>" +
      (selected ? '<div class="card ticket-detail-panel">' + ticketDetailMarkup(selected, statusMap) + "</div>" : "") +
      "</div>"
    );
  }

  function ticketDetailMarkup(t, statusMap) {
    return (
      '<div class="ticket-detail-header">' +
      "<div>" +
      '<span class="status-pill status-' + t.status + '">' + esc(statusMap[t.status]) + "</span>" +
      "<h2>" + esc(t.subject) + "</h2>" +
      '<p class="inline-note">案件編號 ' + esc(t.id) + "．最後更新 " + esc(t.updatedAt) + "</p>" +
      "</div>" +
      '<button type="button" class="ticket-close-btn" data-action="close-ticket-detail" aria-label="關閉案件詳情">✕</button>' +
      "</div>" +
      '<div class="message-thread">' +
      t.messages
        .map(function (m) {
          return (
            '<div class="message-bubble from-' + m.from + '">' +
            esc(m.text) +
            '<span class="message-time">' + esc(m.from === "customer" ? "我" : "客服") + "．" + esc(m.time) + "</span>" +
            "</div>"
          );
        })
        .join("") +
      "</div>" +
      '<p class="inline-note">此對話紀錄為示意資料，本原型不提供實際回覆功能。</p>'
    );
  }

  function viewReturn() {
    if (state.returnSubmitted) {
      return (
        "<h1>退貨退款申請</h1>" +
        '<div class="card">' +
        '<div class="empty-state">' +
        '<img src="' + MASCOT + '" alt="" />' +
        "<h2>申請已送出（示意）</h2>" +
        "<p>退貨退款申請已完成示意流程，實際系統會由客服為您安排後續處理。</p>" +
        '<button type="button" class="btn btn-primary" data-action="return-restart">再次示範流程</button>' +
        '<button type="button" class="btn btn-secondary" data-nav="member">回會員中心</button>' +
        "</div>" +
        "</div>"
      );
    }

    var step = state.returnStep;
    var stepsMarkup =
      '<div class="return-steps" role="list" aria-label="退貨申請步驟">' +
      RETURN_STEP_LABELS.map(function (label, i) {
        var n = i + 1;
        var cls = n === step ? "is-active" : n < step ? "is-done" : "";
        return '<div class="stepper-item ' + cls + '" role="listitem">' + n + "．" + label + "</div>";
      }).join("") +
      "</div>";

    var body = "";
    var nextDisabled = false;

    if (step === 1) {
      body =
        '<div class="order-pick-list" role="radiogroup" aria-label="選擇要退貨的訂單">' +
        MOCK_ORDERS.map(function (o) {
          var pressed = state.returnOrderId === o.id;
          return (
            '<button type="button" class="order-pick-item" data-action="select-return-order" data-id="' + o.id + '" aria-pressed="' + pressed + '">' +
            "<span><strong>" + esc(o.item) + "</strong><br /><span class=\"inline-note\">" + esc(o.id) + "．" + esc(o.date) + "</span></span>" +
            "<span>" + formatPrice(o.amount) + "</span>" +
            "</button>"
          );
        }).join("") +
        "</div>";
      nextDisabled = !state.returnOrderId;
    } else if (step === 2) {
      body =
        '<div class="form-field">' +
        '<span id="return-reason-label" style="font-weight:600">退貨原因</span>' +
        '<div class="radio-row" role="radiogroup" aria-labelledby="return-reason-label">' +
        RETURN_REASONS.map(function (r) {
          return '<label class="radio-option"><input type="radio" name="return-reason" data-action="select-return-reason" value="' + esc(r) + '"' + (state.returnReason === r ? " checked" : "") + " /> " + esc(r) + "</label>";
        }).join("") +
        "</div>" +
        "</div>" +
        '<div class="form-field">' +
        '<label for="return-note">補充說明（選填）</label>' +
        '<textarea id="return-note" placeholder="請描述詳細狀況">' + esc(state.returnNote) + "</textarea>" +
        "</div>";
      nextDisabled = !state.returnReason;
    } else if (step === 3) {
      body =
        '<div class="form-field">' +
        '<span id="return-method-label" style="font-weight:600">取件方式</span>' +
        '<div class="radio-row" role="radiogroup" aria-labelledby="return-method-label">' +
        RETURN_METHODS.map(function (m) {
          return '<label class="radio-option"><input type="radio" name="return-method" data-action="select-return-method" value="' + m.id + '"' + (state.returnMethod === m.id ? " checked" : "") + " /> " + esc(m.label) + "</label>";
        }).join("") +
        "</div>" +
        "</div>" +
        '<div class="form-field">' +
        '<label for="return-evidence">上傳憑證照片（原型展示，不會實際上傳）</label>' +
        '<input type="file" id="return-evidence" disabled />' +
        "</div>";
      nextDisabled = !state.returnMethod;
    } else {
      var order = MOCK_ORDERS.filter(function (o) { return o.id === state.returnOrderId; })[0];
      var method = RETURN_METHODS.filter(function (m) { return m.id === state.returnMethod; })[0];
      body =
        '<div class="card" style="background:var(--color-surface)">' +
        "<h2>申請內容確認</h2>" +
        "<p><strong>訂單：</strong>" + esc(order ? order.item + "（" + order.id + "）" : "—") + "</p>" +
        "<p><strong>原因：</strong>" + esc(state.returnReason || "—") + "</p>" +
        "<p><strong>補充說明：</strong>" + esc(state.returnNote || "（無）") + "</p>" +
        "<p><strong>取件方式：</strong>" + esc(method ? method.label : "—") + "</p>" +
        "</div>";
    }

    return (
      "<h1>退貨退款申請</h1>" +
      '<p class="view-lede">依步驟完成退貨申請，僅供操作示意，不會實際送出。</p>' +
      stepsMarkup +
      '<div class="card">' + body + "</div>" +
      '<div class="wizard-actions">' +
      '<button type="button" class="btn btn-secondary" data-action="return-prev"' + (step === 1 ? " disabled" : "") + ">上一步</button>" +
      (step < 4
        ? '<button type="button" class="btn btn-primary" data-action="return-next"' + (nextDisabled ? " disabled" : "") + ">下一步</button>"
        : '<button type="button" class="btn btn-primary" data-action="return-submit">送出申請（示意）</button>') +
      "</div>"
    );
  }

  function viewReport() {
    return (
      "<h1>檢舉／申訴入口</h1>" +
      '<p class="view-lede">由於 DoSelect 懂選為單一賣場平台，檢舉／申訴僅適用於商品內容、服務體驗或帳號行為，會統一交由客服團隊處理。</p>' +
      '<form id="report-form" class="card" novalidate>' +
      '<div class="form-field">' +
      '<label for="report-type">檢舉／申訴類型</label>' +
      '<select id="report-type" name="type">' +
      REPORT_TYPES.map(function (t) { return '<option value="' + esc(t) + '">' + esc(t) + "</option>"; }).join("") +
      "</select>" +
      "</div>" +
      '<div class="form-field">' +
      '<label for="report-ref">相關訂單或商品連結</label>' +
      '<input type="text" id="report-ref" name="ref" placeholder="例如：訂單編號或商品名稱" />' +
      "</div>" +
      '<div class="form-field">' +
      '<label for="report-desc">問題說明</label>' +
      '<textarea id="report-desc" name="desc" placeholder="請描述遇到的狀況"></textarea>' +
      "</div>" +
      '<div class="form-field">' +
      '<label for="report-contact">聯絡方式</label>' +
      '<input type="text" id="report-contact" name="contact" placeholder="電子郵件或電話" />' +
      "</div>" +
      '<button type="submit" class="btn btn-primary">送出檢舉／申訴（示意）</button>' +
      "</form>"
    );
  }

  /* ---------------------------------------------------------------------
   * Action handling
   * ------------------------------------------------------------------- */

  function handleAction(el) {
    var action = el.getAttribute("data-action");
    var id = el.getAttribute("data-id");

    switch (action) {
      case "go-back":
        goBack();
        return;

      case "select-purpose":
        state.selectedPurpose = id;
        renderRoute();
        return;

      case "select-budget":
        state.selectedBudget = id;
        var b = findBudget(id);
        if (b) state.budgetRangeValue = b.max === Infinity ? b.min : Math.round((b.min + b.max) / 2);
        renderRoute();
        return;

      case "filter-category":
        state.productFilters.category = el.getAttribute("data-category");
        renderRoute();
        return;

      case "filter-category-nav":
        state.productFilters.category = el.getAttribute("data-category");
        goTo("products");
        return;

      case "sort-products":
        state.productFilters.sort = el.getAttribute("data-sort");
        renderRoute();
        return;

      case "add-to-cart":
        addToCart(id);
        showToast("已加入購物車（示意）");
        renderRoute();
        return;

      case "toggle-compare":
        var result = toggleCompare(id);
        if (result === "full") {
          showToast("最多可比較 3 項商品");
        } else if (result === "added") {
          showToast("已加入比較");
        } else {
          showToast("已從比較移除");
        }
        renderRoute();
        return;

      case "cart-qty":
        var delta = parseInt(el.getAttribute("data-delta"), 10);
        var item = state.cartItems.filter(function (i) { return i.productId === id; })[0];
        if (item) {
          item.qty += delta;
          if (item.qty <= 0) {
            state.cartItems = state.cartItems.filter(function (i) { return i.productId !== id; });
          }
        }
        renderRoute();
        return;

      case "cart-remove":
        state.cartItems = state.cartItems.filter(function (i) { return i.productId !== id; });
        renderRoute();
        return;

      case "cart-next":
        state.cartStep = Math.min(3, state.cartStep + 1);
        renderRoute();
        return;

      case "cart-prev":
        state.cartStep = Math.max(1, state.cartStep - 1);
        renderRoute();
        return;

      case "cart-confirm":
        showToast("訂單已確認送出（示意，未產生真實訂單）");
        state.cartItems = [];
        state.cartStep = 1;
        renderRoute();
        return;

      case "select-delivery":
        state.cartDelivery = el.value;
        renderRoute();
        return;

      case "select-payment":
        state.cartPayment = el.value;
        renderRoute();
        return;

      case "member-tab":
        state.memberTab = el.getAttribute("data-tab");
        renderRoute();
        return;

      case "toggle-faq":
        state.faqOpenIds[id] = !state.faqOpenIds[id];
        renderRoute();
        return;

      case "faq-category":
        state.faqCategory = el.getAttribute("data-category");
        renderRoute();
        return;

      case "select-ticket":
        state.selectedTicketId = id;
        renderRoute();
        return;

      case "close-ticket-detail":
        state.selectedTicketId = null;
        renderRoute();
        return;

      case "ticket-filter":
        state.ticketFilter = el.getAttribute("data-filter");
        renderRoute();
        return;

      case "select-return-order":
        state.returnOrderId = id;
        renderRoute();
        return;

      case "select-return-reason":
        state.returnReason = el.value;
        renderRoute();
        return;

      case "select-return-method":
        state.returnMethod = el.value;
        renderRoute();
        return;

      case "return-next":
        state.returnStep = Math.min(4, state.returnStep + 1);
        renderRoute();
        return;

      case "return-prev":
        state.returnStep = Math.max(1, state.returnStep - 1);
        renderRoute();
        return;

      case "return-submit":
        state.returnSubmitted = true;
        showToast("退貨退款申請已送出（示意）");
        renderRoute();
        return;

      case "return-restart":
        state.returnStep = 1;
        state.returnOrderId = null;
        state.returnReason = null;
        state.returnNote = "";
        state.returnMethod = null;
        state.returnSubmitted = false;
        renderRoute();
        return;

      default:
        return;
    }
  }

  /* ---------------------------------------------------------------------
   * Global listeners
   * ------------------------------------------------------------------- */

  function setupGlobalListeners() {
    document.addEventListener("click", function (e) {
      var menuToggle = e.target.closest("#menu-toggle");
      if (menuToggle) {
        var menu = document.getElementById("proto-menu");
        var expanded = menuToggle.getAttribute("aria-expanded") === "true";
        menuToggle.setAttribute("aria-expanded", String(!expanded));
        if (menu) menu.hidden = expanded;
        return;
      }

      var navEl = e.target.closest("[data-nav]");
      if (navEl) {
        e.preventDefault();
        var view = navEl.getAttribute("data-nav");
        var pid = navEl.getAttribute("data-id");
        goTo(view, pid ? { id: pid } : {});
        var protoMenu = document.getElementById("proto-menu");
        var toggleBtn = document.getElementById("menu-toggle");
        if (protoMenu && !protoMenu.hidden) {
          protoMenu.hidden = true;
          if (toggleBtn) toggleBtn.setAttribute("aria-expanded", "false");
        }
        return;
      }

      var actionEl = e.target.closest("[data-action]");
      if (actionEl && !actionEl.disabled) {
        e.preventDefault();
        handleAction(actionEl);
      }
    });

    document.addEventListener("change", function (e) {
      if (e.target && e.target.id === "budget-range") {
        state.budgetRangeValue = parseInt(e.target.value, 10);
        state.selectedBudget = nearestBudgetTier(state.budgetRangeValue);
        renderRoute();
      }
    });

    document.addEventListener("input", function (e) {
      if (e.target && e.target.id === "budget-range") {
        var out = document.getElementById("budget-range-output");
        if (out) out.textContent = parseInt(e.target.value, 10).toLocaleString("zh-Hant-TW");
      }
      if (e.target && e.target.id === "return-note") {
        state.returnNote = e.target.value;
      }
    });

    document.addEventListener("submit", function (e) {
      if (e.target.id === "contact-form") {
        e.preventDefault();
        var subject = e.target.querySelector("#contact-subject").value.trim();
        var errorEl = document.getElementById("contact-error");
        if (!subject) {
          if (errorEl) errorEl.hidden = false;
          return;
        }
        if (errorEl) errorEl.hidden = true;
        var message = e.target.querySelector("#contact-message").value.trim();
        var newId = "T" + String(TICKETS.length + 1).padStart(3, "0");
        TICKETS.unshift({
          id: newId,
          subject: subject,
          status: "unreplied",
          updatedAt: "剛剛送出",
          preview: message.slice(0, 40),
          messages: [{ from: "customer", text: message || "（無補充內容）", time: "剛剛" }]
        });
        showToast("案件已建立（示意），可於客服紀錄查看");
        state.selectedTicketId = newId;
        state.ticketFilter = "全部";
        goTo("tickets", { id: newId });
      } else if (e.target.id === "report-form") {
        e.preventDefault();
        e.target.reset();
        showToast("已收到您的檢舉／申訴（示意，不會實際送出）");
      }
    });

    window.addEventListener("hashchange", renderRoute);
  }

  /* ---------------------------------------------------------------------
   * Init
   * ------------------------------------------------------------------- */

  function init() {
    setupGlobalListeners();
    if (!window.location.hash) {
      window.location.hash = "#/home";
    }
    renderRoute();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
