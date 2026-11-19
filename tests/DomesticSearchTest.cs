using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

class DomesticSearchTest
{
    static void Assert(bool value)
    {
        if (!value) throw new Exception("Domestic flight search regression");
    }

    static void Main()
    {
        var day = DateTime.Today.AddDays(2);
        var dayText = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var evening = new[] { "18:00-21:00", "21:00-24:00" };
        var url = DomesticSearch.Build("CJU", "GMP", day, evening.Reverse(), null);
        Assert(url == "https://flight.naver.com/flights/domestic/CJU:airport-GMP:airport-" + dayText +
            "?adult=1&fareType=YC&depTime0=18:00-21:00,21:00-24:00");
        var saved = DomesticSearch.Read(url);
        Assert(saved.From == "CJU" && saved.To == "GMP" && saved.Date == day && saved.Times.SequenceEqual(evening));
        Assert(!DomesticSearch.Build("CJU", "PUS", day, new string[0], null).Contains("depTime0"));
        Assert(!DomesticSearch.Build("CJU", "PUS", day, DomesticSearch.TimeSlots, null).Contains("depTime0"));
        var pasted = url.Replace("GMP:airport", "GMP%3Acity").Replace("adult=1", "adult=2&option=A&option=B")
            .Replace(",", "%2C") + "#result";
        saved = DomesticSearch.Read(pasted);
        Assert(DomesticSearch.Build("CJU", "GMP", day, evening, saved) == pasted);
        var edited = DomesticSearch.Build("PUS", "GMP", day.AddDays(1), new[] { evening[1] }, saved);
        Assert(edited.Contains("PUS:airport-GMP:city-") && edited.Contains("adult=2&option=A&option=B") &&
            edited.EndsWith("depTime0=21:00-24:00#result"));
        Assert(DomesticSearch.Read(url.Replace("18:00-21:00", "18:05-19:05")) == null);
        Assert(DomesticSearch.Read(url.Replace(dayText + "?", dayText + "/return?")) == null);
        try { DomesticSearch.Build("CJU", "CJU", day, evening, null); throw new Exception("Same airport accepted"); }
        catch (ArgumentException) { }
        try { DomesticSearch.Build("CJU", "GMP", DateTime.Today.AddDays(-1), evening, null); throw new Exception("Past date accepted"); }
        catch (ArgumentException) { }

        var filtered = DomesticSearch.Build("CJU", "GMP", day, evening, null, new[] { "LJ", "7C", "LJ" });
        Assert(filtered.Contains("airline=7C,LJ") && DomesticSearch.Read(filtered).Airlines.SequenceEqual(new[] { "7C", "LJ" }));
        var encoded = url + "&airline=7C%2CLJ&airline=TW&child=1";
        var airlineSearch = DomesticSearch.Read(encoded);
        Assert(airlineSearch.Airlines.SequenceEqual(new[] { "7C", "LJ", "TW" }));
        Assert(DomesticSearch.Build("CJU", "GMP", day, evening, airlineSearch, airlineSearch.Airlines.Reverse()) == encoded);
        var allAirlines = DomesticSearch.Build("CJU", "GMP", day, evening, airlineSearch, new string[0]);
        Assert(!allAirlines.Contains("airline=") && allAirlines.Contains("child=1"));
        Assert(DomesticSearch.AirlinesOf(filtered + "#&airline=KE").SequenceEqual(new[] { "7C", "LJ" }));

        var matches = typeof(MainForm).GetMethod("MatchesWatch", BindingFlags.NonPublic | BindingFlags.Static);
        var ranges = new List<string[]> { new[] { "18:00", "21:00" } };
        var carriers = new HashSet<string>(new[] { "7C", "LJ" }, StringComparer.OrdinalIgnoreCase);
        Func<string, string, string, bool> match = (name, code, dep) => (bool)matches.Invoke(null,
            new object[] { new Card { Airline = name, AirlineCode = code, Dep = dep }, ranges, carriers });
        Assert(match("제주 항공", null, "18:05"));
        Assert(match("표시명 변경", "LJ", "18:05"));
        Assert(!match("진에어", "KE", "18:05"));
        Assert(!match("대한항공", null, "18:05"));
        Assert(!match("진에어", "LJ", "17:00"));
        Assert(Airline.CodeFor("티웨이항공", null) == Airline.CodeFor("트리니티항공", null));

        // 알림 항공사: 로고 코드(판매 항공사) 우선, 싼 좌석 순, 중복·홈페이지 없는 곳(섬에어) 제외
        var seats = new[] {
            new Card { Airline = "진에어", AirlineCode = "KE", Price = 70300 },
            new Card { Airline = "아시아나항공", AirlineCode = "OZ", Price = 66300 },
            new Card { Airline = "대한항공", AirlineCode = "KE", Price = 69300 },
            new Card { Airline = "섬에어", Price = 50000 }
        };
        var sites = Airline.WithSites(seats);
        Assert(sites.Select(a => a.Code).SequenceEqual(new[] { "OZ", "KE" }));

        var naver = "https://flight.naver.com/flights/domestic/GMP:airport-CJU:airport-20261214";
        var one = MainForm.NtfyMessage("t", "제목", "내용", naver, new[] { Airline.FindByCode("LJ") });
        var oneActions = (List<Dictionary<string, object>>)one["actions"];
        Assert((string)one["click"] == "https://www.jinair.com/" && oneActions.Count == 1 && (string)oneActions[0]["url"] == naver);

        var many = MainForm.NtfyMessage("t", "제목", "내용", naver, Airline.Domestic.Where(a => a.Site != null).Take(4).ToList());
        var manyActions = (List<Dictionary<string, object>>)many["actions"];
        Assert((string)many["click"] == naver && manyActions.Count == 3 && (string)manyActions[0]["label"] == "대한항공");

        var none = MainForm.NtfyMessage("t", "제목", "내용", null, new Airline[0]);
        Assert((string)none["click"] == "https://flight.naver.com/" && !none.ContainsKey("actions"));

        // 텔레그램: 항공사 버튼(최대 4개) 아래에 네이버 버튼, 본문의 < > & 는 HTML 로 바꾼다
        Func<object, List<object[]>> keyboard = m => (List<object[]>)((Dictionary<string, object>)((Dictionary<string, object>)m)["reply_markup"])["inline_keyboard"];
        Func<object[], string> buttonUrl = row => (string)((Dictionary<string, object>)row[0])["url"];
        var tgOne = MainForm.TelegramMessage("42", "좌석 <1>", "진에어 & 대한항공", naver, new[] { Airline.FindByCode("LJ") });
        var tgOneRows = keyboard(tgOne);
        Assert((string)tgOne["text"] == "<b>좌석 &lt;1&gt;</b>\n진에어 &amp; 대한항공" && (string)tgOne["chat_id"] == "42");
        Assert(tgOneRows.Count == 2 && buttonUrl(tgOneRows[0]) == "https://www.jinair.com/" && buttonUrl(tgOneRows[1]) == naver);
        Assert((string)((Dictionary<string, object>)tgOneRows[0][0])["text"] == "진에어 홈페이지");
        var tgMany = keyboard(MainForm.TelegramMessage("42", "t", "m", null, Airline.Domestic.Where(a => a.Site != null).Take(6).ToList()));
        Assert(tgMany.Count == 5 && buttonUrl(tgMany[4]) == "https://flight.naver.com/");
        var serialized = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(tgOne);
        Assert(serialized.Contains("\"inline_keyboard\":[[{\"text\":"));

        Assert(MainForm.IsTelegramToken("123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw") && !MainForm.IsTelegramToken("seatwatch-abc") &&
            !MainForm.IsTelegramToken("123:ab/cd") && !MainForm.IsTelegramToken(null));
        Assert(MainForm.TelegramProblem(401, "").Contains("토큰") && MainForm.TelegramProblem(403, "").Contains("Start") &&
            MainForm.TelegramProblem(400, "{\"ok\":false,\"description\":\"Bad Request: chat not found\"}").Contains("채팅 ID") &&
            MainForm.TelegramProblem(400, "other") == null && MainForm.TelegramProblem(502, null) == null);

        var updates = "{\"ok\":true,\"result\":[" +
            "{\"update_id\":1,\"message\":{\"message_id\":1,\"chat\":{\"id\":111,\"first_name\":\"Old\",\"type\":\"private\"},\"text\":\"/start\"}}," +
            "{\"update_id\":2,\"my_chat_member\":{\"chat\":{\"id\":-1001234567890,\"title\":\"가족방\",\"type\":\"supergroup\"}}}," +
            "{\"update_id\":3,\"message\":{\"message_id\":2,\"chat\":{\"id\":987654321,\"first_name\":\"민수\",\"type\":\"private\"},\"text\":\"/start\"}}]}";
        var latest = MainForm.LatestTelegramChat(updates);
        Assert(latest[0] == "987654321" && latest[1] == "민수");
        var group = MainForm.LatestTelegramChat(updates.Substring(0, updates.IndexOf(",{\"update_id\":3")) + "]}");
        Assert(group[0] == "-1001234567890" && group[1] == "가족방");
        Assert(MainForm.LatestTelegramChat("{\"ok\":true,\"result\":[]}") == null);
        Console.WriteLine("Domestic flight conditions and URL compatibility checks passed.");
    }
}
