# nightly の版: YYYY.M.D-<run_number>.g<hash7>（例 2026.11.15-42.gabc1234）。
#
# Velopack は版を SemVer 2 で読むので:
# - 日付は 0 で埋めない（数の部分は 0 で始められない）
# - 「-」の後ろは部分ごとに比べる。run_number を先に置いて、同じ日の 2 回目の push が後ろに並ぶようにする
# - hash には g を付ける（数字だけで 0 始まりの hash は数の部分として通らない）
# 日付は日本の時刻（UTC+9。夏時間は無い）。
param(
    [Parameter(Mandatory)] [int] $RunNumber,
    [Parameter(Mandatory)] [string] $Sha,
    [DateTimeOffset] $At = [DateTimeOffset]::UtcNow
)

$ErrorActionPreference = 'Stop'

if ($RunNumber -lt 1) { throw "RunNumber must be 1 or more, not $RunNumber" }
if ($Sha -notmatch '^[0-9a-fA-F]{7,40}$') { throw "Sha must be a commit hash, not '$Sha'" }

$day = $At.ToOffset([TimeSpan]::FromHours(9))
'{0}.{1}.{2}-{3}.g{4}' -f $day.Year, $day.Month, $day.Day, $RunNumber, $Sha.Substring(0, 7).ToLowerInvariant()
