/* =============================================================================
   DoSelect 懂選 — 後台視覺預覽畫面
   資訊密度較高：以表格、篩選、狀態與處理資訊為主，吉祥物僅在空狀態少量出現。
   ============================================================================= */
(function (global) {
  'use strict';

  var D = global.PreviewData;
  var S = global.StoreViews;
  var esc = S.esc;
  var badge = S.badge;
  var icon = S.icon;
  var money = D.money;

  function pageHead(opts) { return S.pageHead(opts); }

  function tableScroll(head, rows) {
    return '<div class="table-scroll"><table class="data"><thead><tr>' + head +
      '</tr></thead><tbody>' + rows + '</tbody></table></div>';
  }

  function pager(total, shown) {
    return '<div class="pager">' +
      '<span class="pager__status">共 ' + total + ' 筆，顯示第 1–' + shown + ' 筆</span>' +
      '<div class="pager__pages">' +
        '<button type="button" aria-current="page">1</button>' +
        '<button type="button" data-toast="示意：切換到第 2 頁">2</button>' +
        '<button type="button" data-toast="示意：切換到第 3 頁">3</button>' +
      '</div></div>';
  }

  /* ---------- 1. 儀表板 ---------- */

  function dashboard() {
    var openCases = D.adminCases.filter(function (c) { return !c.closed; }).length;
    var unclaimed = D.adminCases.filter(function (c) { return !c.claimed; }).length;
    var overdue = D.adminCases.filter(function (c) { return c.overdue; }).length;
    var openReturns = D.returns.filter(function (r) { return r.status !== 'complete'; }).length;

    return '' +
      pageHead({
        crumbs: ['後台'],
        title: '後台首頁',
        lede: '今日待辦與售後處理概況。所有數字為假資料。',
        actions: '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：匯出今日摘要">匯出摘要</button>'
      }) +
      '<div class="grid grid--4">' +
        '<div class="stat stat--alert"><span class="stat__label">未承接案件</span><span class="stat__value">' + unclaimed + '</span><span class="stat__note">需盡快指派</span></div>' +
        '<div class="stat"><span class="stat__label">處理中案件</span><span class="stat__value">' + openCases + '</span><span class="stat__note">含待客戶回覆</span></div>' +
        '<div class="stat stat--alert"><span class="stat__label">SLA 逾時</span><span class="stat__value">' + overdue + '</span><span class="stat__note">需主管關注</span></div>' +
        '<div class="stat"><span class="stat__label">退貨處理中</span><span class="stat__value">' + openReturns + '</span><span class="stat__note">含待收貨與檢查</span></div>' +
      '</div>' +

      '<div class="grid" style="grid-template-columns:minmax(0,3fr) minmax(280px,2fr)">' +
        '<section class="card card--flush">' +
          '<div class="card__head"><h3>今日待處理案件</h3>' +
            '<button type="button" class="btn btn--ghost btn--sm" data-go="admin/cases">前往客服案件</button></div>' +
          tableScroll(
            '<th>案件編號</th><th>分類</th><th>承接人員</th><th>狀態</th><th>期限</th>',
            D.adminCases.filter(function (c) { return !c.closed; }).map(function (c) {
              return '<tr><td class="mono">' + esc(c.id) + '</td><td>' + esc(c.title) + '</td>' +
                '<td>' + esc(c.assignee) + '</td>' +
                '<td>' + (c.claimed ? (c.replied ? badge('in-progress', '已回覆') : badge('waiting', '未回覆')) : badge('failed', '未承接')) + '</td>' +
                '<td>' + (c.overdue ? badge('failed', '已逾時') : esc(c.sla || '—')) + '</td></tr>';
            }).join('')
          ) +
        '</section>' +
        '<section class="card">' +
          '<h3 style="font-size:var(--fs-h3);margin-bottom:var(--space-3)">售後案件量（近 7 日）</h3>' +
          '<ul class="bars">' +
            [[8, '一'], [12, '二'], [6, '三'], [15, '四'], [11, '五'], [4, '六'], [3, '日']].map(function (d) {
              return '<li><span class="bar" style="height:' + Math.round(d[0] / 15 * 100) + '%"></span>' +
                '<span class="bar__label">' + d[1] + '</span></li>';
            }).join('') +
          '</ul>' +
          '<ul class="legend" style="margin-top:var(--space-3)"><li><span class="swatch"></span>新增案件數</li></ul>' +
        '</section>' +
      '</div>' +

      '<section class="section"><div class="section__head"><h2>快速前往</h2></div>' +
        '<div class="entry-grid">' +
          [['admin/cases', 'support', '客服案件', '承接、回覆、指派與 SLA'],
           ['admin/returns', 'box', '退貨退款審核', '收貨、檢查、核准與退款'],
           ['admin/reports', 'tag', '檢舉審核', '一般檢舉與主管覆核'],
           ['admin/analytics', 'budget', '營運報表', '客服售後範圍統計']].map(function (e) {
            return '<button type="button" class="entry" data-go="' + e[0] + '">' +
              '<span class="entry__icon">' + icon(e[1]) + '</span><h3>' + esc(e[2]) + '</h3><p>' + esc(e[3]) + '</p></button>';
          }).join('') +
        '</div></section>';
  }

  /* ---------- 2. 會員管理 ---------- */

  function members() {
    return '' +
      pageHead({ crumbs: ['後台', '會員'], title: '會員管理', lede: '查詢會員、訂單數與帳號狀態。完整個資需另行授權查看並留下稽核紀錄。' }) +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 240px"><span class="field__label">關鍵字</span><input class="input" type="search" placeholder="會員編號 / 遮蔽信箱" /></label>' +
        '<label class="field"><span class="field__label">帳號狀態</span><select class="select"><option>全部</option><option>正常</option><option>待驗證</option><option>已停用</option></select></label>' +
        '<label class="field"><span class="field__label">加入時間</span><select class="select"><option>不限</option><option>近 30 天</option><option>近 90 天</option></select></label>' +
        '<button type="button" class="btn btn--ghost" data-toast="示意：清除全部條件">清除全部條件</button>' +
      '</div>' +
      tableScroll(
        '<th>會員編號</th><th>姓名</th><th>電子郵件</th><th>加入日</th><th class="num">訂單數</th><th class="num">累計消費</th><th>狀態</th><th></th>',
        D.members.map(function (m) {
          return '<tr><td class="mono">' + esc(m.id) + '</td><td>' + esc(m.name) + '</td>' +
            '<td class="muted">' + esc(m.mail) + '</td><td>' + esc(m.joined) + '</td>' +
            '<td class="num">' + m.orders + '</td><td class="num">' + money(m.spent) + '</td>' +
            '<td>' + badge(m.status, m.statusText) + '</td>' +
            '<td class="num"><button type="button" class="btn btn--ghost btn--sm" data-toast="示意：開啟會員詳細">詳細</button></td></tr>';
        }).join('')
      ) +
      pager(128, D.members.length) +
      '<p class="token-todo">個資顯示規則：列表一律使用遮蔽值；需要完整資料時另按用途申請，並寫入 AuditLog。本預覽僅示意版面。</p>';
  }

  /* ---------- 3. 商品管理 ---------- */

  function productsAdmin() {
    return '' +
      pageHead({
        crumbs: ['後台', '商品'], title: '商品管理',
        lede: '商品上下架、價格與庫存維護。批次操作會先顯示選取筆數與上限。',
        actions: '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：匯入商品">匯入</button>' +
                 '<button type="button" class="btn btn--primary btn--sm" data-toast="示意：建立商品">新增商品</button>'
      }) +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 220px"><span class="field__label">關鍵字</span><input class="input" type="search" placeholder="商品名稱 / 編號" /></label>' +
        '<label class="field"><span class="field__label">分類</span><select class="select"><option>全部</option>' +
          D.categories.map(function (c) { return '<option>' + esc(c.key) + '</option>'; }).join('') + '</select></label>' +
        '<label class="field"><span class="field__label">狀態</span><select class="select"><option>全部</option><option>已上架</option><option>草稿</option><option>已下架</option></select></label>' +
        '<div class="field"><span class="field__label">庫存</span><div class="chip-row">' +
          '<button type="button" class="chip" aria-pressed="false">低庫存</button>' +
          '<button type="button" class="chip" aria-pressed="false">已售完</button></div></div>' +
      '</div>' +
      '<div class="btn-row" style="align-items:center">' +
        '<span class="tiny muted">已選 2 筆 / 單次上限 100 筆</span>' +
        '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：批次上架">批次上架</button>' +
        '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：批次調價">批次調價</button>' +
        '<button type="button" class="btn btn--danger btn--sm" data-toast="示意：需二次確認的批次下架">批次下架</button>' +
      '</div>' +
      tableScroll(
        '<th style="width:36px"><input type="checkbox" aria-label="全選" /></th><th>商品編號</th><th>名稱</th><th>分類</th><th class="num">售價</th><th class="num">庫存</th><th>狀態</th><th>更新日</th><th></th>',
        D.adminProducts.map(function (p, i) {
          return '<tr><td><input type="checkbox"' + (i < 2 ? ' checked' : '') + ' aria-label="選取 ' + esc(p.name) + '" /></td>' +
            '<td class="mono">' + esc(p.id) + '</td><td><strong>' + esc(p.name) + '</strong></td>' +
            '<td>' + esc(p.category) + '</td><td class="num">' + money(p.price) + '</td>' +
            '<td class="num">' + (p.stock === 0 ? '<span class="badge badge--failed">0</span>' : p.stock) + '</td>' +
            '<td>' + badge(p.status, p.statusText) + '</td><td>' + esc(p.updated) + '</td>' +
            '<td class="num"><button type="button" class="btn btn--ghost btn--sm" data-toast="示意：開啟商品編輯">編輯</button></td></tr>';
        }).join('')
      ) +
      pager(86, D.adminProducts.length);
  }

  /* ---------- 4. 訂單管理 ---------- */

  function ordersAdmin() {
    return '' +
      pageHead({
        crumbs: ['後台', '訂單'], title: '訂單管理',
        lede: '訂單狀態、付款與出貨處理。金額與狀態欄不省略。',
        actions: '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：建立批次出貨">批次出貨</button>'
      }) +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 220px"><span class="field__label">訂單編號</span><input class="input" type="search" placeholder="SO-..." /></label>' +
        '<label class="field"><span class="field__label">訂單狀態</span><select class="select"><option>全部</option><option>待付款</option><option>待出貨</option><option>已出貨</option><option>已完成</option><option>已取消</option></select></label>' +
        '<label class="field"><span class="field__label">配送方式</span><select class="select"><option>全部</option><option>宅配</option><option>超商</option></select></label>' +
        '<label class="field"><span class="field__label">日期區間</span><input class="input" type="date" /></label>' +
      '</div>' +
      tableScroll(
        '<th style="width:36px"><input type="checkbox" aria-label="全選" /></th><th>訂單編號</th><th>會員</th><th>日期</th><th class="num">金額</th><th>付款</th><th>配送</th><th>狀態</th><th></th>',
        D.adminOrders.map(function (o) {
          return '<tr><td><input type="checkbox" aria-label="選取 ' + esc(o.id) + '" /></td>' +
            '<td class="mono">' + esc(o.id) + '</td><td>' + esc(o.member) + '</td><td>' + esc(o.date) + '</td>' +
            '<td class="num">' + money(o.total) + '</td><td>' + esc(o.pay) + '</td><td>' + esc(o.ship) + '</td>' +
            '<td>' + badge(o.status, o.statusText) + '</td>' +
            '<td class="num"><button type="button" class="btn btn--ghost btn--sm" data-toast="示意：開啟訂單詳細">詳細</button></td></tr>';
        }).join('')
      ) +
      pager(214, D.adminOrders.length);
  }

  /* ---------- 5. 優惠活動管理 ---------- */

  function promotionsAdmin() {
    return '' +
      pageHead({
        crumbs: ['後台', '優惠活動'], title: '優惠活動管理',
        lede: '活動期間、規則與使用量。停用活動屬高風險操作，需二次確認。',
        actions: '<button type="button" class="btn btn--primary btn--sm" data-toast="示意：建立活動">新增活動</button>'
      }) +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 220px"><span class="field__label">活動名稱</span><input class="input" type="search" placeholder="輸入關鍵字" /></label>' +
        '<label class="field"><span class="field__label">狀態</span><select class="select"><option>全部</option><option>進行中</option><option>待開始</option><option>已結束</option></select></label>' +
        '<label class="field"><span class="field__label">類型</span><select class="select"><option>全部</option><option>滿額折抵</option><option>折扣券</option><option>組合優惠</option></select></label>' +
      '</div>' +
      tableScroll(
        '<th>活動編號</th><th>名稱</th><th>類型</th><th>期間</th><th>規則</th><th class="num">使用量</th><th>狀態</th><th></th>',
        D.promotions.map(function (p) {
          return '<tr><td class="mono">' + esc(p.id) + '</td><td><strong>' + esc(p.name) + '</strong></td>' +
            '<td>' + esc(p.kind) + '</td><td class="tiny">' + esc(p.period) + '</td>' +
            '<td class="tiny muted">' + esc(p.rule) + '</td>' +
            '<td class="num">' + p.used + ' / ' + p.quota + '</td>' +
            '<td>' + badge(p.status, p.statusText) + '</td>' +
            '<td class="num"><button type="button" class="btn btn--ghost btn--sm" data-toast="示意：編輯活動">編輯</button>' +
            (p.status === 'in-progress' ? '<button type="button" class="btn btn--danger btn--sm" data-toast="示意：停用需二次確認並填寫理由">停用</button>' : '') +
            '</td></tr>';
        }).join('')
      );
  }

  /* ---------- 6. 客服案件（分割面板：列表 2/5 ＋ 詳細 3/5，無遮罩） ---------- */

  function cases(state) {
    var current = D.adminCases.filter(function (c) { return c.id === state.caseId; })[0];
    var me = '林佩儀';

    function statusChips(c) {
      var out = [];
      out.push(c.claimed ? badge('complete', '已承接') : badge('failed', '未承接'));
      if (c.closed) { out.push(badge('stopped', '已結案')); }
      else if (c.replied) { out.push(badge('in-progress', '已回覆顧客')); }
      else { out.push(badge('waiting', '未回覆顧客')); }
      if (c.overdue) { out.push(badge('failed', 'SLA 逾時')); }
      return out.join(' ');
    }

    var list = '<div class="case-list">' + D.adminCases.map(function (c) {
      return '<button type="button" class="case-item" aria-current="' + (current && current.id === c.id) + '" data-case="' + esc(c.id) + '">' +
        '<span class="case-item__top"><span class="case-item__no">' + esc(c.id) + '</span>' +
          (c.claimed ? badge('complete', '已承接') : badge('failed', '未承接')) + '</span>' +
        '<span class="case-item__title">' + esc(c.subject) + '</span>' +
        '<span class="case-item__meta">' +
          '<span>' + esc(c.title) + '</span>' +
          '<span>承接：' + esc(c.assignee) + '</span>' +
          '<span>' + (c.closed ? '已結案' : (c.replied ? '已回覆' : '未回覆')) + '</span>' +
          (c.overdue ? '<span>逾時</span>' : '') +
        '</span></button>';
    }).join('') + '</div>';

    var detail = '';
    if (current) {
      // 只有承接者可回覆：目前登入身分示意為「林佩儀」。
      var isMine = state.caseClaimedByMe !== null ? state.caseClaimedByMe : (current.assignee === me);
      var claimed = state.caseClaimedByMe !== null ? state.caseClaimedByMe : current.claimed;
      var canReply = claimed && isMine && !current.closed;

      detail = '<div class="split__detail">' +
        '<div class="split__detail-head">' +
          '<div><h3>' + esc(current.subject) + '</h3>' +
            '<p class="muted tiny">' + esc(current.id) + '・' + esc(current.title) + '・優先級 ' + esc(current.priority) + '</p></div>' +
          '<button type="button" class="split__close" data-case-close aria-label="關閉案件詳細">×</button>' +
        '</div>' +
        '<div class="split__detail-body">' +
          '<div class="btn-row">' + statusChips(current) + '</div>' +
          '<div class="card" style="padding:var(--space-4)">' +
            '<div class="grid grid--2">' +
              '<dl class="detail-list">' +
                '<dt>承接人員</dt><dd><strong>' + esc(claimed ? (isMine ? me : current.assignee) : '（未指派）') + '</strong>' + (isMine && claimed ? '（我）' : '') + '</dd>' +
                '<dt>建立時間</dt><dd>' + esc(current.created) + '</dd>' +
                '<dt>最後活動</dt><dd>' + esc(current.last) + '</dd>' +
                '<dt>SLA 期限</dt><dd>' + (current.sla ? esc(current.sla) + (current.overdue ? '（已逾時）' : '') : '—') + '</dd>' +
              '</dl>' +
              '<div class="field">' +
                '<span class="field__label">主管指派</span>' +
                '<select class="select" data-toast="示意：指派需主管權限，並寫入指派歷程">' +
                  D.agents.map(function (a) {
                    return '<option' + (a === current.assignee ? ' selected' : '') + '>' + esc(a) + '</option>';
                  }).join('') +
                '</select>' +
                '<span class="field__hint">僅主管（CustomerServiceSupervisor）可指派／轉派。</span>' +
              '</div>' +
            '</div>' +
            '<div class="btn-row" style="margin-top:var(--space-4)">' +
              (current.closed
                ? '<button type="button" class="btn btn--secondary btn--sm" disabled>已結案，不可重開</button>'
                : (claimed
                    ? (isMine
                        ? '<button type="button" class="btn btn--danger btn--sm" data-case-claim="false">取消承接</button>'
                        : '<button type="button" class="btn btn--secondary btn--sm" disabled>已由 ' + esc(current.assignee) + ' 承接</button>')
                    : '<button type="button" class="btn btn--primary btn--sm" data-case-claim="true">我要承接</button>')) +
              '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：調整優先級">調整優先級</button>' +
              (current.closed ? '' : '<button type="button" class="btn btn--ghost btn--sm" data-toast="示意：結案需填寫處理結果">結案</button>') +
            '</div>' +
          '</div>' +
          '<div><h4 style="font-size:var(--fs-h3);margin-bottom:var(--space-2)">往來訊息</h4>' +
            '<div class="thread">' + current.thread.map(function (m) {
              var cls = m.who === 'internal' ? ' msg--internal' : (m.who === 'agent' ? ' msg--agent' : '');
              return '<div class="msg' + cls + '"><span class="msg__meta">' + esc(m.name) + '・' + esc(m.time) + '</span>' +
                '<span>' + esc(m.text) + '</span></div>';
            }).join('') + '</div></div>' +
          (canReply
            ? '<div class="reply-box">' +
                '<label class="field"><span class="field__label">公開回覆顧客</span>' +
                  '<textarea class="textarea" placeholder="這則內容顧客看得到"></textarea></label>' +
                '<label class="field"><span class="field__label">內部備註（顧客看不到）</span>' +
                  '<textarea class="textarea" style="min-height:64px" placeholder="僅客服團隊可見"></textarea></label>' +
                '<div class="btn-row">' +
                  '<button type="button" class="btn btn--primary btn--sm" data-toast="示意：已送出公開回覆">送出回覆</button>' +
                  '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：已新增內部備註">只存內部備註</button>' +
                '</div></div>'
            : '<div class="reply-box" data-locked="true">' +
                '<div class="locked-note"><span>' +
                  (current.closed
                    ? '案件已結案，不可再回覆。後續問題請建立關聯的新案件。'
                    : (claimed
                        ? '此案件目前由「' + esc(current.assignee) + '」承接，只有承接者可以回覆。需要接手請由主管轉派。'
                        : '尚未承接。請先按「我要承接」才能回覆顧客。')) +
                  '</span></div>' +
                '<label class="field"><span class="field__label">公開回覆顧客</span>' +
                  '<textarea class="textarea" disabled placeholder="需由承接者才能輸入"></textarea></label>' +
                '<div class="btn-row"><button type="button" class="btn btn--primary btn--sm" disabled>送出回覆</button></div>' +
              '</div>') +
        '</div></div>';
    }

    return '' +
      pageHead({
        crumbs: ['後台', '客服'], title: '客服案件',
        lede: '列表與詳細同層並排；開啟詳細時列表約 2/5、詳細約 3/5，詳細只能由右上角關閉按鈕收起，不會遮住列表。'
      }) +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 200px"><span class="field__label">關鍵字</span><input class="input" type="search" placeholder="案件編號 / 主旨" /></label>' +
        '<label class="field"><span class="field__label">分類</span><select class="select"><option>全部</option><option>訂單問題</option><option>商品瑕疵</option><option>退貨諮詢</option><option>保固諮詢</option></select></label>' +
        '<label class="field"><span class="field__label">承接人員</span><select class="select"><option>全部</option>' +
          D.agents.map(function (a) { return '<option>' + esc(a) + '</option>'; }).join('') + '</select></label>' +
        '<div class="field"><span class="field__label">處理狀態</span><div class="chip-row">' +
          '<button type="button" class="chip" aria-pressed="true">未承接</button>' +
          '<button type="button" class="chip" aria-pressed="false">未回覆顧客</button>' +
          '<button type="button" class="chip" aria-pressed="false">已回覆顧客</button>' +
          '<button type="button" class="chip" aria-pressed="false">已結案</button>' +
          '<button type="button" class="chip" aria-pressed="false">僅逾時</button>' +
        '</div></div>' +
      '</div>' +
      '<div class="split" data-detail-open="' + Boolean(current) + '">' +
        '<div class="split__list">' + list + '</div>' +
        detail +
      '</div>' +
      (current ? '' : '<p class="tiny muted">點左側任一案件即可在右側開啟詳細；詳細開啟後列表仍完整可見。</p>');
  }

  /* ---------- 7. 檢舉審核 ---------- */

  function reportsAdmin() {
    return '' +
      pageHead({
        crumbs: ['後台', '檢舉'], title: '檢舉審核',
        lede: '一般檢舉由客服處理；個資、安全、詐欺、法律等高風險類型需主管覆核。'
      }) +
      '<div class="grid grid--4">' +
        '<div class="stat stat--alert"><span class="stat__label">待審核</span><span class="stat__value">1</span></div>' +
        '<div class="stat"><span class="stat__label">審核中</span><span class="stat__value">1</span></div>' +
        '<div class="stat"><span class="stat__label">待主管覆核</span><span class="stat__value">1</span></div>' +
        '<div class="stat stat--good"><span class="stat__label">本週已處理</span><span class="stat__value">7</span></div>' +
      '</div>' +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 200px"><span class="field__label">關鍵字</span><input class="input" type="search" placeholder="檢舉編號 / 對象" /></label>' +
        '<label class="field"><span class="field__label">檢舉原因</span><select class="select"><option>全部</option><option>不實內容</option><option>廣告灌水</option><option>服務態度</option><option>商品描述不符</option></select></label>' +
        '<label class="field"><span class="field__label">風險等級</span><select class="select"><option>全部</option><option>一般</option><option>需主管覆核</option></select></label>' +
      '</div>' +
      tableScroll(
        '<th>檢舉編號</th><th>檢舉對象</th><th>原因</th><th>檢舉人</th><th>等級</th><th>處理人</th><th>建立時間</th><th>狀態</th><th></th>',
        D.reports.map(function (r) {
          return '<tr><td class="mono">' + esc(r.id) + '</td><td>' + esc(r.target) + '</td>' +
            '<td>' + esc(r.reason) + '</td><td class="muted">' + esc(r.reporter) + '</td>' +
            '<td>' + (r.level === '一般' ? '<span class="tag">一般</span>' : badge('waiting', r.level)) + '</td>' +
            '<td>' + esc(r.assignee) + '</td><td class="tiny">' + esc(r.created) + '</td>' +
            '<td>' + badge(r.status, r.statusText) + '</td>' +
            '<td class="num"><button type="button" class="btn btn--ghost btn--sm" data-toast="示意：開啟檢舉詳細與處理歷程">審核</button></td></tr>';
        }).join('')
      ) +
      '<p class="token-todo">S-03 檢舉功能在正式系統需通過 S 門檻才會啟用；此頁僅為版面預覽，不代表功能已開放。</p>';
  }

  /* ---------- 8. 退貨退款審核 ---------- */

  function returnsAdmin(state) {
    var current = D.returns.filter(function (r) { return r.id === state.returnId; })[0];

    var rows = D.returns.map(function (r) {
      return '<tr' + (current && current.id === r.id ? ' aria-selected="true"' : '') + '>' +
        '<td class="mono">' + esc(r.id) + '</td><td class="mono">' + esc(r.order) + '</td>' +
        '<td>' + esc(r.item) + '</td><td>' + esc(r.reason) + '</td>' +
        '<td class="num">' + money(r.amount) + '</td>' +
        '<td>' + badge(r.status, r.statusText) + '</td>' +
        '<td>' + esc(r.due) + '</td><td>' + esc(r.assignee) + '</td>' +
        '<td class="num"><button type="button" class="btn btn--ghost btn--sm" data-return="' + esc(r.id) + '">處理</button></td></tr>';
    }).join('');

    var detail = current ? '<section class="card card--flush">' +
      '<div class="card__head">' +
        '<div><h3>' + esc(current.id) + '・' + esc(current.item) + '</h3>' +
          '<p class="muted tiny">原訂單 ' + esc(current.order) + '・退貨原因：' + esc(current.reason) + '</p></div>' +
        '<button type="button" class="split__close" data-return-close aria-label="關閉退貨詳細">×</button>' +
      '</div>' +
      '<div class="card__body">' +
      '<ol class="steps">' + D.returnStages.map(function (label, i) {
        var n = i + 1;
        var attrs = n === current.stage ? ' aria-current="step"' : (n < current.stage ? ' data-done="true"' : '');
        return '<li' + attrs + '><span class="steps__no">' + n + '</span>' + esc(label) + '</li>';
      }).join('') + '</ol>' +
      '<div class="grid grid--2" style="margin-top:var(--space-4)">' +
        '<dl class="detail-list">' +
          '<dt>退款金額</dt><dd class="num">' + money(current.amount) + '</dd>' +
          '<dt>寄回期限</dt><dd>' + esc(current.due) + '</dd>' +
          '<dt>處理人</dt><dd>' + esc(current.assignee) + '</dd>' +
          '<dt>顧客附件</dt><dd><span class="tag">開箱照片.jpg</span> <span class="tag">外箱.jpg</span></dd>' +
        '</dl>' +
        '<div><h4 style="font-size:var(--fs-h3);margin-bottom:var(--space-2)">處理歷程</h4>' +
          '<ul class="timeline">' +
            '<li><strong>申請成立</strong><span class="muted">08-26 09:12 · 顧客送出</span></li>' +
            '<li><strong>核准寄回</strong><span class="muted">08-26 11:40 · ' + esc(current.assignee) + '</span></li>' +
            (current.stage >= 3 ? '<li><strong>已收貨</strong><span class="muted">08-27 15:02 · 倉庫</span></li>' : '') +
            (current.stage >= 4 ? '<li><strong>檢查完成：可再售</strong><span class="muted">08-28 10:11</span></li>' : '') +
          '</ul></div>' +
      '</div>' +
      '<div class="card" style="margin-top:var(--space-4);background:var(--color-bg)">' +
        '<h4 style="font-size:var(--fs-h3);margin-bottom:var(--space-3)">審核動作</h4>' +
        '<div class="grid grid--2">' +
          '<label class="field"><span class="field__label">檢查結果</span>' +
            '<select class="select"><option>可再售（回補庫存）</option><option>不可再售</option><option>需維修</option></select></label>' +
          '<label class="field"><span class="field__label">退款金額</span><input class="input" value="' + money(current.amount) + '" /></label>' +
          '<label class="field" style="grid-column:1/-1"><span class="field__label">審核說明<span class="field__req">*</span></span>' +
            '<textarea class="textarea" style="min-height:72px" placeholder="核准或退回的理由（必填）"></textarea></label>' +
        '</div>' +
        '<div class="btn-row" style="margin-top:var(--space-4)">' +
          '<button type="button" class="btn btn--primary btn--sm" data-toast="示意：核准退款需二次確認">核准退款</button>' +
          '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：延長寄回期限（僅可一次）">延長期限</button>' +
          '<button type="button" class="btn btn--danger btn--sm" data-toast="示意：退回申請需填寫理由">退回申請</button>' +
        '</div>' +
        '<p class="tiny muted" style="margin-top:var(--space-3)">退款執行由財務／SuperAdmin 於退款頁完成，並需 TOTP 二次驗證。</p>' +
      '</div></div>' +
    '</section>' : '';

    return '' +
      pageHead({
        crumbs: ['後台', '退貨退款'], title: '退貨退款審核',
        lede: '收貨、檢查、核准與退款交接。逾期未寄回的案件會標示提醒。'
      }) +
      '<div class="grid grid--4">' +
        '<div class="stat"><span class="stat__label">待收貨</span><span class="stat__value">1</span></div>' +
        '<div class="stat"><span class="stat__label">檢查中</span><span class="stat__value">1</span></div>' +
        '<div class="stat"><span class="stat__label">待客戶回覆</span><span class="stat__value">1</span></div>' +
        '<div class="stat stat--alert"><span class="stat__label">逾期未寄回</span><span class="stat__value">1</span></div>' +
      '</div>' +
      '<div class="filter-bar">' +
        '<label class="field" style="flex:1 1 200px"><span class="field__label">關鍵字</span><input class="input" type="search" placeholder="RMA / 訂單編號" /></label>' +
        '<label class="field"><span class="field__label">狀態</span><select class="select"><option>全部</option><option>待收貨</option><option>檢查中</option><option>待客戶回覆</option><option>已退款</option><option>逾期未寄回</option></select></label>' +
        '<div class="field"><span class="field__label">注意旗標</span><div class="chip-row">' +
          '<button type="button" class="chip" aria-pressed="true">僅逾期</button>' +
          '<button type="button" class="chip" aria-pressed="false">高金額</button></div></div>' +
      '</div>' +
      tableScroll(
        '<th>退貨編號</th><th>原訂單</th><th>商品</th><th>原因</th><th class="num">退款金額</th><th>狀態</th><th>寄回期限</th><th>處理人</th><th></th>',
        rows
      ) +
      detail;
  }

  /* ---------- 9. 營運報表（客服售後範圍） ---------- */

  function analytics() {
    return '' +
      pageHead({
        crumbs: ['後台', '報表'], title: '營運報表（客服售後）',
        lede: '本頁僅涵蓋客服、檢舉與退貨退款範圍，不含全站銷售與庫存報表。',
        actions: '<button type="button" class="btn btn--secondary btn--sm" data-toast="示意：匯出 CSV">匯出</button>'
      }) +
      '<div class="filter-bar">' +
        '<label class="field"><span class="field__label">統計區間</span><select class="select"><option>近 7 日</option><option>近 30 日</option><option>本月</option></select></label>' +
        '<label class="field"><span class="field__label">報表</span><select class="select"><option>客服案件量與 SLA</option><option>檢舉處理結果</option><option>退貨原因分布</option></select></label>' +
        '<label class="field"><span class="field__label">承接人員</span><select class="select"><option>全部</option>' +
          D.agents.map(function (a) { return '<option>' + esc(a) + '</option>'; }).join('') + '</select></label>' +
      '</div>' +
      '<div class="grid grid--4">' +
        '<div class="stat"><span class="stat__label">新增案件</span><span class="stat__value">59</span><span class="stat__note">較上週 +8</span></div>' +
        '<div class="stat stat--good"><span class="stat__label">首回覆達成率</span><span class="stat__value">92%</span><span class="stat__note">目標 90%</span></div>' +
        '<div class="stat stat--alert"><span class="stat__label">逾時案件</span><span class="stat__value">4</span><span class="stat__note">較上週 +1</span></div>' +
        '<div class="stat"><span class="stat__label">平均結案時數</span><span class="stat__value">18.4</span><span class="stat__note">小時</span></div>' +
      '</div>' +
      '<div class="grid" style="grid-template-columns:minmax(0,3fr) minmax(280px,2fr)">' +
        '<section class="card"><h3 style="font-size:var(--fs-h3);margin-bottom:var(--space-3)">案件分類分布（近 7 日）</h3>' +
          '<ul class="bars">' +
            [[18, '訂單', ''], [14, '瑕疵', 'bar--info'], [12, '退貨', 'bar--butter'], [9, '保固', 'bar--mint'], [6, '其他', '']].map(function (d) {
              return '<li><span class="bar ' + d[2] + '" style="height:' + Math.round(d[0] / 18 * 100) + '%"></span>' +
                '<span class="bar__label">' + d[1] + '</span></li>';
            }).join('') +
          '</ul>' +
          '<ul class="legend" style="margin-top:var(--space-3)">' +
            '<li><span class="swatch"></span>訂單／其他</li>' +
            '<li><span class="swatch swatch--info"></span>商品瑕疵</li>' +
            '<li><span class="swatch swatch--butter"></span>退貨諮詢</li>' +
            '<li><span class="swatch swatch--mint"></span>保固諮詢</li>' +
          '</ul>' +
        '</section>' +
        '<section class="card"><h3 style="font-size:var(--fs-h3);margin-bottom:var(--space-3)">承接人員負載</h3>' +
          [['林佩儀', 82, ''], ['陳柏勳', 64, ''], ['黃品瑄', 95, 'meter__fill--warn'], ['未指派', 22, 'meter__fill--danger']].map(function (a) {
            return '<div class="meter" style="margin-bottom:var(--space-3)">' +
              '<div style="display:flex;justify-content:space-between" class="tiny"><span>' + esc(a[0]) + '</span><span class="num">' + a[1] + '%</span></div>' +
              '<div class="meter__track"><div class="meter__fill ' + a[2] + '" style="width:' + a[1] + '%"></div></div></div>';
          }).join('') +
        '</section>' +
      '</div>' +
      '<section class="card card--flush"><div class="card__head"><h3>明細</h3>' +
        '<span class="tiny muted">僅示意欄位配置</span></div>' +
        tableScroll(
          '<th>日期</th><th class="num">新增案件</th><th class="num">結案</th><th class="num">逾時</th><th class="num">檢舉</th><th class="num">退貨申請</th><th class="num">退款金額</th>',
          [['08-21', 8, 7, 0, 1, 2, 8570], ['08-22', 12, 9, 1, 0, 3, 24180], ['08-23', 6, 8, 0, 2, 1, 3280],
           ['08-24', 15, 11, 2, 1, 4, 51900], ['08-25', 11, 10, 1, 1, 2, 5880], ['08-26', 4, 6, 0, 2, 1, 46800],
           ['08-27', 3, 5, 0, 1, 1, 1590]].map(function (r) {
            return '<tr><td>' + r[0] + '</td><td class="num">' + r[1] + '</td><td class="num">' + r[2] + '</td>' +
              '<td class="num">' + (r[3] ? '<span class="badge badge--failed">' + r[3] + '</span>' : r[3]) + '</td>' +
              '<td class="num">' + r[4] + '</td><td class="num">' + r[5] + '</td><td class="num">' + money(r[6]) + '</td></tr>';
          }).join('')
        ) +
      '</section>';
  }

  global.AdminViews = {
    pages: {
      dashboard: dashboard,
      members: members,
      products: productsAdmin,
      orders: ordersAdmin,
      promotions: promotionsAdmin,
      cases: cases,
      reports: reportsAdmin,
      returns: returnsAdmin,
      analytics: analytics
    }
  };
})(window);
