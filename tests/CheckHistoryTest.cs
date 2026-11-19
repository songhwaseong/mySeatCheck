// 조회 소요 시간 기록(checks.jsonl) 시험.
// csc /nologo /r:System.Web.Extensions.dll /out:%TEMP%\CheckHistoryTest.exe tests\CheckHistoryTest.cs src\CheckHistory.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

class CheckHistoryTest
{
    static void Assert(bool value, string what)
    {
        if (!value) throw new Exception("Check history regression: " + what);
    }

    static CheckRecord Record(long ms, bool ok, bool cancelled)
    {
        return new CheckRecord { Time = "2026-11-19 19:00:00", Route = "CJU→GMP 11/22", TotalMs = ms, Ok = ok, Cancelled = cancelled };
    }

    static void Main()
    {
        var json = new JavaScriptSerializer();

        Assert(CheckHistory.Seconds(2149) == "2.1초" && CheckHistory.Seconds(0) == "0.0초" && CheckHistory.Seconds(-5) == "0.0초", "seconds");
        Assert(CheckHistory.Stages(new CheckRecord { NavMs = 900, WaitMs = 1040, ScrollMs = 0 }) == "이동 0.9 · 대기 1.0 · 스크롤 0.0", "stages");
        var timing = new CheckTiming { Attempts = 3 };
        timing.Stop();
        Assert(timing.Label.EndsWith("초(3회 시도)"), "retry label");
        timing.Attempts = 1;
        Assert(!timing.Label.Contains("시도"), "single attempt label");

        // 요약: 취소는 빼고 최근 window 회만, 확인 못 함은 따로 센다.
        Assert(CheckHistory.Summary(new List<CheckRecord>(), 20).StartsWith("아직"), "empty summary");
        var records = new List<CheckRecord> { Record(90000, true, false), Record(1000, true, false), Record(3000, false, false),
            Record(2000, true, false), Record(50000, false, true) };
        var summary = CheckHistory.Summary(records, 3);
        Assert(summary == "최근 3회 조회 · 평균 2.0초 · 중간값 2.0초 · 가장 느림 3.0초 · 확인 못 함 1회", summary);

        // 파일: 쓰고, 깨진 줄은 건너뛰고, 크면 .old 로 넘기고, 넘긴 파일까지 이어서 읽는다.
        var dir = Path.Combine(Path.GetTempPath(), "seatwatch-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "checks.jsonl");
            Assert(CheckHistory.OldPath(path) == Path.Combine(dir, "checks.old.jsonl"), "old path");
            Assert(CheckHistory.ReadTail(path, 10, json).Count == 0, "missing file");
            var t = new CheckTiming { NavMs = 800, WaitMs = 1200, Attempts = 1 };
            t.Stop();
            CheckHistory.Append(path, CheckHistory.Create(new DateTime(2026, 11, 19, 19, 34, 45), "CJU→GMP 11/22", "18:00~21:00",
                t, "좌석 있음 · 2개", true, false, 2), json);
            File.AppendAllText(path, "{not json\n", new UTF8Encoding(false));
            var read = CheckHistory.ReadTail(path, 10, json);
            Assert(read.Count == 1 && read[0].Time == "2026-11-19 19:34:45" && read[0].Route == "CJU→GMP 11/22" &&
                read[0].NavMs == 800 && read[0].Seats == 2 && read[0].Ok && read[0].Result == "좌석 있음 · 2개", "round trip");

            CheckHistory.RotateIfLarge(path, 10);
            Assert(!File.Exists(path) && File.Exists(CheckHistory.OldPath(path)), "rotate");
            CheckHistory.Append(path, Record(4000, true, false), json);
            read = CheckHistory.ReadTail(path, 10, json);
            Assert(read.Count == 2 && read[0].Seats == 2 && read[1].TotalMs == 4000, "read across rotation");
            Assert(CheckHistory.ReadTail(path, 1, json).Single().TotalMs == 4000, "tail limit");

            var log = Path.Combine(dir, "log.txt");
            File.WriteAllText(log, "첫 줄\r\n둘째 줄\r\n셋째 줄\r\n", Encoding.UTF8);
            Assert(CheckHistory.ReadTextTail(log, 1000) == "첫 줄\r\n둘째 줄\r\n셋째 줄\r\n", "short log");
            Assert(CheckHistory.ReadTextTail(log, 12) == "둘째 줄\r\n셋째 줄\r\n", "log tail keeps a line that fits exactly");
            Assert(CheckHistory.ReadTextTail(log, 11) == "셋째 줄\r\n", "log tail starts at a whole line");
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("CheckHistoryTest OK");
    }
}
