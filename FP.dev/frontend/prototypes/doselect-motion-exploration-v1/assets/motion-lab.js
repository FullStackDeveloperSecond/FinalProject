/* GSAP 動態方案 A／B／C 比較頁。
   使用 npm 安裝、版本固定 3.15.0 的同一份 GSAP，不走 CDN。
   本頁不連線任何 API、不含帳號或 token，所有文字皆為示意假資料。 */

import gsap from '/customer-web/node_modules/gsap/index.js';
import { PRESETS, PRESET_IDS, SCENARIOS } from './presets.js';

const state = {
  presetId: 'gentle',
  /** 強制 reduced motion：模擬使用者系統偏好，供對照截圖使用。 */
  forceReduced: false
};

const preset = () => PRESETS[state.presetId];

/** gsap.context().add(fn) 會立即執行 fn 但不回傳它的結果；這裡把 tween 帶出來。 */
function runIn(ctx, build) {
  let tween = null;
  ctx.add(() => { tween = build(); });
  return tween;
}

/** 系統偏好 or 頁面上的強制開關。 */
function isReduced() {
  if (state.forceReduced) { return true; }
  if (typeof window.matchMedia !== 'function') { return true; }
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/* ---------------------------------------------------------------------------
   helpers：與 shared/src/motion/helpers.ts 同一套語意
   —— 一律 gsap.from()／fromTo()，reduced 時回傳 null 不建立任何 tween。
   --------------------------------------------------------------------------- */

function reveal(targets, ctx) {
  if (isReduced() || !targets) { return null; }
  const p = preset().reveal;
  return runIn(ctx, () => gsap.from(targets, {
    opacity: 0, y: p.y, scale: p.scaleFrom,
    duration: p.duration, ease: p.ease, clearProps: 'transform,opacity'
  }));
}

function stagger(targets, ctx, delay = 0) {
  if (isReduced() || !targets || targets.length === 0) { return null; }
  const p = preset().stagger;
  const animated = Array.from(targets).slice(0, p.maxItems);
  return runIn(ctx, () => gsap.from(animated, {
    opacity: 0, y: p.y, scale: p.scaleFrom,
    duration: p.duration, ease: p.ease, stagger: p.each, delay,
    clearProps: 'transform,opacity'
  }));
}

function panelEnter(target, ctx) {
  if (isReduced() || !target) { return null; }
  const p = preset().panel;
  return runIn(ctx, () => gsap.from(target, {
    opacity: 0, x: p.x, scale: p.scaleFrom,
    duration: p.duration, ease: p.ease, clearProps: 'transform,opacity'
  }));
}

function panelLeave(target, ctx, done) {
  if (isReduced() || !target) { done(); return null; }
  const p = preset().panel;
  return runIn(ctx, () => gsap.to(target, {
    opacity: 0, x: p.x, duration: p.leaveDuration, ease: p.leaveEase, onComplete: done
  }));
}

function pulse(target, ctx) {
  if (isReduced() || !target || target.disabled || target.getAttribute('aria-busy') === 'true') { return null; }
  const p = preset().feedback;
  return runIn(ctx, () => gsap.fromTo(target,
    { scale: 1 },
    { scale: p.scaleTo, duration: p.duration, ease: p.ease, yoyo: true, repeat: 1, clearProps: 'transform' }
  ));
}

function shake(target, ctx) {
  if (isReduced() || !target) { return null; }
  const p = preset().shake;
  return runIn(ctx, () => gsap.fromTo(target,
    { x: -p.x },
    { x: p.x, duration: p.duration, ease: p.ease, yoyo: true, repeat: p.repeat, clearProps: 'transform' }
  ));
}

/* ---------------------------------------------------------------------------
   舞台：每個情境一個 gsap.context，重播前先 revert，切換方案時全部重建。
   --------------------------------------------------------------------------- */

/** 「全部重播」時不搶焦點；只有單獨播放某個情境才移動焦點。 */
let bulkReplay = false;

const contexts = new Map();

function contextFor(id, scope) {
  const existing = contexts.get(id);
  if (existing) { existing.revert(); }
  const ctx = gsap.context(() => {}, scope);
  contexts.set(id, ctx);
  return ctx;
}

function revertAll() {
  contexts.forEach(ctx => ctx.revert());
  contexts.clear();
}

const el = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) { node.className = className; }
  if (text !== undefined) { node.textContent = text; }
  return node;
};

/** 每個 kind 對應一組假 UI 與一個 play(ctx, stage)。 */
const STAGES = {
  reveal(stage, scenario) {
    stage.replaceChildren();
    const box = el('div', 'mock-donggu');
    box.append(el('span', 'mock-donggu__mark', 'Donggu 圖片預留區'));
    box.append(el('p', null, '還沒有符合的結果。要不要放寬預算，或改看其他用途？'));
    stage.append(box);
    return ctx => reveal(box, ctx);
  },

  'reveal-then-stagger'(stage, scenario) {
    stage.replaceChildren();
    const title = el('p', 'mock-title', scenario.id === 'home-hero' ? '說出需求，組出適合你的電腦' : '申請退貨');
    const row = el('div', 'mock-cards');
    const labels = scenario.id === 'home-hero'
      ? ['依用途挑選', '依預算挑選', '看全部商品']
      : ['選擇品項', '填寫原因', '上傳照片', '確認送出'];
    const items = labels.map(label => el('div', 'mock-card', label));
    items.forEach(item => row.append(item));
    stage.append(title, row);
    return (ctx) => {
      reveal(title, ctx);
      stagger(items, ctx, 0.06);
    };
  },

  stagger(stage, scenario) {
    stage.replaceChildren();
    const wrap = el('div', scenario.id === 'thread' || scenario.id === 'admin-filter' ? 'mock-lines' : 'mock-cards');
    const items = [];
    for (let index = 0; index < (scenario.items || 6); index += 1) {
      const isAdminReply = scenario.id === 'thread' && index % 2 === 1;
      const node = el(
        'div',
        wrap.className === 'mock-lines' ? `mock-line${isAdminReply ? ' mock-line--admin' : ''}` : 'mock-card',
        wrap.className === 'mock-lines'
          ? `${isAdminReply ? '客服人員' : '您'}・第 ${index + 1} 則訊息`
          : `項目 ${index + 1}`
      );
      items.push(node);
      wrap.append(node);
    }
    stage.append(wrap);
    if ((scenario.items || 0) > preset().stagger.maxItems) {
      stage.append(el('p', 'scenario__note', `共 ${scenario.items} 筆，只有前 ${preset().stagger.maxItems} 筆進場，其餘直接顯示。`));
    }
    return ctx => stagger(items, ctx);
  },

  panel(stage) {
    stage.replaceChildren();
    const split = el('div', 'mock-split');
    split.dataset.open = 'false';

    const list = el('div', 'mock-split__list');
    list.append(el('p', 'mock-title', '案件列表'));
    ['CS-0001', 'CS-0002', 'CS-0003'].forEach(id => list.append(el('div', 'mock-line', id)));
    split.append(list);
    stage.append(split);

    let detail = null;
    const build = () => {
      const node = el('div', 'mock-split__detail');
      const head = el('div', 'mock-split__detail-head');
      head.append(el('strong', null, '案件 CS-0001'));
      const close = el('button', 'mock-split__close', '×');
      close.type = 'button';
      close.setAttribute('aria-label', '關閉案件檢視');
      close.addEventListener('click', () => {
        const ctx = contextFor('panel-leave', stage);
        panelLeave(node, ctx, () => {
          node.remove();
          split.dataset.open = 'false';
          detail = null;
        });
      });
      head.append(close);
      node.append(head);
      node.append(el('p', null, '往來訊息、附件與狀態都在這一欄；列表全程留在左側，不會被蓋住。'));
      return node;
    };

    return (ctx) => {
      if (detail) { detail.remove(); }
      detail = build();
      split.dataset.open = 'true';
      split.append(detail);
      panelEnter(detail, ctx);
    };
  },

  feedback(stage, scenario) {
    stage.replaceChildren();
    const isBadge = scenario.id === 'admin-return-status';
    const target = isBadge
      ? el('span', 'mock-badge', '待審核')
      : el('span', 'mock-amount', 'NT$ 32,800');
    const line = el('p', null, isBadge ? '狀態：' : '小計：');
    line.append(target);
    stage.append(line);

    const busy = el('button', 'lab-button', '送出中（不應有動畫）');
    busy.type = 'button';
    busy.disabled = true;
    busy.setAttribute('aria-busy', 'true');
    stage.append(busy);
    stage.append(el('p', 'scenario__note', '狀態文字本身就會改變；動畫只是補強，不是唯一提示。'));

    let toggled = false;
    return (ctx) => {
      toggled = !toggled;
      if (isBadge) {
        target.textContent = toggled ? '已核准' : '待審核';
      }
      else {
        target.textContent = toggled ? 'NT$ 35,600' : 'NT$ 32,800';
      }
      pulse(target, ctx);
      // 明確示範：disabled／aria-busy 的按鈕不會產生 tween。
      pulse(busy, ctx);
    };
  },

  shake(stage) {
    stage.replaceChildren();
    const field = el('label', 'mock-field', '退款金額');
    const input = document.createElement('input');
    input.type = 'text';
    input.value = '99999';
    input.setAttribute('aria-invalid', 'true');
    input.setAttribute('aria-describedby', 'lab-conflict-error');
    field.append(input);
    const error = el('p', 'mock-error', '這筆案件已被其他人更新（RowVersion 衝突），畫面已重新載入，請重新確認金額。');
    error.id = 'lab-conflict-error';
    stage.append(field, error);
    return (ctx) => {
      shake(field, ctx);
      // 動畫不取代錯誤文字與 ARIA：焦點一定移到錯誤欄位。
      if (!bulkReplay) { input.focus(); }
    };
  },

  sidebar(stage) {
    stage.replaceChildren();
    const nav = el('div', 'mock-sidebar');
    const indicator = el('span', 'mock-sidebar__indicator');
    nav.append(indicator);
    const labels = ['首頁', '商品管理', '客服 SLA 佇列', '案件工作台', '退貨案件'];
    const buttons = labels.map((label, index) => {
      const button = el('button', null, label);
      button.type = 'button';
      if (index === 0) { button.setAttribute('aria-current', 'page'); }
      return button;
    });
    buttons.forEach(button => nav.append(button));
    stage.append(nav);

    const moveTo = (button, ctx) => {
      buttons.forEach(other => other.removeAttribute('aria-current'));
      button.setAttribute('aria-current', 'page');
      const top = button.offsetTop;
      if (isReduced()) {
        gsap.set(indicator, { y: top, height: button.offsetHeight });
        return;
      }
      const p = preset().reveal;
      runIn(ctx, () => gsap.to(indicator, {
        y: top, height: button.offsetHeight, duration: p.duration, ease: p.ease
      }));
    };

    buttons.forEach((button) => {
      button.addEventListener('click', () => moveTo(button, contextFor('sidebar-click', stage)));
    });

    let index = 0;
    return (ctx) => {
      index = (index + 1) % buttons.length;
      moveTo(buttons[index], ctx);
    };
  }
};

/* --------------------------------------------------------------------------- */

const players = new Map();

function buildScenarios() {
  const storeHost = document.querySelector('#scenarios-store');
  const adminHost = document.querySelector('#scenarios-admin');
  storeHost.replaceChildren();
  adminHost.replaceChildren();
  players.clear();

  SCENARIOS.forEach((scenario) => {
    const card = el('article', 'scenario');
    const head = el('div', 'scenario__head');
    head.append(el('h3', null, scenario.title));

    const play = el('button', 'lab-button', scenario.replayLabel || '播放');
    play.type = 'button';
    head.append(play);
    card.append(head);
    card.append(el('p', 'scenario__note', scenario.note));

    const stage = el('div', 'scenario__stage');
    stage.id = `stage-${scenario.id}`;
    card.append(stage);

    const run = STAGES[scenario.kind](stage, scenario);
    players.set(scenario.id, () => run(contextFor(scenario.id, stage)));
    play.addEventListener('click', () => players.get(scenario.id)());

    (scenario.side === 'store' ? storeHost : adminHost).append(card);
  });
}

function renderSpecTable() {
  const body = document.querySelector('#spec-body');
  body.replaceChildren();
  const rows = [
    ['進場 duration', p => `${p.reveal.duration}s`],
    ['進場 ease', p => p.reveal.ease],
    ['進場位移', p => `${p.reveal.y}px`],
    ['進場 scale', p => `${p.reveal.scaleFrom} → 1`],
    ['stagger 間隔', p => `${p.stagger.each}s`],
    ['stagger 上限', p => `${p.stagger.maxItems} 個`],
    ['面板 duration', p => `${p.panel.duration}s`],
    ['面板 ease', p => p.panel.ease],
    ['面板收起', p => `${p.panel.leaveDuration}s`],
    ['回饋 duration', p => `${p.feedback.duration}s`],
    ['回饋 ease', p => p.feedback.ease],
    ['錯誤提示', p => `${p.shake.duration}s ／ ${p.shake.ease} ／ ${p.shake.x}px ／ 往返 ${p.shake.repeat + 1} 次`]
  ];

  rows.forEach(([label, read]) => {
    const tr = document.createElement('tr');
    tr.append(el('th', null, label));
    PRESET_IDS.forEach((id) => {
      const td = el('td');
      const code = el('code', null, read(PRESETS[id]));
      td.append(code);
      tr.append(td);
    });
    body.append(tr);
  });
}

function renderPresetCards() {
  const host = document.querySelector('#preset-summary');
  host.replaceChildren();
  PRESET_IDS.forEach((id) => {
    const card = el('article', 'preset-card');
    card.dataset.active = String(id === state.presetId);
    card.append(el('h3', null, PRESETS[id].label));
    card.append(el('p', null, PRESETS[id].description));
    host.append(card);
  });
}

function syncSwitch() {
  document.querySelectorAll('.preset-switch button').forEach((button) => {
    button.setAttribute('aria-pressed', String(button.dataset.preset === state.presetId));
  });
  document.querySelector('#current-preset').textContent = PRESETS[state.presetId].label;
  document.querySelector('#reduced-state').textContent = isReduced() ? '是（不建立任何位移／縮放動畫）' : '否';
}

function playAll() {
  bulkReplay = true;
  players.forEach(play => play());
  bulkReplay = false;
}

function selectPreset(id) {
  state.presetId = id;
  revertAll();
  renderPresetCards();
  buildScenarios();
  syncSwitch();
  playAll();
}

document.querySelectorAll('.preset-switch button').forEach((button) => {
  button.addEventListener('click', () => selectPreset(button.dataset.preset));
});

document.querySelector('#toggle-reduced').addEventListener('click', (event) => {
  state.forceReduced = !state.forceReduced;
  event.currentTarget.setAttribute('aria-pressed', String(state.forceReduced));
  event.currentTarget.querySelector('span').textContent = state.forceReduced
    ? '強制 reduced motion：開'
    : '強制 reduced motion：關';
  revertAll();
  buildScenarios();
  syncSwitch();
});

// 慢速檢視：把 GSAP 全域 timeScale 降到 0.2，方便逐格比較三套方案的差異。
// 只影響本原型，不改任何 preset 數值。
document.querySelector('#toggle-slow').addEventListener('click', (event) => {
  const slow = event.currentTarget.getAttribute("aria-pressed") !== "true";
  event.currentTarget.setAttribute("aria-pressed", String(slow));
  event.currentTarget.querySelector("span").textContent = slow ? "慢速檢視：開" : "慢速檢視：關";
  gsap.globalTimeline.timeScale(slow ? 0.2 : 1);
});

document.querySelector('#replay-all').addEventListener('click', () => {
  revertAll();
  playAll();
});

renderPresetCards();
renderSpecTable();
buildScenarios();
syncSwitch();
playAll();

// 供人工檢查用：在 console 觀察是否有殘留 tween。
window.__motionLab = {
  gsapVersion: gsap.version,
  liveTweens: () => gsap.globalTimeline.getChildren(true, true, false).length,
  revertAll
};
