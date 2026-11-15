using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Modules;

namespace SpoitableToolKit.Host.ExternalModules;

/// <summary>
/// モジュール間でサービスをやり取りする場所。<b>名前で登録し、名前で取る</b>（<see cref="IExternalModuleRegistry"/> に決まり）。
/// <para>
/// 参照は必ずラッパー（<see cref="IExternalModuleRef{T}"/>）で返す。提供元のモジュールが落ちたときに
/// 消費側が死んだオブジェクトを掴み続けないため。v1 はホットリロードしないので
/// <c>IsAvailable</c> はほぼ常に true だが、ここを今 API に入れておけば
/// あとからホットリロードを足しても呼び出し側が壊れない。
/// </para>
/// <para>
/// <b>失敗の文は、読んだ側（モジュールを書くのは AI）がそのまま直せる形にする</b>:
/// 正しい名前の形・登録されている名前の一覧・型が食い違うときは、どの dll から来た型か。試験で文を固定している。
/// </para>
/// </summary>
internal sealed class ExternalModuleRegistry(ILoggerFactory loggerFactory) : IExternalModuleRegistry
{
    /// <summary>
    /// 名前のうち、Id のあとの部分に使える形（<c>api</c>・<c>api-v2</c> など）。<b><c>.</c> は入れない</b> —
    /// Id には <c>.</c> が入りうるので、入れると <c>hrs</c> の <c>hrs.extra.api</c> と <c>hrs.extra</c> の <c>hrs.extra.api</c> が同じ名前になる。
    /// 入れなければ、名前の持ち主は「最後の <c>.</c> の手前」で 1 つに決まる。
    /// </summary>
    private static readonly Regex NameRest = new(@"^[a-z0-9]([a-z0-9_-]*[a-z0-9])?\z", RegexOptions.CultureInvariant);

    private readonly ILogger<ExternalModuleRegistry> _logger = loggerFactory.CreateLogger<ExternalModuleRegistry>();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public required ISpoitableModule Owner { get; init; }
        public required string Name { get; init; }
        public required Type Contract { get; init; }
        public required object Implementation { get; init; }

        /// <summary>外すのは鍵の中、読むのは鍵の外（参照を持っている側）なので、すぐ見えるように。</summary>
        public volatile bool IsAvailable = true;
    }

    /// <summary>登録の実体を指し続けるハンドル。取り消されると <c>Instance</c> が null になる。</summary>
    private sealed class Ref<T>(Entry entry) : IExternalModuleRef<T> where T : class
    {
        public T? Instance => entry.IsAvailable ? (T)entry.Implementation : null;

        public bool IsAvailable => entry.IsAvailable;
    }

    public void Register<T>(ISpoitableModule owner, string name, T implementation) where T : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(implementation);

        var prefix = owner.Id + ".";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !NameRest.IsMatch(name[prefix.Length..]))
        {
            throw new ArgumentException(
                $"'{name}' is not a module name the module '{owner.Id}' can register. " +
                $"A module name is the registering module's Id, one dot, and what the API is in a-z, 0-9, '_' or '-' (no further dots) — for example '{prefix}api'. " +
                $"Keep it as a constant on the contract (const string ModuleIdentity = \"{prefix}api\";) so both sides pass the same text.",
                nameof(name));
        }

        // 型引数を書き忘れると、実体の型（モジュールのクラス）で登録される。使う側はその型を参照できない（別のコンテキストの型）。
        if (!typeof(T).IsInterface)
        {
            throw new ArgumentException(
                $"'{name}' was registered as {typeof(T).FullName}, which is not an interface. " +
                "Pass the contract interface as the type argument — Register<IYourContract>(this, name, implementation) — " +
                "and keep that interface in a contract dll that this module brings in its shared/ folder, so that other modules can reference it " +
                "(with the SDK: reference the contract project with SpoitableContract=\"Provide\").",
                nameof(implementation));
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(name, out var existing))
            {
                throw new InvalidOperationException(
                    $"The module name '{name}' is already registered (by '{existing.Owner.Id}' as {existing.Contract.FullName}). " +
                    $"A name can be registered once; give the second API its own name, for example '{name}2'.");
            }

            _entries[name] = new Entry { Owner = owner, Name = name, Contract = typeof(T), Implementation = implementation };
        }

        _logger.LogInformation("{Module} provides {Name} ({Contract})", owner.Id, name, typeof(T).FullName);
    }

    public IExternalModuleRef<T> GetRequired<T>(string name) where T : class =>
        GetOptional<T>(name) ?? throw new InvalidOperationException(Missing<T>(name));

    public IExternalModuleRef<T>? GetOptional<T>(string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_gate)
        {
            if (!_entries.TryGetValue(name, out var entry)) return null;
            if (entry.Implementation is T) return new Ref<T>(entry);

            throw new InvalidOperationException(Mismatch<T>(entry));
        }
    }

    public IReadOnlyDictionary<string, IExternalModuleRef<T>> GetAll<T>() where T : class
    {
        lock (_gate)
        {
            // 同じ名前の型で登録されているのに使えないものがあれば、黙って抜かさない（契約の dll が二重。抜かすと「誰も登録していない」に見える）。
            if (_entries.Values.FirstOrDefault(e => e.Implementation is not T && e.Contract.FullName == typeof(T).FullName) is { } copy)
                throw new InvalidOperationException(Mismatch<T>(copy));

            return _entries.Values
                .Where(entry => entry.Implementation is T)
                .ToDictionary(entry => entry.Name, IExternalModuleRef<T> (entry) => new Ref<T>(entry), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// モジュールが落ちるときに、そのモジュールが提供していたものを全部無効にする。
    /// 消費側が既に持っている <see cref="IExternalModuleRef{T}"/> はここで <c>Instance</c> が null になる。
    /// </summary>
    public void RevokeAll(ISpoitableModule owner)
    {
        List<string> revoked = [];
        lock (_gate)
        {
            foreach (var (name, entry) in _entries)
            {
                if (!ReferenceEquals(entry.Owner, owner)) continue;
                entry.IsAvailable = false;
                revoked.Add(name);
            }

            foreach (var name in revoked) _entries.Remove(name);
        }

        if (revoked.Count > 0)
            _logger.LogInformation("{Module} revoked {Count} module(s): {Names}", owner.Id, revoked.Count, string.Join(", ", revoked));
    }

    /// <summary>
    /// その名前の登録が無いときの文。<b>何が登録されているか</b>を並べる（名前の打ち間違いか、渡す側が動いていないのかが分かるように）。
    /// </summary>
    private string Missing<T>(string name) where T : class
    {
        string[] sameType, all;
        lock (_gate)
        {
            sameType = [.. _entries.Values.Where(e => e.Implementation is T).Select(e => e.Name).Order(StringComparer.Ordinal)];
            all = [.. _entries.Keys.Order(StringComparer.Ordinal)];
        }

        // 名前の持ち主は、最後の . の手前（Id には . が入りうる。そのあとには入らない）。形が違えば、まず形を言う。
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || !NameRest.IsMatch(name[(dot + 1)..]))
        {
            return $"'{name}' is not a module name, so nothing can be registered under it. " +
                   "A module name is the providing module's Id, one dot, and what the API is (for example 'hrs.api'). " +
                   "Pass the contract's ModuleIdentity constant instead of typing the name.";
        }

        var provider = name[..dot];
        var known = sameType.Length > 0
            ? $"Registered as {typeof(T).FullName}: {string.Join(", ", sameType)}."
            : all.Length > 0
                ? $"Nothing is registered as {typeof(T).FullName}. Registered names: {string.Join(", ", all)}."
                : "No module has registered any module.";

        return $"No module is registered as '{name}'. {known} " +
               "Either the name differs from the one the provider registers (pass the contract's ModuleIdentity constant on both sides), " +
               $"or the module '{provider}' is not installed, is turned off, or failed to start (see the Modules page). " +
               "Modules are registered in OnPostInit, so ask for them in OnAllModulesLoaded or later. " +
               "If you can work without it, use GetOptional and handle null.";
    }

    /// <summary>
    /// 名前はあるのに型が合わないときの文。同じ名前の型が 2 つあるのか（契約の dll の写し・別の版）、そもそも別の型なのかを言い分ける。
    /// </summary>
    private static string Mismatch<T>(Entry entry) where T : class
    {
        var asked = typeof(T);
        var registered = entry.Contract;

        if (string.Equals(asked.FullName, registered.FullName, StringComparison.Ordinal))
        {
            return $"The module '{entry.Name}' is registered as {registered.FullName} from {Describe(registered)}, " +
                   $"but it was asked for as {asked.FullName} from {Describe(asked)}. " +
                   "They have the same name but are two different types: the contract assembly was loaded twice (a copy, or another version). " +
                   $"Keep the contract dll only in the provider's shared/ folder (modules/{entry.Owner.Id}/shared/), " +
                   "and reference it from the other module without copying it to that module's output " +
                   "(with the SDK: SpoitableContract=\"Provide\" on the provider's reference to the contract, SpoitableContract=\"Use\" on the other module's).";
        }

        return $"The module '{entry.Name}' is registered as {registered.FullName} (by '{entry.Owner.Id}'), which is not a {asked.FullName}. " +
               $"Ask for it with the contract it was registered with: GetRequired<{registered.Name}>(\"{entry.Name}\").";
    }

    /// <summary>型がどの dll から来たか（名前・版・あれば場所）。</summary>
    private static string Describe(Type type)
    {
        var assembly = type.Assembly;
        var name = assembly.GetName();
        var location = assembly.Location is { Length: > 0 } path ? path : "loaded from memory";
        return $"{name.Name} {name.Version} ({location})";
    }
}
