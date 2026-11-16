using SpoitableToolKit.Abstractions.Localization;

namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// <b>値ではない設定の行。</b> 状態の札・説明・押すとその場で動くボタンを、設定画面の<b>宣言した場所</b>に出す。
/// <code>
/// public sealed class HdsSettings : ITogglableSettings
/// {
///     public bool Enabled { get; set; }
///
///     public int Port = 3476;
///
///     // 待ち受けポートのすぐ下に出る
///     [SpoitableSettingsLabelLocalize("hrs.settings.hds.firewall.label")]
///     public readonly SpoitableSettingsAction Firewall = new(dependsOn: [nameof(Port)]);
/// }
///
/// // OnInit で、ISpoitableModule.Config で返す本物の設定にだけ結ぶ
/// _config.Hds.Firewall.Bind(
///     describe: text =&gt; new SpoitableSettingsActionState(
///         Tone: SpoitableSettingsTone.Warn,
///         Status: text["hrs.settings.hds.firewall.missing"],
///         Detail: text.Format("hrs.panel.firewall.missing", _config.Hds.Port),
///         Button: text["hrs.panel.firewall.add"],
///         ButtonNote: text["hrs.panel.firewall.uac"],
///         Copy: null),
///     run: () =&gt; _firewall.AddRuleAsync());
///
/// // 状態が変わったら（どのスレッドからでもよい）
/// _config.Hds.Firewall.Changed();
/// </code>
/// <para>
/// <b>保存されない・「適用」にも乗らない。</b> ボタンは押した瞬間に <c>run</c> を走らせる（画面で溜めて「適用」でまとめて送る値とは別）。
/// config.json には書かれず、<see cref="Events.Config.IConfigEventListener{TConfig}"/> にも届かない。
/// </para>
/// <para>
/// <b>readonly のフィールドで持つ</b>（get だけのプロパティでもよい）。差し替えない — ホストが掴むのは設定を迎え入れたときのインスタンス。
/// </para>
/// <para>
/// <b>Bind は 1 回、OnInit の中で。</b> 2 回目は例外。結ばなかった行は画面に「使用できません」と出て、アプリのログに警告が 1 回残る。
/// 設定画面の下見と listener に見せる写しはホストが <c>new</c> で作り直したもので、結ばれていない。
/// ホストは写しの行でも本物の行を読むので、写しのことは考えなくてよい。
/// </para>
/// <para>
/// <b><c>describe</c> はホストが読みたいときに呼ぶ</b>: 設定画面を開いたとき・言語を変えたとき・<see cref="Changed"/> のあと・ボタンを押した直後と終わったあと。
/// <b>どのスレッドからも呼ばれる</b>ので、手元の値を読んで返すだけにする（待たない・重い処理をしない）。
/// 文言は渡された <c>text</c> で引く（言語を変えると、引き直した文言で描き直される）。
/// 例外はホストが握り潰し、その行に「状態の取得に失敗しました」を出す（設定画面ごと開かなくなるのを避ける）。
/// </para>
/// <para>
/// <b><c>run</c> はボタンを押したときにだけ、UI のスレッドの外で呼ぶ。</b> 返した <see cref="Task"/> が終わるまでボタンは押せず、その間の押しは捨てる。
/// 終わったら行を読み直す。例外はホストがログに残すだけなので、結果は <c>describe</c> の状態で見せる。
/// </para>
/// <para>
/// <b><c>dependsOn</c></b> は、この行の状態が前提にしている<b>同じクラスの設定</b>（C# の名前。<c>nameof</c> で書く）。
/// そのどれかを画面で変えて、まだ「適用」していない間は、ボタンの代わりに「変更を適用すると使えます」と出す
/// （上の例なら、ポートを打ち替えただけでは、状態はまだ前のポートの話をしている）。前提が無いなら <c>[]</c>。
/// 同じクラスの設定でない名前は、ホストがアプリのログにエラーを残して無視する。
/// </para>
/// </summary>
public sealed class SpoitableSettingsAction
{
    private readonly Lock _gate = new();

    private Func<ILocalizer, SpoitableSettingsActionState>? _describe;

    private Func<Task>? _run;

    private Action? _changed;

    /// <param name="dependsOn">この行の状態が前提にしている、同じクラスの設定の名前（<c>nameof</c>）。前提が無いなら <c>[]</c>。</param>
    public SpoitableSettingsAction(string[] dependsOn)
    {
        if (dependsOn is null)
            throw new ArgumentNullException(nameof(dependsOn),
                "Pass the names of the settings this row depends on, e.g. new(dependsOn: [nameof(Port)]), or new(dependsOn: []) when it depends on none.");

        if (dependsOn.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException(
                "dependsOn has an empty name. Name the settings of the same class with nameof, e.g. new(dependsOn: [nameof(Port)]).",
                nameof(dependsOn));

        DependsOn = [.. dependsOn];
    }

    /// <summary>この行の状態が前提にしている設定の名前（C# の名前）。</summary>
    public IReadOnlyList<string> DependsOn { get; }

    /// <summary>
    /// 行の中身（<paramref name="describe"/>）と、ボタンで走らせるもの（<paramref name="run"/>）を結ぶ。
    /// <b>OnInit の中で 1 回だけ。</b> 結んだあとに状態が変わったら <see cref="Changed"/> を呼ぶ。
    /// </summary>
    /// <param name="describe">いまの姿を返す。どのスレッドからも呼ばれるので、手元の値を読んで返すだけにする。文言は渡された <see cref="ILocalizer"/> で引く。</param>
    /// <param name="run">ボタンを押したときに走らせる。UI のスレッドの外で呼ばれ、終わるまで次の押しは捨てられる。ボタンを出さない行でも渡す（<c>() =&gt; Task.CompletedTask</c>）。</param>
    /// <exception cref="InvalidOperationException">もう結んである。</exception>
    public void Bind(Func<ILocalizer, SpoitableSettingsActionState> describe, Func<Task> run)
    {
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(run);

        Action? changed;
        lock (_gate)
        {
            if (_describe is not null)
                throw new InvalidOperationException(
                    "This SpoitableSettingsAction is already bound. Call Bind once, in OnInit, on the settings the module returns from ISpoitableModule.Config; " +
                    "to show a new state afterwards, call Changed().");

            _describe = describe;
            _run = run;
            changed = _changed;
        }

        // 結ぶ前に描かれていた画面（「使用できません」）を、結んだ姿に追いつかせる。
        changed?.Invoke();
    }

    /// <summary>
    /// 状態が変わったので、設定画面にこの行を読み直させる（<c>describe</c> がもう一度呼ばれる）。
    /// <b>どのスレッドから呼んでもよく、待たない。</b> 画面の溜めてある変更は消えない（描き直すのはこの行だけ）。
    /// 結ぶ前に呼んでも何も起きない。
    /// </summary>
    public void Changed()
    {
        Action? changed;
        lock (_gate)
        {
            if (_describe is null) return;
            changed = _changed;
        }

        changed?.Invoke();
    }

    /// <summary>結んであるか。ホストが読む。</summary>
    internal bool IsBound
    {
        get
        {
            lock (_gate) return _describe is not null;
        }
    }

    /// <summary>いまの姿。結んでいなければ <c>null</c>。モジュールの例外はそのまま上がる（握り潰すのはホスト）。</summary>
    internal SpoitableSettingsActionState? Describe(ILocalizer text)
    {
        Func<ILocalizer, SpoitableSettingsActionState>? describe;
        lock (_gate) describe = _describe;

        return describe?.Invoke(text);
    }

    /// <summary>ボタンの分を走らせる。結んでいなければ <c>null</c>。</summary>
    internal Task? Run()
    {
        Func<Task>? run;
        lock (_gate) run = _run;

        return run?.Invoke();
    }

    /// <summary>ホストが「変わった」を受け取る口。1 つだけ（後から張った方が勝つ）。<c>null</c> で外す。</summary>
    internal void Listen(Action? changed)
    {
        lock (_gate) _changed = changed;
    }
}
