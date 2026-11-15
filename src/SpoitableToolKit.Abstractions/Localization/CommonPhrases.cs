namespace SpoitableToolKit.Abstractions.Localization;

/// <summary>
/// どのモジュールでも使う短い文言（「閉じる」「コピー」「戻る」…）の<b>キー</b>。訳は本体が持っている（ja / en）。
/// <code>
/// host.Text[CommonPhrases.Ui.Close]                    // C# から
/// &lt;button data-i18n="common.ui.close"&gt;&lt;/button&gt;       // パネルの HTML から
/// spoitable.t('common.ui.close')                       // パネルの JS から
/// </code>
/// <para>
/// <b>同じ文言をモジュールの辞書に持たない。</b> モジュールの辞書（<c>locales/&lt;言語&gt;.json</c>）に書けるのは
/// 自分の Id で始まるキーだけで、<c>common.…</c> や <c>host.…</c> を書いても本体は読み込まない（ログに警告が出る）。
/// ここに無い文言は、自分のキー（<c>&lt;Id&gt;.…</c>）で持つ。
/// </para>
/// </summary>
public static class CommonPhrases
{
    /// <summary>ボタンやメニューに置く短い言葉。定数の値がキー（<c>common.ui.…</c>）。</summary>
    public static class Ui
    {
        /// <summary>戻る / Back</summary>
        public const string Back = "common.ui.back";

        /// <summary>進む / Next</summary>
        public const string Next = "common.ui.next";

        /// <summary>再読み込み / Reload</summary>
        public const string Reload = "common.ui.reload";

        /// <summary>開く / Open</summary>
        public const string Open = "common.ui.open";

        /// <summary>閉じる / Close</summary>
        public const string Close = "common.ui.close";

        /// <summary>OK / OK</summary>
        public const string Ok = "common.ui.ok";

        /// <summary>はい / Yes</summary>
        public const string Yes = "common.ui.yes";

        /// <summary>いいえ / No</summary>
        public const string No = "common.ui.no";

        /// <summary>やめる / Cancel</summary>
        public const string Cancel = "common.ui.cancel";

        /// <summary>適用 / Apply</summary>
        public const string Apply = "common.ui.apply";

        /// <summary>保存 / Save</summary>
        public const string Save = "common.ui.save";

        /// <summary>完了 / Done</summary>
        public const string Done = "common.ui.done";

        /// <summary>追加 / Add</summary>
        public const string Add = "common.ui.add";

        /// <summary>編集 / Edit</summary>
        public const string Edit = "common.ui.edit";

        /// <summary>外す / Remove</summary>
        public const string Remove = "common.ui.remove";

        /// <summary>削除 / Delete</summary>
        public const string Delete = "common.ui.delete";

        /// <summary>元に戻す / Revert</summary>
        public const string Revert = "common.ui.revert";

        /// <summary>やり直す / Retry</summary>
        public const string Retry = "common.ui.retry";

        /// <summary>検索 / Search</summary>
        public const string Search = "common.ui.search";

        /// <summary>コピー / Copy</summary>
        public const string Copy = "common.ui.copy";

        /// <summary>コピーした / Copied</summary>
        public const string Copied = "common.ui.copied";

        /// <summary>切り取り / Cut</summary>
        public const string Cut = "common.ui.cut";

        /// <summary>貼り付け / Paste</summary>
        public const string Paste = "common.ui.paste";

        /// <summary>読み込み中… / Loading…</summary>
        public const string Loading = "common.ui.loading";

        /// <summary>失敗 / Failed</summary>
        public const string Failed = "common.ui.failed";
    }
}
