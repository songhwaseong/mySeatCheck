using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

class WatchRecoveryTest
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }

    static void Main()
    {
        var mobile = new Settings { NtfyTopic = "example", TelegramToken = "123:test-only", TelegramChat = "42" };
        var watch = new Watch { Url = "https://flight.naver.com/flights/domestic/GMP:airport-CJU:airport-20991231" };
        var a = new Card { Airline = "진에어", Dep = "11:05", Arr = "12:15", Cls = "일반석", Price = 68300 };
        var b = new Card { Airline = "진에어", Dep = "11:30", Arr = "12:40", Cls = "할인석", Price = 58000 };
        var first = new List<Card> { a };
        var second = new List<Card> { b };
        Check(watch.Observe(first, true, mobile).Count == 0, "silent baseline");
        Check(watch.Observe(second, false, mobile).Single() == b && watch.PrevKeys.Contains(a.Key), "positive-only observation");
        Check(watch.Observe(second, false, mobile).Count == 0, "deduplicate partial");
        Watch.FinishDelivery(watch.PendingNtfy, second, true);
        Watch.FinishDelivery(watch.PendingTelegram, second, false);
        Check(watch.PendingNtfy.Count == 0 && watch.PendingTelegram[b.Key] == 1, "independent retry");
        watch.Observe(first, false, mobile);
        Check(watch.PendingTelegram[b.Key] == 1, "unseen partial preserves pending");
        Check(!Watch.FinishDelivery(watch.PendingTelegram, second, false) && Watch.FinishDelivery(watch.PendingTelegram, second, false) &&
            watch.PendingTelegram.Count == 0, "three attempts");
        watch.Observe(first, true, mobile);
        Check(!watch.PrevKeys.Contains(b.Key) && watch.Observe(second, true, mobile).Single() == b, "confirmed disappearance and return");
        watch.Observe(first, true, mobile);
        Check(!watch.PendingNtfy.ContainsKey(b.Key) && !watch.PendingTelegram.ContainsKey(b.Key), "sold seat retires retries");
        Check(new Watch { NotifyFirst = true }.Observe(first, false, mobile).Single() == a && a.FareText.Contains("11:05→12:15"), "first-check opt-in and fare");
        watch.UpdateHealth(true, new DateTime(2026, 11, 19, 14, 2, 0));
        watch.UpdateHealth(false, DateTime.Now);
        Check(watch.LastSuccess == "2026-11-19 14:02:00" && watch.FailureStreak == 1, "health history");
        var route = FlightSearch.Read(watch.Url + "?adult=2&depTime0=18:05-19:05#result", true);
        Check(route.WithDate(new DateTime(2099, 12, 30)).EndsWith("20991230?adult=2&depTime0=18:05-19:05#result"), "copy custom search");
        Check(FlightSearch.Read(watch.Url.Replace("domestic", "international"), true).International &&
            FlightSearch.Read(watch.Url.Replace("domestic", "multicity"), true) == null &&
            FlightSearch.Read(watch.Url + "?depTime0=32:01-34:05", true) == null, "unsupported search");
        var archive = new Watch { Url = watch.Url };
        archive.EvaluateRoute(new DateTime(2099, 12, 31));
        Check(archive.Available, "departure day remains active");
        archive.PendingTelegram[a.Key] = 1;
        archive.EvaluateRoute(new DateTime(2100, 1, 1));
        Check(!archive.Available && archive.DisplayStatus(true).Contains("출발일") && archive.PendingTelegram.Count == 0,
            "expired watch is archived with pending retries cleared");
        archive.Url = "https://flight.naver.com/flights/international/test";
        archive.EvaluateRoute(DateTime.Today);
        Check(!archive.Available && archive.DisplayStatus(true).Contains("지원하지"), "legacy unsupported watch preserved and excluded");

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "SeatWatchTest-" + Guid.NewGuid().ToString("N")));
        try
        {
            string file = Path.Combine(dir.FullName, "settings.json"), notice;
            var store = new SettingsStore(file);
            var json = new JavaScriptSerializer();
            mobile.Watches.Add(watch);
            File.WriteAllText(file, json.Serialize(mobile));
            File.Copy(file, file + ".bak");
            var loaded = store.Load(out notice);
            Check(loaded.TelegramToken == mobile.TelegramToken && !File.ReadAllText(file).Contains(mobile.TelegramToken) &&
                !File.ReadAllText(file + ".bak").Contains(mobile.TelegramToken), "legacy token migration on both files");
            loaded.IntervalMin = 7;
            store.Save(loaded);
            Check(store.Load(out notice).IntervalMin == 7, "atomic save");
            File.WriteAllText(file, "{broken");
            var recovered = store.Load(out notice);
            Check(notice != null && recovered.IntervalMin == 3 && recovered.TelegramToken == mobile.TelegramToken &&
                recovered.Watches.Single().PendingTelegram.ContainsKey(a.Key), "backup recovers token, route and pending delivery");
            Check(store.Load(out notice).IntervalMin == 3 && notice == null, "repair primary");
            File.WriteAllText(file, "{}");
            File.WriteAllText(file + ".bak", "{broken");
            try { store.Load(out notice); throw new Exception("invalid config accepted"); }
            catch (IOException) { Check(File.ReadAllText(file) == "{}" && File.ReadAllText(file + ".bak") == "{broken", "both originals preserved"); }
        }
        finally { dir.Delete(true); }
        Console.WriteLine("WatchRecoveryTest OK");
    }
}
