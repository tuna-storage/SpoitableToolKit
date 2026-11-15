using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.LogView;

/// <summary>
/// 起動の流れの中の繋ぎ（記録を開く → 設定を読んでから読み戻す → 起動時に古い行を消す）を、専用のアプリで本当に起動し直して通す。
/// </summary>
public sealed class AppLogRestartTests(AppFixture app) : IClassFixture<AppFixture>
{
    private string Database => Path.Combine(app.DataDirectory, "applog.db");

    [Fact]
    public async Task TheAppLaneComesBackAfterARestart()
    {
        var before = await app.Page.EvaluateAsync<long[]>(
            "async () => (await bridge.getStatus()).appLogs.map(a => a.seq)");
        Assert.True(before.Length >= 2, "1 回目の起動の行が台帳に無い");

        // 落とす前に、記録に入ったのを確かめる。落とし方は kill なので、キューに居る分は消える。
        await WaitForAsync(() => Scalar("SELECT COALESCE(MAX(seq), 0) FROM app_log") >= before.Max(),
            "1 回目の起動の行が記録に入らなかった");

        var tooOld = before.Min();
        Execute($"UPDATE app_log SET at = {DateTimeOffset.UtcNow.AddDays(-40).ToUnixTimeMilliseconds()} WHERE seq = {tooOld}");

        await app.RestartAsync();

        await app.Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await app.Page.Locator("#logTabs .spoitable-tab[data-source='app']").ClickAsync();
        await Assertions.Expect(app.Page.Locator("#rowsApp .run-mark")).ToHaveCountAsync(1, new() { Timeout = 10_000 });

        using var shape = JsonDocument.Parse(await app.Page.EvaluateAsync<string>("""
            () => {
              const kids = [...el.rowsApp.children];
              const at = kids.findIndex(c => c.classList.contains('run-mark'));
              const seqs = list => list.filter(c => c.classList.contains('row')).map(c => Number(c.dataset.seq));
              return JSON.stringify({ above: seqs(kids.slice(0, at)), below: seqs(kids.slice(at + 1)), mark: kids[at].textContent });
            }
            """));

        long[] Seqs(string name) => [.. shape.RootElement.GetProperty(name).EnumerateArray().Select(seq => seq.GetInt64())];
        var above = Seqs("above");
        var below = Seqs("below");

        // 印の下に前の起動の行（落とす直前に出た分が足されていてもよい）、上にこの起動の行。
        Assert.All(before.Where(seq => seq != tooOld), seq => Assert.Contains(seq, below));
        Assert.NotEmpty(above);

        long[] all = [.. above, .. below];
        Assert.True(all.Zip(all.Skip(1)).All(pair => pair.First > pair.Second), "起動をまたいで番号が重なっている: " + string.Join(",", all));

        Assert.StartsWith("起動 ", shape.RootElement.GetProperty("mark").GetString());

        // 消すのはキューの先なので待つ。
        Assert.DoesNotContain(tooOld, below);
        await WaitForAsync(() => Scalar($"SELECT COUNT(*) FROM app_log WHERE seq = {tooOld}") == 0,
            "残す期間より前の行が起動しても消えなかった");
    }

    /// <summary>繋ぎを溜めない（持ったままだと、アプリを落としたあとにファイルが消せない）。</summary>
    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Database,

            // 作らない。無いなら「アプリが作っていない」ので、空の DB を置くより落ちた方がいい。
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private long Scalar(string sql)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Execute(string sql)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition()) return;
            await Task.Delay(100);
        }

        Assert.Fail(because);
    }
}
