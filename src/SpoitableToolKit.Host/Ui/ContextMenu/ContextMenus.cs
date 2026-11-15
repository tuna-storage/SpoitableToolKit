using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Clipboard;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Ui;

namespace SpoitableToolKit.Host.Ui.ContextMenu;

/// <summary>
/// コンテキストメニューの中身を決めて、押されたものを実行するところ。画面を知らない（DOM も CEF も触らない）ので、窓を立てずに単体試験が書ける。
/// 押した瞬間に呼ばれる — メニューはいま出来ることを見せるもので、先に配ると出来ないことが並ぶ。
/// 実行は全部ここへ返る。切り取りと貼り付けも「この文字で選択範囲を置き換えろ」という指示を返す形にして（<see cref="ContextMenuResult.Act"/>）、クリップボードを読む口をページへ開けずに済ませる — 中身が渡るのは、利用者がそのページで貼り付けを押した瞬間だけ。
/// </summary>
internal sealed class ContextMenus(ContextMenus.SourceLookup sources, IClipboard clipboard, ILogger logger)
{
    /// <summary>項目を持っているモジュールを引く。<c>ModuleManager</c> を直に受けないのは、ここが読み込みの事情を知らなくてよいから（試験もモジュールを一式立ち上げずに書ける）。</summary>
    public delegate IContextMenuSource? SourceLookup(string moduleId);

    /// <summary>ホストが持つ項目の印。モジュールの項目とぶつからないよう頭に持ち主を付けるので、画面側は id を見分ける必要がない。</summary>
    private const string HostOwner = "host";

    /// <summary>持ち主と印の区切り。モジュール Id にも項目の印にも出てこない文字を選ぶ。</summary>
    private const char OwnerSeparator = ' ';

    /// <summary>この場所に出す項目。空なら画面はメニューを開かない。</summary>
    /// <param name="owner">名乗りが来た iframe の持ち主。シェル自身からなら <c>null</c>。発信元 origin から決まったもので、モジュールが自分で名乗ったものではない。</param>
    public IReadOnlyList<ContextMenuEntry> Build(ContextMenuTarget target, string? owner)
    {
        List<ContextMenuEntry> items = [.. Editing(target), .. ForKey(target)];

        var extra = FromModule(target, owner);

        // 区切りは前と後ろの両方に中身があるときだけ。先頭や末尾に線が残ると、消えた項目があるように見える。
        if (items.Count > 0 && extra.Count > 0) items.Add(new ContextMenuEntry { Separator = true });

        items.AddRange(extra);
        return items;
    }

    public async Task<ContextMenuResult> InvokeAsync(string id, ContextMenuTarget target, string? owner)
    {
        var (itemOwner, itemId) = Split(id);

        if (itemOwner == HostOwner) return InvokeHost(itemId, target);

        // 持ち主は名乗りのときと同じでなければならない。一覧を出したのとは別のモジュールの項目を、あとから id だけ差し替えて実行させない。
        if (itemOwner != owner)
        {
            logger.LogWarning("Refused a context menu item {Id} claimed for {Owner}", itemId, itemOwner);
            return ContextMenuResult.Failed;
        }

        if (Source(owner) is not { } source) return ContextMenuResult.Failed;

        try
        {
            await source.InvokeContextMenuAsync(itemId, target).ConfigureAwait(false);
            return ContextMenuResult.Done;
        }
        catch (Exception ex)
        {
            // 1 つのモジュールの失敗で右クリックそのものを壊さない（CallModule と同じ扱い）。
            logger.LogWarning(ex, "A context menu item of {Module} threw", owner);
            return ContextMenuResult.Failed;
        }
    }

    private ContextMenuResult InvokeHost(string id, ContextMenuTarget target) => id switch
    {
        "copy" => Written(clipboard.SetText(target.SelectionText)),

        // 切り取りはクリップボードへ書いてから消す。書けなければ消さない — 消してから書けなかったら、選んでいた文字がどこにも残らない。
        "cut" => clipboard.SetText(target.SelectionText)
            ? ContextMenuResult.Replace(string.Empty)
            : ContextMenuResult.Failed,

        // 貼り付けはここで読む（ページにクリップボードを読む口を開けずに済ませたい）。空なら何もしない（空で上書きすると、選んでいた文字がただ消える）。
        "paste" => clipboard.GetText() is { Length: > 0 } text
            ? ContextMenuResult.Replace(text)
            : NothingToPaste(),

        "copyRow" => Written(clipboard.SetText(target.Get("text") ?? string.Empty)),
        "copyDetail" => Written(clipboard.SetText(target.Get("detail") ?? string.Empty)),
        _ => ContextMenuResult.Failed,
    };

    private static ContextMenuResult Written(bool ok) => ok ? ContextMenuResult.Done : ContextMenuResult.Failed;

    /// <summary>貼り付けたのに字が取れなかった。何もしないが、跡は残す（読めなかったのか空だったのかは HostClipboard が出す）。</summary>
    private ContextMenuResult NothingToPaste()
    {
        logger.LogInformation("Paste found no text on the clipboard");
        return ContextMenuResult.Done;
    }

    /// <summary>既定のメニューが担っていた分。入力欄と、文字を選んだところにだけ出す。<c>Ctrl+C</c> / <c>Ctrl+V</c> は CEF のキーボード側で生きているが、右クリックで出るのが当たり前の操作なので出す。</summary>
    private static IEnumerable<ContextMenuEntry> Editing(ContextMenuTarget target)
    {
        var selected = !string.IsNullOrEmpty(target.SelectionText);

        if (target.Editable)
        {
            // 伏せ欄（type=password）ではコピーも切り取りも出さない（出せば画面に出さない意味が無くなる。Chromium の既定も同じ）。
            if (!target.Secret)
            {
                yield return new ContextMenuEntry { Id = Owned(HostOwner, "cut"), LabelKey = CommonPhrases.Ui.Cut, Enabled = selected };
                yield return new ContextMenuEntry { Id = Owned(HostOwner, "copy"), LabelKey = CommonPhrases.Ui.Copy, Enabled = selected };
            }

            // 貼り付けだけは伏せ欄でも出す（入れる側は守る対象ではない）。
            // クリップボードが空かどうかは見ない。読むのは押されたときだけにしたい（右クリックのたびに他所のクリップボードを覗きに行く作りにはしない）。空で押されたら何も起きない。
            yield return new ContextMenuEntry { Id = Owned(HostOwner, "paste"), LabelKey = CommonPhrases.Ui.Paste };

            yield break;
        }

        // 入力欄の外。選んでいないなら出さない（灰色のコピーだけが出ても意味が無い）。
        if (selected)
            yield return new ContextMenuEntry { Id = Owned(HostOwner, "copy"), LabelKey = CommonPhrases.Ui.Copy };
    }

    private static IEnumerable<ContextMenuEntry> ForKey(ContextMenuTarget target)
    {
        if (target.Key != "log-row") yield break;

        if (target.Has("text"))
            yield return new ContextMenuEntry { Id = Owned(HostOwner, "copyRow"), LabelKey = "host.menu.copyRow" };

        // 明細はシートと同じもの。画面は行を開いていなくても渡す。渡されなかったとき（明細を持たない面）は、灰色に出すより出さない方が静か。
        if (target.Has("detail"))
            yield return new ContextMenuEntry { Id = Owned(HostOwner, "copyDetail"), LabelKey = "host.menu.copyDetail" };
    }

    private IReadOnlyList<ContextMenuEntry> FromModule(ContextMenuTarget target, string? owner)
    {
        if (owner is null || Source(owner) is not { } source) return [];

        try
        {
            return
            [
                .. source.GetContextMenu(target)
                    .Select(item => new ContextMenuEntry
                    {
                        Id = item.Separator ? string.Empty : Owned(owner, item.Id),
                        LabelKey = item.LabelKey,
                        Enabled = item.Enabled,
                        Separator = item.Separator,
                    }),
            ];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The context menu of {Module} threw", owner);
            return [];
        }
    }

    private IContextMenuSource? Source(string owner) => sources(owner);

    private static string Owned(string owner, string id) => $"{owner}{OwnerSeparator}{id}";

    private static (string Owner, string Id) Split(string id)
    {
        var at = id.IndexOf(OwnerSeparator);

        return at < 0 ? (string.Empty, id) : (id[..at], id[(at + 1)..]);
    }

}
