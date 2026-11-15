using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Store;

/// <summary>
/// 保存した行から、生きているイベントと同じ型を作り直す。
/// 無いと、過去の行だけ本文がそのまま出る（<c>[API] Requesting Get …</c> のような生テキスト）のに対し、
/// いま流れている行は種別ごとに整えた見た目になる、という二重の見せ方になる。
/// </summary>
internal static class StoredEventCodec
{
    /// <summary>種別名 → 型。アセンブリを走査して作る — 手で並べると、種別を足したときに更新し忘れて「新しい種別の過去の行だけ本文がそのまま出る」になる。</summary>
    private static readonly Dictionary<string, Type> Kinds =
        typeof(VrcLogEventParams).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(VrcLogEventParams).IsAssignableFrom(type))
            .ToDictionary(type => type.Name, StringComparer.Ordinal);

    /// <summary>
    /// 読み戻した行を型へ戻す。戻せなければ null で、例外は投げない。
    /// DB は実行ファイルより長生きする。種別の名前を変えれば古い行が読めなくなるし、
    /// 逆に新しい種別を書いた DB を古いビルドで開くこともある。
    /// どちらも「その行だけ素の見た目になる」で済ませ、画面ごと落とさない。
    /// </summary>
    public static VrcLogEventParams? Rebuild(StoredLogEvent row, ILogger logger)
    {
        if (!Kinds.TryGetValue(row.Kind, out var type)) return null;

        try
        {
            if (JsonNode.Parse(row.Payload) is not JsonObject payload) return null;

            // 元の行は payload から落としてある（本文は message 列に持っているので重複させない）。
            // 型のほうは required なので、列に残っている分から組み立てて戻す。
            // レベルとタグはイベントには保存していない。無いものを埋めずに素のまま渡す。
            payload["Line"] = new JsonObject
            {
                ["Timestamp"] = row.Timestamp.ToString("O"),
                ["Level"] = (int)VrcLogLevel.Unknown,
                ["Message"] = row.Message,
                ["SourceFile"] = row.SourceFile ?? string.Empty,
            };

            // 既定の設定で読む。書くときの設定（PayloadOptions）は Line を契約から
            // 外しているので、それで読むと今入れた Line が捨てられ、Timestamp が落ちる。
            return payload.Deserialize(type) as VrcLogEventParams;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not rebuild the payload of {Kind} (id={Id})", row.Kind, row.Id);
            return null;
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Could not rebuild the payload of {Kind} (id={Id})", row.Kind, row.Id);
            return null;
        }
    }
}
