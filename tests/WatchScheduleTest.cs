using System;
using System.Collections.Generic;

// 조회 간격을 노선마다 따로 세는지 확인한다.
class WatchScheduleTest
{
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("Failed: " + name);
        Console.WriteLine("ok  " + name);
    }

    static Watch Route(string url)
    {
        var w = new Watch { Url = url };
        w.EvaluateRoute(new DateTime(2026, 11, 20));
        return w;
    }

    static void Main()
    {
        var a = Route("https://flight.naver.com/flights/domestic/GMP:airport-CJU:airport-20261220");
        var b = Route("https://flight.naver.com/flights/domestic/CJU:airport-GMP:airport-20261221");
        Check(a.Available && b.Available, "routes available");
        var list = new List<Watch> { a, b };
        var t0 = new DateTime(2026, 11, 20, 9, 0, 0);

        Check(Watch.NextDue(list, 3, t0) == a, "new routes run at once, in list order");

        // A 를 조회하고 10초 뒤에 B 를 조회했다.
        a.LastRun = t0;
        b.LastRun = t0.AddSeconds(10);
        Check(Watch.NextDue(list, 3, t0.AddSeconds(20)) == null, "nothing due right after");
        Check(Watch.NextDueAt(list, 3) == t0.AddMinutes(3), "next due is A");
        Check(Watch.NextDue(list, 3, t0.AddMinutes(3)) == a, "A due after its own interval");
        a.LastRun = t0.AddMinutes(3).AddSeconds(5);
        Check(Watch.NextDue(list, 3, t0.AddMinutes(3).AddSeconds(5)) == null, "B not pulled forward by A");
        Check(Watch.NextDue(list, 3, t0.AddMinutes(3).AddSeconds(10)) == b, "B keeps its own 10s offset");
        b.LastRun = t0.AddMinutes(3).AddSeconds(10);

        // 새로 추가한 노선만 바로 조회하고 다른 노선의 차례는 그대로다.
        var c = Route("https://flight.naver.com/flights/domestic/GMP:airport-PUS:airport-20261222");
        list.Add(c);
        Check(Watch.NextDue(list, 3, t0.AddMinutes(4)) == c, "added route runs at once");
        c.LastRun = t0.AddMinutes(4);
        Check(Watch.NextDue(list, 3, t0.AddMinutes(4)) == null, "others untouched by add");

        // 노선 감시를 끄면 차례에서 빠지고, 다시 켜면 그 노선만 바로 조회한다.
        b.Enabled = false;
        Check(Watch.NextDue(list, 3, t0.AddMinutes(6).AddSeconds(15)) == a, "disabled route skipped");
        b.Enabled = true;
        b.RunSoon();
        Check(Watch.NextDue(list, 3, t0.AddMinutes(5)) == b, "re-enabled route runs at once");

        // 조회 간격을 바꾸면 각 노선의 마지막 조회 시각부터 새 간격을 센다.
        b.LastRun = t0.AddMinutes(5);
        Check(Watch.NextDueAt(list, 10) == t0.AddMinutes(13).AddSeconds(5), "longer interval applies per route");

        // 노선에 따로 정한 간격은 그 노선에만 쓰고, 2분보다 짧게는 하지 않는다.
        a.IntervalMin = 15;
        c.IntervalMin = 1;
        Check(a.DueAt(3) == t0.AddMinutes(18).AddSeconds(5) && b.DueAt(3) == t0.AddMinutes(8), "own interval only for that route");
        Check(c.DueAt(3) == t0.AddMinutes(6), "own interval at least 2 minutes");
        a.IntervalMin = c.IntervalMin = 0;

        var off = Route("https://flight.naver.com/flights/domestic/GMP:airport-CJU:airport-20261101");
        Check(Watch.NextDue(new List<Watch> { off }, 3, t0) == null && Watch.NextDueAt(new List<Watch> { off }, 3) == null,
            "past-date route never due");
        Console.WriteLine("all passed");
    }
}
