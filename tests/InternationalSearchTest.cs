// 국제선 감시 주소·좌석 비교 시험. 빌드 방법은 README 의 '시험' 참고.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

class InternationalSearchTest
{
    static void Check(bool value, string name) { if (!value) throw new Exception("International search regression: " + name); }

    static void Main()
    {
        var day = DateTime.Today.AddDays(30);
        var dayText = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var evening = new[] { "21:00-24:00", "18:00-21:00" };

        // 사이트에는 없는 시간대·항공사 조건을 감시 주소에 적고, 페이지를 열 때는 뺀다.
        var url = FlightSearch.BuildInternational("ICN", "NRT", day, evening, true, null, new[] { "OZ", "KE" });
        Check(url == "https://flight.naver.com/flights/international/ICN-NRT-" + dayText +
            "?adult=1&fareType=Y&isDirect=true&airline=KE,OZ&depTime0=18:00-21:00,21:00-24:00", "build " + url);
        var saved = FlightSearch.Read(url);
        Check(saved.International && saved.Direct && saved.From == "ICN" && saved.To == "NRT" && saved.Date == day.Date &&
            saved.Times.SequenceEqual(new[] { "18:00-21:00", "21:00-24:00" }) && saved.Airlines.SequenceEqual(new[] { "KE", "OZ" }), "read");
        Check(saved.PageUrl == "https://flight.naver.com/flights/international/ICN-NRT-" + dayText + "?adult=1&fareType=Y&isDirect=true", "page url");
        Check(FlightSearch.PageOf(url) == saved.PageUrl && FlightSearch.IsInternational(url), "page of");
        Check(FlightSearch.AirlinesOf(url).SequenceEqual(new[] { "KE", "OZ" }), "airlines of");
        Check(FlightSearch.BuildInternational("ICN", "NRT", day, evening, true, saved, saved.Airlines) == url, "unchanged keeps url");
        var undirected = FlightSearch.BuildInternational("ICN", "NRT", day, evening, false, saved, saved.Airlines);
        Check(!undirected.Contains("isDirect") && !FlightSearch.Read(undirected).Direct, "direct off");
        Check(saved.WithDate(day.AddDays(1)).Contains("ICN-NRT-" + day.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "?"), "copy date");

        // 네이버 국제선 주소 그대로(앱 조건 없음)와 공항 종류가 붙은 주소도 받는다.
        var pasted = "https://flight.naver.com/flights/international/SEL:city-TYO:city-" + dayText + "?adult=1&fareType=Y";
        var city = FlightSearch.Read(pasted);
        Check(city != null && city.From == "SEL" && city.To == "TYO" && city.PageUrl == pasted && city.Times.Length == 0, "pasted city search");
        Check(FlightSearch.BuildInternational("SEL", "TYO", day, new[] { "06:00-09:00" }, false, city)
            .StartsWith("https://flight.naver.com/flights/international/SEL:city-TYO:city-" + dayText + "?adult=1&fareType=Y&depTime0="), "keeps city types");
        Check(FlightSearch.Read(pasted.Replace("TYO:city-" + dayText, "TYO:city-" + dayText + "/TYO-SEL-" + dayText)) == null, "round trip rejected");
        Check(FlightSearch.Read("https://flight.naver.com/flights/international/ICN-ICN-" + dayText) == null, "same airport");
        Check(FlightSearch.Read("https://flight.naver.com/flights/international/icn-NRT-" + dayText) == null, "lowercase code");

        Expect(() => FlightSearch.BuildInternational("ICN", "N1", day, evening, false, null), "bad code");
        Expect(() => FlightSearch.BuildInternational("ICN", "ICN", day, evening, false, null), "same code");
        Expect(() => FlightSearch.BuildInternational("ICN", "NRT", DateTime.Today.AddDays(-1), evening, false, null), "past date");
        // 국내선 수정 화면에 국제선 주소가 넘어와도 국내선 조건을 섞지 않는다.
        Check(FlightSearch.Build("CJU", "GMP", day, evening, saved).Contains("/domestic/CJU:airport-GMP:airport-") &&
            !FlightSearch.Build("CJU", "GMP", day, evening, saved).Contains("isDirect"), "domestic ignores international previous");

        // 공항 입력 칸의 글자에서 코드 꺼내기
        Check(MainForm.AirportCode("도쿄 나리타 (NRT)") == "NRT" && MainForm.AirportCode(" bkk ") == "BKK" &&
            MainForm.AirportCode("방콕") == "BKK" && MainForm.AirportCode("어딘가") == "어딘가", "airport code input");
        Check(TicketRoute.PlaceName("NRT") == "도쿄 나리타" && TicketRoute.PlaceName("CJU") == "제주" && TicketRoute.PlaceName("XYZ") == "XYZ", "place names");
        Check(new TicketRoute(url, "18:00~24:00").CitiesLabel == "인천 → 도쿄 나리타", "ticket label");

        // 국제선 좌석은 도착 시각까지 구분하고, 여러 항공사 일정은 고른 항공사가 하나라도 있으면 맞는다.
        var viaHnd = new Card { Airline = "대한항공, 일본항공", Dep = "18:50", Arr = "09:30+1", Cls = "경유 1", Price = 473200,
            International = true, AirlineCode = "KE", AirlineCodes = new[] { "KE", "JL" } };
        var viaOther = new Card { Airline = "대한항공, 일본항공", Dep = "18:50", Arr = "13:10+1", Cls = "경유 1", Price = 473200,
            International = true, AirlineCode = "KE", AirlineCodes = new[] { "KE", "JL" } };
        Check(viaHnd.Key != viaOther.Key && viaHnd.Key.EndsWith("→09:30+1"), "international key " + viaHnd.Key);
        Check(new Card { Airline = "진에어", Dep = "18:05", Arr = "19:15", Cls = "할인석" }.Key == "18:05 진에어 할인석", "domestic key unchanged");
        var ranges = new List<string[]> { new[] { "18:00", "21:00" } };
        Check(MainForm.MatchesWatch(viaHnd, ranges, new HashSet<string>(new[] { "JL" }, StringComparer.OrdinalIgnoreCase)), "any carrier matches");
        Check(!MainForm.MatchesWatch(viaHnd, ranges, new HashSet<string>(new[] { "OZ" }, StringComparer.OrdinalIgnoreCase)), "other carrier");
        Check(!MainForm.MatchesWatch(viaHnd, new List<string[]> { new[] { "06:00", "09:00" } }, new HashSet<string>()), "time window");
        Check(Airline.CodeFor("젯스타재팬", null) == "GK" && Airline.FindByCode("ZG").Name == "집에어" &&
            Airline.WithSites(new[] { new Card { Airline = "일본항공", AirlineCode = "JL", Price = 1 } }).Count == 0, "foreign airlines");

        var columns = (string[])typeof(MainForm).GetMethod("FareColumns", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static).Invoke(null, new object[] { "18:50→09:30+1 대한항공, 일본항공 경유 1 473,200원" });
        Check(columns.SequenceEqual(new[] { "대한항공, 일본항공 경유 1", "18:50", "09:30 (+1일)", "473,200원" }), "fare columns " + string.Join("|", columns));
        var domesticColumns = (string[])typeof(MainForm).GetMethod("FareColumns", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static).Invoke(null, new object[] { "18:05→19:15 진에어 할인석 45,600원" });
        Check(domesticColumns.SequenceEqual(new[] { "진에어 할인석", "18:05", "19:15", "45,600원" }), "domestic fare columns");

        var watch = new Watch { Url = url };
        watch.EvaluateRoute(DateTime.Today);
        Check(watch.Available, "international watch is active");
        watch.EvaluateRoute(day.AddDays(1));
        Check(!watch.Available && watch.DisplayStatus(true).Contains("출발일"), "international watch expires");
        Console.WriteLine("International search checks passed.");
    }

    static void Expect(Action build, string name)
    {
        try { build(); }
        catch (ArgumentException) { return; }
        throw new Exception("International search regression: " + name + " accepted");
    }
}
