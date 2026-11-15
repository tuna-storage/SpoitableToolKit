// ネイティブのツールチップ（`title`）をページの中に描くものへ差し替える。
//
// `title` の吹き出しは CEF から別レイヤーとして飛んでくるので、画面外に描いて絵を取り出す
// VR の面には乗らない（`pick.js`・`when.js` と同じ話）。
//
// `title` は奪う。残したままだとネイティブの吹き出しも一緒に出て、窓側で二重に見える。
// `data-tip` へ移して、こちらで描く。
//
// ⚠ 移したあとに書き戻されることがある。シェルも `bridge.js` も、言語を当てるときに
// `el.title = …` をやり直す。だから 1 度舐めるだけでは足りず、属性を見張る。
(() => {
  const STYLE = `
    .spoitable-tip {
      position: fixed; z-index: 90; max-width: 280px; pointer-events: none;
      font-size: 11px; line-height: 1.4; padding: 3px 7px; white-space: pre-wrap;
      border: 1px solid var(--border); border-radius: var(--radius-sm);
      background: var(--pop); color: var(--fg); box-shadow: var(--shadow-pop);
    }
    .spoitable-tip[hidden] { display: none; }
  `;

  const style = document.createElement('style');
  style.textContent = STYLE;
  document.head.appendChild(style);

  const pop = document.createElement('div');
  pop.className = 'spoitable-tip';
  pop.hidden = true;
  pop.setAttribute('role', 'tooltip');

  /** 見張りが自分の書き換えに反応しないようにする印。 */
  let stealing = false;

  /** `title` を `data-tip` へ移す。空の `title` は<b>触らない</b>（意図して消してある場合がある）。 */
  function steal(element) {
    const text = element.getAttribute('title');
    if (!text) return;

    stealing = true;
    element.removeAttribute('title');
    stealing = false;

    element.dataset.tip = text;
  }

  function stealAll(root) {
    if (root.nodeType !== 1) return;

    if (root.hasAttribute('title')) steal(root);
    for (const element of root.querySelectorAll('[title]')) steal(element);
  }

  // **書き戻されたら奪い直す。** 言語を当て直すたびに `el.title = …` が走る。
  const watcher = new MutationObserver(records => {
    if (stealing) return;

    for (const record of records) {
      if (record.type === 'attributes') steal(record.target);
      else for (const added of record.addedNodes) stealAll(added);
    }
  });

  /**
   * 被せた部品が、隠してある元の欄から説明を借りる。
   *
   * 写さずに借りる。作ったときに写すと、言語を変えたときに古いまま残る
   * （元の欄は `data-i18n-title` から当て直されるが、写した先は誰も直さない）。
   */
  function borrowed(box) {
    const wrapped = box.querySelector(':scope > select, :scope > input');
    return wrapped ? wrapped.dataset.tip ?? wrapped.title ?? '' : '';
  }

  let shownFor = null;

  function hide() {
    if (!shownFor) return;

    shownFor = null;
    pop.hidden = true;
  }

  function show(element, text) {
    shownFor = element;
    pop.textContent = text;
    pop.hidden = false;

    // 下に出す。**入り切らなければ上へ返す** — 画面の端で切れると読めない。
    const at = element.getBoundingClientRect();
    const size = pop.getBoundingClientRect();

    const below = at.bottom + 4;
    const top = below + size.height <= window.innerHeight ? below : at.top - size.height - 4;

    pop.style.top = `${Math.max(0, top)}px`;
    pop.style.left = `${Math.max(0, Math.min(at.left, window.innerWidth - size.width))}px`;
  }

  function start() {
    document.body.appendChild(pop);

    stealAll(document.body);
    watcher.observe(document.body, {
      subtree: true, childList: true, attributes: true, attributeFilter: ['title'],
    });

    document.addEventListener('mouseover', event => {
      const at = event.target instanceof Element
        ? event.target.closest('[data-tip], .spoitable-pick-box, .spoitable-when-box')
        : null;

      const text = at ? at.dataset.tip || borrowed(at) : '';

      if (!text) {
        hide();
        return;
      }

      if (at === shownFor) return;

      show(at, text);
    });

    // 押したら引っ込める。**出したままだと押した先を隠す**（VR ではレーザーの先が見えなくなる）。
    document.addEventListener('mousedown', hide, true);
    document.addEventListener('wheel', hide, true);
    window.addEventListener('scroll', hide, true);
  }

  if (document.body) start();
  else document.addEventListener('DOMContentLoaded', start);
})();
