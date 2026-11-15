// ネイティブの `<input type="datetime-local">` と `<input type="date">` をページの中に描くものへ差し替える。
//
// 日付だけ（`type="date"`）は時・分の段を出さず、「今」は「今日」になる。
// `required` が付いていれば「消す」を出さない（空にできない欄）。
//
// ネイティブのカレンダーは CEF から別レイヤーとして飛んでくるので、
// 画面外に描いて絵を取り出す VR の面には乗らない。`pick.js`（`<select>`）と同じ話。
//
// 時刻もドロップダウンにする。数字の欄にすると VR で手打ちが要るが、
// 選ぶ形ならレーザーだけで最後まで行ける。時と分は `pick.js` の部品をそのまま使う。
//
// 元の `<input>` は残す。隠して置いておき、値も `change` もそのまま流すので、
// `el.logFrom.value` を読む側も `change` を待つ側も何も直さなくてよい。差し替えではなく上被せ。
(() => {
  const STYLE = `
    .spoitable-when-box { position: relative; display: inline-block; }
    .spoitable-when-box > input { display: none; }
    .spoitable-when-value {
      font: inherit; font-size: 12px; padding: 3px 8px; cursor: pointer; text-align: left;
      border: 1px solid var(--border); border-radius: var(--radius-sm);
      background: var(--bg); color: var(--fg); min-width: 118px;
    }
    .spoitable-when-value.spoitable-when-empty { color: var(--muted); }
    /* ⚠ body に置いて fixed で出す。箱の中に absolute で出すと、
       囲いが overflow: hidden なら切り取られる。
       置き場所は spoitablePick.place が決める（出し方を 2 通りにしない）。
       ⚠ ここは JS のテンプレート文字列の中。バッククォートを書くと文字列がそこで閉じる。 */
    /* 中は em（基準は暦の 12px。pick.js の一覧と同じく、VR の窓は shell.css がこの基準だけ大きくする）。 */
    .spoitable-when-pop {
      position: fixed; z-index: 60; padding: .5em; font-size: 12px;
      border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--pop);
      box-shadow: var(--shadow-pop);
    }
    .spoitable-when-pop[hidden] { display: none; }
    .spoitable-when-head { display: flex; align-items: center; gap: .333em; margin-bottom: .333em; }
    .spoitable-when-head .spoitable-when-label { flex: 1; text-align: center; font-size: 1em; }
    .spoitable-when-head button {
      font: inherit; font-size: 1em; line-height: 1; padding: .25em .583em; cursor: pointer;
      border: 1px solid var(--border); border-radius: var(--radius-sm);
      background: var(--bg); color: var(--fg);
    }
    .spoitable-when-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 1px; }
    .spoitable-when-grid .spoitable-when-dow { font-size: .833em; color: var(--muted); text-align: center; padding: .167em 0; }
    .spoitable-when-grid button {
      font: inherit; font-size: 1em; padding: .25em 0; cursor: pointer; min-width: 2.167em;
      border: 1px solid transparent; border-radius: var(--radius-sm);
      background: none; color: var(--fg);
    }
    /* 前後の月の日。**消さずに薄くする** — 消すと桝がずれて曜日が合わなくなる。 */
    .spoitable-when-grid button.spoitable-when-other { color: var(--muted); }
    .spoitable-when-grid button:hover { background: color-mix(in srgb, var(--pop) 70%, var(--accent)); }
    .spoitable-when-grid button.spoitable-when-today { border-color: var(--border); }
    .spoitable-when-grid button[aria-selected="true"] { background: var(--accent); color: var(--bg); }
    .spoitable-when-time { display: flex; align-items: center; gap: .333em; margin-top: .5em; }
    .spoitable-when-time .spoitable-when-sep { color: var(--muted); }
    .spoitable-when-foot { display: flex; gap: .333em; margin-top: .5em; }
    .spoitable-when-foot button {
      flex: 1; font: inherit; font-size: 1em; padding: .25em 0; cursor: pointer;
      border: 1px solid var(--border); border-radius: var(--radius-sm);
      background: var(--bg); color: var(--fg);
    }
  `;

  let styled = false;

  function ensureStyle() {
    if (styled) return;

    // **自分で持ち込む。** `/_host/ui.css` を読まないページでも、隠す・暦を浮かせるが効いて部品として動く。
    // head の最後に足すので、同じ詳細度なら ui.css やページの CSS より後に来て勝つ。色はトークンから引くので、テーマには乗る。
    const style = document.createElement('style');
    style.textContent = STYLE;
    document.head.appendChild(style);
    styled = true;
  }

  // 訳が要るのはこの 3 つだけ。**月名と曜日は `Intl` から取る**ので、翻訳キーを増やさない。
  let labels = { now: 'Now', today: 'Today', clear: 'Clear' };

  /** 文言を差し替える。言語が変わったらもう 1 度呼べばよい（開くたびに読み直す）。 */
  function setLabels(next) {
    labels = { ...labels, ...next };
  }

  let openPop = null;

  /** 開いている暦の持ち主。**body に置いてあるので、親を辿っても箱には戻れない。** */
  let openBox = null;

  let openAnchor = null;

  function closeOpen() {
    if (!openPop) return;

    openPop.hidden = true;
    openPop = null;
    openBox = null;
    openAnchor = null;
  }

  // 外を押したら閉じる。**capture で拾う**が、押した先が自分の中なら閉じない。
  window.addEventListener('mousedown', event => {
    if (!openPop) return;
    if (openPop.contains(event.target) || openBox?.contains(event.target)) return;

    // ⚠ **時・分の一覧も body に居る。** 暦の中には入っていないので、
    // ここで外さないと<b>時刻を選んだ瞬間に暦が閉じる</b>。
    if (event.target instanceof Element && event.target.closest('.spoitable-pick-list')) return;

    closeOpen();
  }, true);

  // 下の面が動いたら付いていく。fixed で置いてあるので、放っておくと置き去りになる。
  //
  // ⚠ 閉じてはいけない。ログの面は行が届くたびに自分で動くので、
  // 動いたら閉じる作りだと開けた直後に閉じる。
  window.addEventListener('scroll', follow, true);
  window.addEventListener('resize', follow);

  function follow(event) {
    if (!openPop || !openAnchor) return;

    // 時・分の一覧が中を送ったぶんは関係ない（暦の中には居ないが、暦の操作の一部）。
    if (event.target instanceof Element && event.target.classList.contains('spoitable-pick-list')) return;

    // **押したものを抱えていない面の動きは関係ない。**
    if (event.type === 'scroll' && event.target instanceof Node && !event.target.contains(openAnchor)) return;

    window.spoitablePick?.place(openPop, openAnchor);
  }
  window.addEventListener('resize', closeOpen);

  window.addEventListener('keydown', event => {
    if (event.key === 'Escape') closeOpen();
  }, true);

  const pad = number => String(number).padStart(2, '0');

  /** `Date` → `date` の文字列（`YYYY-MM-DD`）。地元の日付。 */
  function toDay(date) {
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  }

  /** `Date` → `datetime-local` の文字列。**タイムゾーンを持たない**ので、地元の時刻をそのまま並べる。 */
  function toValue(date) {
    return toDay(date) + `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  /**
   * `datetime-local` / `date` の文字列 → `Date`。読めなければ `null`。日付だけなら 0:00。
   *
   * ⚠ **`new Date(文字列)` に渡さない。** 形によっては UTC として解釈され、地元の時刻とずれる。
   */
  function toDate(value) {
    const found = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/.exec(value ?? '');
    if (!found) return null;

    const [, year, month, day, hour = 0, minute = 0] = found.map(part => part === undefined ? undefined : Number(part));
    return new Date(year, month - 1, day, hour, minute);
  }

  const language = () => document.documentElement.lang || 'en';

  /** 曜日の頭文字。**日曜始まり**（en も ja も日曜始まりなので、暦の情報は引かない）。 */
  function weekdays() {
    const format = new Intl.DateTimeFormat(language(), { weekday: 'narrow' });

    // new Date(2026, 8, 6) は日曜。ここを起点に 7 日ぶん。
    return Array.from({ length: 7 }, (_, index) => format.format(new Date(2026, 8, 6 + index)));
  }

  function monthLabel(date) {
    return new Intl.DateTimeFormat(language(), { year: 'numeric', month: 'long' }).format(date);
  }

  /** 時 / 分のドロップダウン。**`pick.js` の部品をそのまま使う**ので、VR でも押せる。 */
  function numbers(count) {
    const select = document.createElement('select');

    for (let value = 0; value < count; value++) {
      const option = document.createElement('option');
      option.value = String(value);
      option.textContent = pad(value);
      select.appendChild(option);
    }

    return select;
  }

  /**
   * 1 つ上被せする。既に被せてあるものは触らない。
   */
  function upgrade(input) {
    if (input.parentElement?.classList.contains('spoitable-when-box')) return;

    ensureStyle();

    const dateOnly = input.type === 'date';
    const format = date => dateOnly ? toDay(date) : toValue(date);

    const box = document.createElement('div');
    box.className = 'spoitable-when-box';
    input.parentElement.insertBefore(box, input);
    box.appendChild(input);

    const value = document.createElement('button');
    value.type = 'button';
    value.className = 'spoitable-when-value';

    const pop = document.createElement('div');
    pop.className = 'spoitable-when-pop';
    pop.hidden = true;

    // いま見ている月。**選んだ日とは別**（空のまま月をめくれる）。
    let shown = new Date();

    function label() {
      const at = toDate(input.value);

      value.classList.toggle('spoitable-when-empty', !at);
      value.textContent = !at ? '—'
        : dateOnly ? toDay(at)
        : `${toDay(at)} ${pad(at.getHours())}:${pad(at.getMinutes())}`;
    }

    /** 値を書いて `change` を出す。**元の欄が正。**（直に書いてもブラウザは出さない） */
    function commit(next) {
      input.value = next;
      label();
      input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    const head = document.createElement('div');
    head.className = 'spoitable-when-head';

    const previous = document.createElement('button');
    previous.type = 'button';
    previous.textContent = '‹';

    const title = document.createElement('span');
    title.className = 'spoitable-when-label';

    const next = document.createElement('button');
    next.type = 'button';
    next.textContent = '›';

    head.append(previous, title, next);

    const grid = document.createElement('div');
    grid.className = 'spoitable-when-grid';

    const time = document.createElement('div');
    time.className = 'spoitable-when-time';

    const hours = numbers(24);
    const minutes = numbers(60);
    const separator = document.createElement('span');
    separator.className = 'spoitable-when-sep';
    separator.textContent = ':';

    time.append(hours, separator, minutes);

    // ⚠ 隠すのは style で。上の STYLE に足すと、VR の窓向けに STYLE を em へ書き換える所とぶつかる。
    if (dateOnly) time.style.display = 'none';

    const foot = document.createElement('div');
    foot.className = 'spoitable-when-foot';

    const now = document.createElement('button');
    now.type = 'button';

    const clear = document.createElement('button');
    clear.type = 'button';

    if (input.required) clear.style.display = 'none';

    foot.append(now, clear);
    pop.append(head, grid, time, foot);

    /** いま選ばれている時刻。空なら 0:00 として扱う（日を押した瞬間に値が要るため）。 */
    function chosenTime() {
      const at = toDate(input.value);
      return at ? { hour: at.getHours(), minute: at.getMinutes() } : { hour: 0, minute: 0 };
    }

    function paint() {
      title.textContent = monthLabel(shown);
      grid.textContent = '';

      for (const day of weekdays()) {
        const head = document.createElement('div');
        head.className = 'spoitable-when-dow';
        head.textContent = day;
        grid.appendChild(head);
      }

      const chosen = toDate(input.value);
      const today = new Date();

      // その月の 1 日が入る桝まで戻る。**前月ぶんも桝を埋める**（消すと曜日がずれる）。
      const first = new Date(shown.getFullYear(), shown.getMonth(), 1);
      const start = new Date(first);
      start.setDate(1 - first.getDay());

      for (let index = 0; index < 42; index++) {
        const at = new Date(start.getFullYear(), start.getMonth(), start.getDate() + index);

        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = String(at.getDate());
        button.classList.toggle('spoitable-when-other', at.getMonth() !== shown.getMonth());

        const same = (a, b) => a.getFullYear() === b.getFullYear()
          && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();

        button.classList.toggle('spoitable-when-today', same(at, today));
        button.setAttribute('aria-selected', String(!!chosen && same(at, chosen)));

        button.onclick = () => {
          const { hour, minute } = chosenTime();
          const picked = new Date(at.getFullYear(), at.getMonth(), at.getDate(), hour, minute);

          // 押した日が前後の月なら、**そちらへめくってから**塗り直す（押した日が消えると迷う）。
          shown = new Date(at.getFullYear(), at.getMonth(), 1);

          commit(format(picked));
          paint();
        };

        grid.appendChild(button);
      }

      // ⚠ 塗り直しでは値を書き返さない。時・分に `change` を出すのは
      // 被せた側の見た目を追いつかせるためだけで、これを `retime` が拾うと
      // 「消す」がその場で打ち消される（空 → 今日の 00:00 が入る）。
      painting = true;

      const { hour, minute } = chosenTime();
      hours.value = String(hour);
      minutes.value = String(minute);
      hours.dispatchEvent(new Event('change', { bubbles: true }));
      minutes.dispatchEvent(new Event('change', { bubbles: true }));

      painting = false;

      now.textContent = dateOnly ? labels.today : labels.now;
      clear.textContent = labels.clear;
    }

    /// 塗り直しの最中か。**自分が出した `change` を自分で拾わない**ための印。
    let painting = false;

    function retime() {
      if (painting) return;

      // **日がまだ無いなら今日にする。** 時刻だけ選んで値が空のままだと、押した手応えが無い。
      const at = toDate(input.value) ?? new Date();

      commit(toValue(new Date(
        at.getFullYear(), at.getMonth(), at.getDate(), Number(hours.value), Number(minutes.value))));
    }

    hours.addEventListener('change', retime);
    minutes.addEventListener('change', retime);

    previous.onclick = () => {
      shown = new Date(shown.getFullYear(), shown.getMonth() - 1, 1);
      paint();
    };

    next.onclick = () => {
      shown = new Date(shown.getFullYear(), shown.getMonth() + 1, 1);
      paint();
    };

    now.onclick = () => {
      const at = new Date();
      shown = new Date(at.getFullYear(), at.getMonth(), 1);

      commit(format(at));
      paint();
    };

    clear.onclick = () => {
      commit('');
      paint();
    };

    value.onclick = () => {
      const wasOpen = openPop === pop;
      closeOpen();

      if (wasOpen) return;

      // 開くときは**選んである月**を見せる（空なら今月）。
      const at = toDate(input.value) ?? new Date();
      shown = new Date(at.getFullYear(), at.getMonth(), 1);

      paint();
      pop.hidden = false;
      openPop = pop;
      openBox = box;
      openAnchor = value;

      window.spoitablePick?.place(pop, value);
    };

    box.appendChild(value);

    // 暦は箱の外。**囲いが切る場所に置かない**（上の STYLE の注を見ること）。
    document.body.appendChild(pop);

    // 時と分は `<select>` のまま置いてあるので、押せる形に被せる（日付だけの欄では出さないので要らない）。
    if (!dateOnly) {
      window.spoitablePick?.upgrade(hours);
      window.spoitablePick?.upgrade(minutes);
    }

    // 外から `input.value = …` で変えられても追いつく。
    input.addEventListener('change', label);
    label();
  }

  /** その中の `datetime-local` と `date` を全部。あとから作った要素にも使える。 */
  function upgradeAll(root) {
    for (const input of (root ?? document).querySelectorAll('input[type="datetime-local"], input[type="date"]')) upgrade(input);
  }

  window.spoitableWhen = { upgrade, upgradeAll, setLabels };
})();
