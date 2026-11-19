// 조회 소요 시간 측정과 조회 이력(checks.jsonl). 화면과 떨어진 부분이라 tests\CheckHistoryTest.cs 가 따로 시험한다.
// 컴파일러는 .NET Framework 의 csc(C# 5) — $"" · ?. · => 멤버 · out var 를 쓰지 말 것.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// checks.jsonl 한 줄. JavaScriptSerializer 로 읽고 쓰므로 public 필드로 둔다.
public class CheckRecord
{
    public string Time = "";   // yyyy-MM-dd HH:mm:ss (조회를 마친 시각)
    public string Route = "", Range = "", Result = "";
    public long TotalMs, NavMs, WaitMs, ScrollMs;
    public int Attempts, Seats;
    public bool Ok;            // 좌석 판단까지 마친 정상 조회
    public bool Cancelled;     // 노선을 끄거나 지워서 중간에 그만둔 조회
}

// 한 노선 조회에 걸린 시간. 다시 열기를 하면 단계별 시간은 시도마다 더해진다.
internal sealed class CheckTiming
{
    readonly Stopwatch total = Stopwatch.StartNew();
    internal long NavMs, WaitMs, ScrollMs;
    internal int Attempts;

    internal long TotalMs { get { return total.ElapsedMilliseconds; } }
    internal void Stop() { total.Stop(); }

    // 조회 기록 한 줄 끝에 붙는 글. 예: "2.1초" · "7.4초(3회 시도)"
    internal string Label
    {
        get { return CheckHistory.Seconds(TotalMs) + (Attempts > 1 ? "(" + Attempts + "회 시도)" : ""); }
    }
}

internal static class CheckHistory
{
    internal const long MaxFileBytes = 1024 * 1024;

    internal static string Seconds(long ms)
    {
        return (Math.Max(0, ms) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "초";
    }

    // 표의 '단계' 열. 예: "이동 0.9 · 대기 1.0 · 스크롤 0.0"
    internal static string Stages(CheckRecord r)
    {
        Func<long, string> s = ms => (Math.Max(0, ms) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
        return "이동 " + s(r.NavMs) + " · 대기 " + s(r.WaitMs) + " · 스크롤 " + s(r.ScrollMs);
    }

    internal static CheckRecord Create(DateTime finished, string route, string range, CheckTiming timing,
        string result, bool ok, bool cancelled, int seats)
    {
        return new CheckRecord {
            Time = finished.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Route = route ?? "", Range = range ?? "", Result = result ?? "",
            TotalMs = timing.TotalMs, NavMs = timing.NavMs, WaitMs = timing.WaitMs, ScrollMs = timing.ScrollMs,
            Attempts = timing.Attempts, Seats = seats, Ok = ok, Cancelled = cancelled
        };
    }

    // log.txt → log.old.txt, checks.jsonl → checks.old.jsonl. 바로 앞 묶음 하나만 남긴다.
    internal static string OldPath(string path)
    {
        return Path.Combine(Path.GetDirectoryName(path),
            Path.GetFileNameWithoutExtension(path) + ".old" + Path.GetExtension(path));
    }

    internal static void RotateIfLarge(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= maxBytes) return;
            var old = OldPath(path);
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
        }
        catch { }
    }

    internal static void Append(string path, CheckRecord record, JavaScriptSerializer json)
    {
        RotateIfLarge(path, MaxFileBytes);
        try { File.AppendAllText(path, json.Serialize(record) + "\n", new UTF8Encoding(false)); } catch { }
    }

    // 오래된 순서로 최근 max 개. 깨진 줄은 건너뛴다.
    internal static List<CheckRecord> ReadTail(string path, int max, JavaScriptSerializer json)
    {
        var lines = new List<string>();
        foreach (var file in new[] { OldPath(path), path })
        {
            try { if (File.Exists(file)) lines.AddRange(File.ReadAllLines(file, Encoding.UTF8)); } catch { }
        }
        var result = new List<CheckRecord>();
        foreach (var line in lines.Skip(Math.Max(0, lines.Count - max)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = json.Deserialize<CheckRecord>(line);
                if (record != null) result.Add(record);
            }
            catch { }
        }
        return result;
    }

    // 파일 끝에서 maxChars 글자 안쪽의 온전한 줄만 돌려준다(조회 기록 원문 창을 다시 채울 때).
    internal static string ReadTextTail(string path, int maxChars)
    {
        string text;
        try { if (!File.Exists(path)) return ""; text = File.ReadAllText(path, Encoding.UTF8); }
        catch { return ""; }
        if (text.Length <= maxChars) return text;
        int start = text.Length - maxChars;
        if (text[start - 1] == '\n') return text.Substring(start);
        int cut = text.IndexOf('\n', start);
        return cut >= 0 ? text.Substring(cut + 1) : text.Substring(start);
    }

    // 조회 기록 화면 위쪽 요약. 취소한 조회는 빼고 최근 window 회를 본다.
    internal static string Summary(IList<CheckRecord> records, int window)
    {
        var recent = records.Where(r => !r.Cancelled).Reverse().Take(window).ToList();
        if (recent.Count == 0) return "아직 조회 기록이 없습니다. 조회가 끝날 때마다 걸린 시간이 여기에 쌓입니다.";
        var ms = recent.Select(r => r.TotalMs).OrderBy(v => v).ToList();
        long median = ms.Count % 2 == 1 ? ms[ms.Count / 2] : (ms[ms.Count / 2 - 1] + ms[ms.Count / 2]) / 2;
        int failed = recent.Count(r => !r.Ok);
        return "최근 " + recent.Count + "회 조회 · 평균 " + Seconds((long)ms.Average()) + " · 중간값 " + Seconds(median) +
            " · 가장 느림 " + Seconds(ms[ms.Count - 1]) + (failed > 0 ? " · 확인 못 함 " + failed + "회" : "");
    }
}
