// モジュールのページからホストを呼ぶ窓口。
// 呼び出しは全部シェル（spoitable://ui）へ postMessage で渡し、シェルが「どの origin から来たか」で
// 宛先モジュールを決める。モジュール側が自分の Id を名乗る必要はないし、名乗っても信用されない。
// これは分離ではなく規約: iframe からも CefSharp.BindObjectAsync('stkHost') が通り、
// シェルが通していないホスト機能まで叩ける（bind がブラウザ単位でフレーム単位ではないため）。
//
//   <script src="/_host/bridge.js"></script>
//   const result = await spoitable.call('resolve', { url });
(() => {
  const SHELL_ORIGIN = 'spoitable://ui';

  // 空間の窓（VR）の中のパネルは巻き棒を太く・はっきりする（シェルの body.vr-window と同じ見た目）。
  // 色はテーマの --fg。theme.css を読まないパネルでは --fg が無いので、字の色（CanvasText）に落とす（無いと巻き棒が透明になる）。
  // 印はシェルが URL に付ける surface=window。パネルは iframe ごと 1.5 倍に拡大されるので、2/3 の太さで書く（見た目は 22px）。
  if (new URLSearchParams(location.search).get('surface') === 'window') {
    document.documentElement.classList.add('stk-vr-window');
    const style = document.createElement('style');
    style.textContent = `
      ::-webkit-scrollbar { width: 15px; height: 15px; }
      ::-webkit-scrollbar-track { background: color-mix(in srgb, var(--fg, CanvasText) 7%, transparent); border: 3px solid transparent; border-radius: 8px; background-clip: content-box; }
      ::-webkit-scrollbar-thumb { min-height: 32px; background: color-mix(in srgb, var(--fg, CanvasText) 42%, transparent); border: 3px solid transparent; border-radius: 8px; background-clip: content-box; }
    `;
    document.head.append(style);
  }

  let sequence = 0;
  const pending = new Map();
  const subscribers = [];

  window.addEventListener('message', event => {
    if (event.origin !== SHELL_ORIGIN) return;

    const message = event.data;
    if (!message || message.stk !== 1) return;

    if (message.kind === 'push') {
      // 設定が変わったらテーマを引き直す。シェルの読み込み直しに頼らない —
      // 前の読み込みが終わっていないと src の入れ直しは無視され、パネルだけ前の配色で取り残される。
      if (message.channel === 'configChanged') relinkTheme();

      // 購読者へは文言が届いてから渡す。push はページを読み込んだ直後にも来る（心拍は毎秒）。
      // 届く前に渡すと、購読者の描画で t() はキーを返し phrases は投げる。
      ready.then(() => {
        for (const fn of subscribers) {
          try { fn(message.channel, message.payload); }
          catch (err) { console.error('onPush の中で例外', err); }
        }
      });
      return;
    }

    const waiting = pending.get(message.id);
    if (!waiting) return;
    pending.delete(message.id);

    if (message.ok) waiting.resolve(message.result);
    else waiting.reject(new Error(message.error || 'unknown error'));
  });

  // テーマの取り直し。URL を変えないと再取得が起きないので版を付ける。
  // 時刻ではなく数える: Date.now() はミリ秒までなので、続けて呼ぶと同じ URL になる。
  // 読み込みごとの印も付ける: パネルを読み込み直すと数え直しになり、前に取った同じ URL
  // （前の配色のまま CEF に残っている）とぶつかる。
  let themeVersion = 0;
  const themeLoad = Date.now().toString(36);

  function relinkTheme() {
    const link = document.querySelector('link[href^="/_host/theme.css"]');
    if (link) link.href = '/_host/theme.css?v=' + themeLoad + '-' + (++themeVersion);
  }

  function send(kind, method, payload) {
    return new Promise((resolve, reject) => {
      const id = ++sequence;
      pending.set(id, { resolve, reject });
      parent.postMessage({ stk: 1, id, kind, method, payload }, SHELL_ORIGIN);
    });
  }

  // 画面の文言。シェルから 1 回もらって、あとは自分で引く（1 語ずつ橋を渡ると往復が増える）。
  let strings = {};
  let language = 'en';
  let arrived = false;

  function translate(key, ...args) {
    const text = Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;

    // 訳が無ければキーがそのまま出る。隠さない（訳の抜けに気付けるように）。
    return args.length ? text.replace(/\{(\d+)\}/g, (whole, index) => args[index] ?? whole) : text;
  }

  function localize(root) {
    for (const el of (root ?? document).querySelectorAll('[data-i18n]')) el.textContent = translate(el.dataset.i18n);
    for (const el of (root ?? document).querySelectorAll('[data-i18n-title]')) el.title = translate(el.dataset.i18nTitle);

    document.documentElement.lang = language;
  }

  // どのモジュールも使う短い文言を、名前で引く口（C# の CommonPhrases と同じ並び）: spoitable.phrases.ui.close
  // t() は無いキーをそのまま返すが、こちらは本体が持っている文言の名前なので、無ければ打ち間違いとして投げる。
  const PHRASES = 'common.';

  // 名前ではない問い合わせ（await・JSON.stringify・文字への変換が覗く）。投げずに「無い」と返す。
  const PROBES = new Set(['then', 'toJSON', 'constructor', 'valueOf', 'toString']);

  function phrasesUnder(prefix, path) {
    return new Proxy(Object.freeze({}), {
      get(target, name) {
        if (typeof name !== 'string' || PROBES.has(name)) return undefined;

        if (!arrived) {
          throw new Error(`${path}.${name} was read before the wording arrived. Wait first: await spoitable.whenReady`);
        }

        const key = prefix + name;
        if (Object.prototype.hasOwnProperty.call(strings, key)) return strings[key];

        const keys = Object.keys(strings).filter(k => k.startsWith(prefix));
        if (keys.some(k => k.startsWith(key + '.'))) return phrasesUnder(key + '.', `${path}.${name}`);

        const known = [...new Set(keys.map(k => k.slice(prefix.length).split('.')[0]))].sort();
        throw new Error(`${path}.${name} is not a phrase the app provides. ${path} has: ${known.join(', ')}. `
          + "Wording of your own goes in your module's locales under '<your module Id>.…' and is read with spoitable.t(key).");
      },
    });
  }

  const ready = send('host', 'strings', {})
    .then(result => {
      strings = JSON.parse(result?.strings ?? '{}');
      language = result?.language ?? language;
      arrived = true;
    })
    .catch(err => console.error('文言を受け取れなかった', err))
    .finally(() => localize());

  // メニューを描くのはシェル。パネルは「何の上で押されたか」を名乗るだけ（パネルの端でメニューが切れないように）。
  // 既定のメニューは C# 側で止めてある。ここの preventDefault は保険。
  // ⚠ シェルにも同じ形の名乗りがある（index.html の nameContext）。別 origin の別のページなので
  // コードは分けるしかない。片方だけ直さないこと。

  const contextExtractors = new Map();

  /**
   * 押した時点の入力欄と範囲。メニューを押す頃には焦点が移っているので、
   * そのとき読み直すと空になる。setRangeText は焦点が外れていても効く。
   */
  function nameContext(event) {
    const at = event.target instanceof Element ? event.target : event.target?.parentElement ?? null;
    const named = at?.closest('[data-context]') ?? null;
    const field = at?.closest('input, textarea') ?? null;

    // 選択を持てる型だけ。type=number などは setRangeText が投げる。シェル側の nameContext と同じ一覧を持つこと。
    const editable = !!field && !field.disabled && !field.readOnly
      && (field.tagName === 'TEXTAREA' || ['text', 'search', 'url', 'tel', 'password'].includes(field.type));

    const start = field?.selectionStart ?? 0;
    const end = field?.selectionEnd ?? 0;

    const key = named?.dataset.context ?? '';
    let data = {};
    if (named) {
      try { data = contextExtractors.get(key)?.(named) ?? {}; }
      catch (err) { console.error('材料の取り出しで例外', err); }
    }

    return {
      request: {
        key,
        editable,
        secret: field?.type === 'password',

        // ⚠ window.getSelection() は input の中の選択を返さない。
        selectionText: editable ? String(field.value ?? '').slice(start, end) : String(window.getSelection() ?? ''),
        data,
        x: event.clientX,
        y: event.clientY,
      },
      field: editable ? field : null,
      start,
      end,
    };
  }

  // 押したらシェルに知らせる（シェルが描いている右クリックのメニューを閉じるため）。
  // iframe の中の mousedown はシェルの窓に届かない。捕まえる段で拾う（中で止められても届く）。
  window.addEventListener('mousedown', () => parent.postMessage({ stk: 1, kind: 'pointerdown' }, SHELL_ORIGIN), true);

  let menusOpen = 0;

  // メニューが開いている間の Escape はシェルに渡す（パネルの中に焦点があると、キーはシェルの窓に届かない）。
  // 渡した Escape はパネルの中では止める（いちばん上のメニューだけを閉じ、パネル自身の Escape — 畳む・取り消す — は走らせない）。
  // 開いていない間は何も渡さない。変換中の Escape は IME のものなので奪わない。
  window.addEventListener('keydown', event => {
    if (menusOpen === 0 || event.key !== 'Escape' || event.isComposing) return;

    event.preventDefault();
    event.stopImmediatePropagation();
    parent.postMessage({ stk: 1, kind: 'escape' }, SHELL_ORIGIN);
  }, true);

  document.addEventListener('contextmenu', async event => {
    event.preventDefault();

    const named = nameContext(event);

    // シェルは押されるまで返事をしないので、これはメニューが閉じるまで待つ。
    menusOpen++;
    const result = await send('host', 'contextMenu', named.request).catch(err => {
      console.error('メニューを出せなかった', err);
      return null;
    }).finally(() => { menusOpen--; });

    // 試験の口: 返事が返った回数（閉じても押しても 1 つ増える。返らないまま残ると増えない）。
    window.__stkCtxSettled = (window.__stkCtxSettled ?? 0) + 1;

    if (!result || result.act !== 'replaceSelection' || !named.field) return;

    named.field.setRangeText(result.text ?? '', named.start, named.end, 'end');

    // ⚠ value を直に書き換えてもブラウザは input を出さない。出さないと、
    // 入力を見張っている側（設定の「適用」など）が変更に気付かない。
    named.field.dispatchEvent(new Event('input', { bubbles: true }));
  });

  window.spoitable = {
    /** 自分のモジュールが登録した口（C# の host.Ui.RegisterEndpoint）を呼ぶ。戻り値はモジュールが返した JSON をパースしたもの。 */
    call: (method, payload) => send('module', method, payload ?? {}),

    /** 画面の文言を引く。無いキーはそのまま返る（訳の抜けを隠さない）。 */
    t: translate,

    /**
     * 本体が持っている、どのモジュールも使う短い文言（「閉じる」「コピー」…）。C# の CommonPhrases と同じ名前で引く:
     * spoitable.phrases.ui.close。**無い名前は Error を投げる**（キーのまま画面に出さない）。whenReady を待ってから読む。
     * HTML に書くなら data-i18n="common.ui.close"。
     */
    phrases: phrasesUnder(PHRASES, 'spoitable.phrases'),

    /** 文言が届くまで待つ。**最初の描画はこれを待ってから**。 */
    whenReady: ready,

    /** data-i18n が付いた要素に文言を差し込む。あとから作った要素にも使える。 */
    localize,

    /** クリップボードへ書く。navigator.clipboard は独自スキームだと使えないことがあるのでホスト側で書く。手首のタイルの中では断られる。 */
    copyToClipboard: text => send('host', 'copyToClipboard', { text }),

    /**
     * 右クリックのメニューに材料を渡す。data-context="<key>" を付けた要素の上で
     * 押されたときに fn(element) が呼ばれ、返した object がそのままホストへ渡る。
     *
     * **押されたときだけ呼ばれる**ので、材料を先に属性へ書いておく必要はない。
     * 項目そのものを決めるのは C# 側（IContextMenuSource）。
     */
    onContext: (key, extract) => { contextExtractors.set(key, extract); },

    /**
     * モジュール（C# 側）からの push を受ける。fn(channel, payload) の形で呼ばれる。
     *
     * **呼ばれるのは文言が届いたあと**（それより前に来た push は、届くまで待ってから順に渡る）。
     * なので fn の中では t() も phrases もそのまま使える。
     *
     * これは best-effort。このパネルが開かれていなかった間の push は届いていない。
     * 読み込み時に spoitable.call() で現在値を取り直してから購読すること
     * （正となる状態は C# 側にあり、ここはその写しでしかない）。
     */
    onPush: fn => { subscribers.push(fn); },

    /**
     * シェルに「表示できる状態になった」と伝える。読み込み完了時に自動で送られる。
     * ホストの bind オブジェクトがこの origin から見えているかも一緒に報告する
     * （見えていると envelope を迂回できてしまうので、シェル側で観測できるようにしておく）。
     */
    ready: () => parent.postMessage({ stk: 1, kind: 'ready', cefsharp: typeof CefSharp }, SHELL_ORIGIN),
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => window.spoitable.ready());
  } else {
    window.spoitable.ready();
  }
})();
