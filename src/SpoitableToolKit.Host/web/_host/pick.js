// ネイティブの `<select>` をページの中に描くものへ差し替える。
//
// ネイティブのドロップダウンは CEF から別レイヤーとして飛んでくるので、
// 画面外に描いて絵を取り出す VR の面には乗らない（合成しない限り出ない）。
// VR 以外でも、ネイティブの `<select>` はテーマが効かず、窓側でもそこだけ配色から浮く。
//
// 元の `<select>` は残す。隠して置いておき、値も change もそのまま流す。
// こうすると `el.logSize.value` を読んでいる側も、`change` を待っている側も何も直さなくてよい。
// 差し替えではなく上被せ。
//
// シェル（spoitable://ui）からもモジュールのページ（spoitable://<Id>）からも読める
// （`_host/` はどの origin からでもシェルの web/ から配られる）。
(() => {
  const STYLE = `
    .spoitable-pick-box { position: relative; display: inline-block; }
    .spoitable-pick-box > select { display: none; }
    /* 幅は中身に任せる。min-width を 100% にすると、箱の幅は中身で決まるので取り合いになり、
       flex の中（設定画面・心拍パネル）で崩れる。
       ⚠ ここは JS のテンプレート文字列の中。バッククォートを書くと文字列がそこで閉じ、
       この部品が丸ごと動かなくなる。 */
    .spoitable-pick-value {
      font: inherit; font-size: 12px; padding: 3px 22px 3px 8px; cursor: pointer; text-align: left;
      border: 1px solid var(--border); border-radius: var(--radius-sm);
      background: var(--bg); color: var(--fg);
    }
    /* 開いていることが分かる印。文字で足さない（訳が要るものを増やさない）。 */
    .spoitable-pick-box::after {
      content: ''; position: absolute; right: 8px; top: 50%; margin-top: -2px; pointer-events: none;
      border: 4px solid transparent; border-top-color: var(--muted);
    }
    /* ⚠ 一覧は body に置いて fixed で出す。箱の中に absolute で出すと、
       設定の行（畳みの高さを動かすため overflow: hidden）に切り取られる。
       囲いが切るかどうかは部品の側から知りようがないので、囲いの外へ出す。 */
    /* 中は em で書く（基準は一覧の 12px）。VR の窓は shell.css がこの基準だけ大きくする
       （12px のままだと手首の窓で 3 mm の字になる）。 */
    .spoitable-pick-list {
      position: fixed; z-index: 60; max-height: 15em; overflow-y: auto;
      width: max-content; max-width: 25em; font-size: 12px;
      border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--pop);
      box-shadow: var(--shadow-pop);
    }
    .spoitable-pick-list[hidden] { display: none; }
    .spoitable-pick-list button {
      display: block; width: 100%; text-align: left; font: inherit; font-size: 1em;
      padding: .333em .667em; cursor: pointer; border: 0; background: none; color: var(--fg); white-space: nowrap;
    }
    .spoitable-pick-list button:hover { background: color-mix(in srgb, var(--pop) 70%, var(--accent)); }
    .spoitable-pick-list button[aria-selected="true"] { color: var(--accent); }
  `;

  let styled = false;

  function ensureStyle() {
    if (styled) return;

    // 自分で持ち込む。`/_host/ui.css` を読まないページでも、隠す・一覧を浮かせるが効いて部品として動く。
    // head の最後に足すので、同じ詳細度なら ui.css やページの CSS より後に来て勝つ。色はトークンから引くので、テーマには乗る。
    const style = document.createElement('style');
    style.textContent = STYLE;
    document.head.appendChild(style);
    styled = true;
  }

  /**
   * 浮かせたものを、押したものの下へ置く。<b>`when.js` も同じものを使う</b> —
   * 出し方が 2 通りあると、片方だけ切り取られる形が残る。
   *
   * **入り切らなければ上へ返す。** 画面の下端で開くと、そのままでは中身が見えない。
   */
  function place(popup, anchor) {
    // 測る前に位置を落とす。**端に寄せたままだと、畳まれた大きさで測ることになる。**
    popup.style.top = '0px';
    popup.style.left = '0px';
    popup.style.minWidth = `${anchor.getBoundingClientRect().width}px`;

    const at = anchor.getBoundingClientRect();
    const size = popup.getBoundingClientRect();

    const below = at.bottom + 2;
    const top = below + size.height <= window.innerHeight ? below : at.top - size.height - 2;

    popup.style.top = `${Math.max(0, top)}px`;
    popup.style.left = `${Math.max(0, Math.min(at.left, window.innerWidth - size.width))}px`;
  }

  let openList = null;

  /** 開いている一覧の持ち主。**body に置いてあるので、親を辿っても箱には戻れない。** */
  let openBox = null;

  let openAnchor = null;

  function closeOpen() {
    if (!openList) return;

    openList.hidden = true;
    openList = null;
    openBox = null;
    openAnchor = null;
  }

  // 外を押したら閉じる。**capture で拾う** — 中のボタンの click より先に来ると閉じてしまうので、
  // 押した先が自分の中かどうかを見てから決める。
  window.addEventListener('mousedown', event => {
    if (!openList) return;
    if (openBox?.contains(event.target) || openList.contains(event.target)) return;

    closeOpen();
  }, true);

  window.addEventListener('keydown', event => {
    if (event.key === 'Escape') closeOpen();
  }, true);

  // 下の面が動いたら付いていく。fixed で置いてあるので、放っておくと置き去りになる。
  //
  // ⚠ 閉じてはいけない。ログの面は行が届くたびに自分で動くので、動いたら閉じる作りだと
  // 開けた直後に閉じて使えない。
  //
  // ⚠ 一覧自身が動いたぶんも数えない。開くときに選んである所まで送るので、
  // 数えるとその場で置き直しが走る。
  window.addEventListener('scroll', follow, true);
  window.addEventListener('resize', follow);

  function follow(event) {
    if (!openList || !openAnchor) return;
    if (event.target === openList) return;

    // 押したものを抱えていない面の動きは関係ない。
    if (event.type === 'scroll' && event.target instanceof Node && !event.target.contains(openAnchor)) return;

    place(openList, openAnchor);
  }

  /**
   * 1 つ上被せする。既に被せてあるものは触らない。
   *
   * **選択肢は開くたびに組み直す。** `<option>` があとから足される作りの画面があるので、
   * 作ったときの中身を覚えておくと古いものが出る。
   */
  function upgrade(select) {
    if (select.parentElement?.classList.contains('spoitable-pick-box')) return;

    ensureStyle();

    const box = document.createElement('div');
    box.className = 'spoitable-pick-box';
    select.parentElement.insertBefore(box, select);
    box.appendChild(select);

    const value = document.createElement('button');
    value.type = 'button';
    value.className = 'spoitable-pick-value';

    const list = document.createElement('div');
    list.className = 'spoitable-pick-list';
    list.hidden = true;
    list.setAttribute('role', 'listbox');

    function label() {
      value.textContent = select.options[select.selectedIndex]?.textContent ?? '';
    }

    function build() {
      list.textContent = '';

      for (const option of select.options) {
        const choice = document.createElement('button');
        choice.type = 'button';
        choice.textContent = option.textContent;
        choice.dataset.value = option.value;
        choice.setAttribute('role', 'option');
        choice.setAttribute('aria-selected', String(option.value === select.value));

        choice.onclick = () => {
          closeOpen();

          if (option.value === select.value) return;

          select.value = option.value;
          label();

          // **元の欄が正。** 値を書いたら change を出す（直に書いてもブラウザは出さない）。
          select.dispatchEvent(new Event('change', { bubbles: true }));
        };

        list.appendChild(choice);
      }
    }

    value.onclick = () => {
      const wasOpen = openList === list;
      closeOpen();

      if (wasOpen) return;

      build();
      list.hidden = false;
      openList = list;
      openBox = box;
      openAnchor = value;

      place(list, value);

      // 選んであるところまで送る。分の選択（60 通り）は箱に収まらないので、
      // 送らないといつも 00 分から探すことになる。
      //
      // ⚠ `scrollIntoView` は使わない。先祖の面まで巻くので、設定画面では画面ごと飛ぶ。
      const chosen = list.querySelector('[aria-selected="true"]');
      if (chosen) list.scrollTop = Math.max(0, chosen.offsetTop - (list.clientHeight - chosen.offsetHeight) / 2);
    };

    box.appendChild(value);

    // 一覧は箱の外。**囲いが切る場所に置かない**（上の STYLE の注を見ること）。
    document.body.appendChild(list);

    // 外から `select.value = …` で変えられても追いつく。
    select.addEventListener('change', label);

    // ⚠ 字は後から入ることがある。モジュールのパネルは `data-i18n` だけ書いた空の `<option>` を並べ、
    // 訳が届いてから字を入れる（心拍パネルがそう）。作ったときに 1 度読むだけだと、ボタンが空のまま固まる。
    new MutationObserver(label).observe(select, { childList: true, subtree: true, characterData: true });

    label();
  }

  /** その中の `<select>` を全部。あとから作った要素にも使える。 */
  function upgradeAll(root) {
    for (const select of (root ?? document).querySelectorAll('select')) upgrade(select);
  }

  window.spoitablePick = { upgrade, upgradeAll, place };
})();
