using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class Airline
{
    internal static readonly Airline[] Domestic = {
        new Airline("KE", "대한항공", "https://www.koreanair.com/", "Korean Air"),
        new Airline("OZ", "아시아나항공", "https://flyasiana.com/", "아시아나", "Asiana Airlines"),
        new Airline("7C", "제주항공", "https://www.jejuair.net/", "Jeju Air"),
        new Airline("LJ", "진에어", "https://www.jinair.com/", "Jin Air"),
        new Airline("BX", "에어부산", "https://www.airbusan.com/", "Air Busan"),
        new Airline("RS", "에어서울", "https://flyairseoul.com/", "Air Seoul"),
        new Airline("ZE", "이스타항공", "https://www.eastarjet.com/", "Eastar Jet"),
        new Airline("RF", "에어로케이", "https://www.aerok.com/", "에어로케이항공", "에어로K", "Aero K"),
        new Airline("WE", "파라타항공", "https://www.parataair.com/", "Parata Air"),
        new Airline("YP", "에어프레미아", "https://www.airpremia.com/", "Air Premia"),
        new Airline("TW", "트리니티항공", "https://www.trinityairways.com/", "티웨이항공", "티웨이", "Trinity Airways", "T'way Air"),
        new Airline("XU", "섬에어", null)
    };

    // 국제선 감시의 항공사 선택지. 홈페이지 주소는 확인하지 않아 비워 두고, 알림에는 네이버 링크만 붙인다.
    internal static readonly Airline[] Foreign = {
        new Airline("JL", "일본항공", null, "Japan Airlines", "JAL"),
        new Airline("NH", "전일본공수", null, "ANA", "All Nippon Airways"),
        new Airline("MM", "피치항공", null, "피치", "Peach Aviation"),
        new Airline("GK", "젯스타 재팬", null, "Jetstar Japan"),
        new Airline("CX", "캐세이퍼시픽", null, "캐세이퍼시픽항공", "Cathay Pacific"),
        new Airline("UO", "홍콩익스프레스", null, "Hong Kong Express"),
        new Airline("BR", "에바항공", null, "EVA Air"),
        new Airline("CI", "중화항공", null, "China Airlines"),
        new Airline("CA", "중국국제항공", null, "Air China"),
        new Airline("MU", "중국동방항공", null, "China Eastern"),
        new Airline("CZ", "중국남방항공", null, "China Southern"),
        new Airline("SQ", "싱가포르항공", null, "Singapore Airlines"),
        new Airline("TG", "타이항공", null, "Thai Airways"),
        new Airline("VN", "베트남항공", null, "Vietnam Airlines"),
        new Airline("VJ", "비엣젯항공", null, "비엣젯", "VietJet Air"),
        new Airline("PR", "필리핀항공", null, "Philippine Airlines"),
        new Airline("5J", "세부퍼시픽", null, "세부퍼시픽항공", "Cebu Pacific"),
        new Airline("DL", "델타항공", null, "Delta Air Lines"),
        new Airline("UA", "유나이티드항공", null, "United Airlines"),
        new Airline("EK", "에미레이트항공", null, "Emirates"),
        new Airline("TK", "터키항공", null, "튀르키예항공", "Turkish Airlines"),
        new Airline("ZG", "집에어", null, "ZIPAIR", "Zipair Tokyo")
    };

    static readonly Dictionary<string, Airline> ByName = Domestic.Concat(Foreign)
        .SelectMany(a => a.Names.Select(n => new KeyValuePair<string, Airline>(n, a)))
        .ToDictionary(pair => NormalizeName(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    internal readonly string Code, Name, Site;
    readonly string[] Names;

    Airline(string code, string name, string site, params string[] aliases)
    {
        Code = code;
        Name = name;
        Site = site;
        Names = new[] { name }.Concat(aliases).ToArray();
    }

    static string NormalizeName(string name)
    {
        return Regex.Replace(name ?? "", @"\s+", "");
    }

    internal static Airline FindByName(string name)
    {
        Airline airline;
        return ByName.TryGetValue(NormalizeName(name), out airline) ? airline : null;
    }

    internal static string CodeFor(string name, string code)
    {
        if (!string.IsNullOrEmpty(code)) return code;
        var airline = FindByName(name);
        return airline == null ? null : airline.Code;
    }

    internal static Airline FindByCode(string code)
    {
        return Domestic.Concat(Foreign).FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    // 좌석을 파는 항공사(로고에서 읽은 코드 우선) 중 홈페이지가 있는 곳. 가장 싼 좌석이 있는 항공사부터.
    internal static List<Airline> WithSites(IEnumerable<Card> seats)
    {
        return seats.OrderBy(c => c.Price)
            .Select(c => FindByCode(CodeFor(c.Airline, c.AirlineCode)))
            .Where(a => a != null && a.Site != null).Distinct().ToList();
    }
}

// 네이버 항공권 편도 검색 주소. 국내선은 시간대·항공사를 사이트가 거르고(주소의 depTime0·airline),
// 국제선은 사이트 주소에 그런 조건이 없어서 같은 이름의 파라미터를 감시 주소에만 적어 두고 앱이 직접 거른다.
// 국제선 페이지를 열 때는 PageUrl 로 그 파라미터를 뺀다.
internal sealed class FlightSearch
{
    internal static readonly string[] TimeSlots = {
        "00:00-06:00", "06:00-09:00", "09:00-12:00", "12:00-15:00",
        "15:00-18:00", "18:00-21:00", "21:00-24:00"
    };
    const string Host = "https://flight.naver.com/flights/";
    internal readonly bool International;
    internal readonly string From, To;
    internal readonly DateTime Date;
    internal readonly string[] Times, Airlines;
    internal readonly bool Direct; // 국제선 직항만(isDirect=true)
    readonly string original, fromType, toType, fragment;
    readonly string[] otherParameters;

    FlightSearch(Uri uri, bool international, Match path, DateTime date, string[] times, string[] airlines, string[] other)
    {
        original = uri.OriginalString;
        International = international;
        From = path.Groups[1].Value;
        fromType = path.Groups[2].Value;
        To = path.Groups[3].Value;
        toType = path.Groups[4].Value;
        Date = date;
        Times = times;
        Airlines = airlines;
        otherParameters = other;
        Direct = international && other.Any(p => p == "isDirect=true");
        fragment = uri.Fragment;
    }

    internal static bool IsAirportCode(string code)
    {
        return code != null && Regex.IsMatch(code, "^[A-Z]{3}$");
    }

    internal static FlightSearch Read(string url, bool allowCustomTimes = false)
    {
        Uri uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "flight.naver.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0) return null;
        var absolutePath = Uri.UnescapeDataString(uri.AbsolutePath);
        bool international = absolutePath.StartsWith("/flights/international/", StringComparison.Ordinal);
        var path = international
            ? Regex.Match(absolutePath, @"^/flights/international/([A-Z]{3})(?::(airport|city))?-([A-Z]{3})(?::(airport|city))?-(\d{8})/?$")
            : Regex.Match(absolutePath, @"^/flights/domestic/([A-Z]{3}):(airport|city)-([A-Z]{3}):(airport|city)-(\d{8})/?$");
        DateTime date;
        if (!path.Success || path.Groups[1].Value == path.Groups[3].Value ||
            (!international && (!TicketRoute.Cities.ContainsKey(path.Groups[1].Value) || !TicketRoute.Cities.ContainsKey(path.Groups[3].Value))) ||
            !DateTime.TryParseExact(path.Groups[5].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
            date < System.Windows.Forms.DateTimePicker.MinimumDateTime || date > System.Windows.Forms.DateTimePicker.MaximumDateTime)
            return null;
        var parameters = uri.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
        var filters = parameters.Where(p => Uri.UnescapeDataString(p.Split('=')[0]) == "depTime0").ToArray();
        if (filters.Length > 1) return null;
        var times = filters.Length == 0 ? new string[0] : Uri.UnescapeDataString(filters[0].Substring(filters[0].IndexOf('=') + 1)).Split(',');
        if (times.Any(t => !Regex.IsMatch(t, @"^(?:[01]\d|2[0-3]):[0-5]\d-(?:(?:[01]\d|2[0-3]):[0-5]\d|24:00)$") ||
            string.CompareOrdinal(t.Substring(0, 5), t.Substring(6)) >= 0)) return null;
        if (!allowCustomTimes && times.Except(TimeSlots).Any()) return null;
        var airlineParameters = AirlineParameters(uri.Query);
        return new FlightSearch(uri, international, path, date, times, AirlineCodes(airlineParameters),
            parameters.Where(p => !filters.Contains(p) && !airlineParameters.Contains(p)).ToArray());
    }

    // WebView2 로 여는 네이버 주소. 국내선은 감시 주소 그대로, 국제선은 앱 전용 조건(depTime0·airline)을 뺀다.
    internal string PageUrl
    {
        get
        {
            if (!International) return original;
            var uri = new Uri(original);
            return uri.GetLeftPart(UriPartial.Path) + (otherParameters.Length > 0 ? "?" + string.Join("&", otherParameters) : "") + uri.Fragment;
        }
    }

    internal static string PageOf(string url)
    {
        var search = Read(url, true);
        return search == null ? url : search.PageUrl;
    }

    internal static bool IsInternational(string url)
    {
        Uri uri;
        return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
            Uri.UnescapeDataString(uri.AbsolutePath).StartsWith("/flights/international/", StringComparison.Ordinal);
    }

    internal string WithDate(DateTime date)
    {
        var uri = new Uri(original);
        var path = Regex.Replace(Uri.UnescapeDataString(uri.AbsolutePath), @"\d{8}(/?)$",
            date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "${1}");
        return uri.GetLeftPart(UriPartial.Authority) + path + uri.Query + uri.Fragment;
    }

    static string[] AirlineParameters(string query)
    {
        return query.TrimStart('?').Split('&')
            .Where(p => p.IndexOf('=') >= 0 && Uri.UnescapeDataString(p.Substring(0, p.IndexOf('='))) == "airline").ToArray();
    }

    static string[] AirlineCodes(IEnumerable<string> parameters)
    {
        return parameters.SelectMany(p => Uri.UnescapeDataString(p.Substring(p.IndexOf('=') + 1)).Split(','))
            .Select(code => code.Trim()).Where(code => code.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string[] AirlinesOf(string url)
    {
        Uri uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !(uri.AbsolutePath.StartsWith("/flights/domestic/", StringComparison.Ordinal) ||
            uri.AbsolutePath.StartsWith("/flights/international/", StringComparison.Ordinal)))
            return new string[0];
        return AirlineCodes(AirlineParameters(uri.Query));
    }

    internal static string Build(string from, string to, DateTime date, IEnumerable<string> times, FlightSearch previous,
        IEnumerable<string> airlines = null)
    {
        if (from == to) throw new ArgumentException("출발지와 도착지를 다르게 선택해 주세요.");
        if (!TicketRoute.Cities.ContainsKey(from) || !TicketRoute.Cities.ContainsKey(to))
            throw new ArgumentException("국내선 공항을 선택해 주세요.");
        if (previous != null && previous.International) previous = null;
        return Compose(false, from, to, date, times, previous, airlines, false);
    }

    internal static string BuildInternational(string from, string to, DateTime date, IEnumerable<string> times, bool direct,
        FlightSearch previous, IEnumerable<string> airlines = null)
    {
        if (!IsAirportCode(from) || !IsAirportCode(to))
            throw new ArgumentException("공항은 영문 대문자 세 글자 코드(예: ICN, NRT)로 입력해 주세요.");
        if (from == to) throw new ArgumentException("출발지와 도착지를 다르게 선택해 주세요.");
        if (previous != null && !previous.International) previous = null;
        return Compose(true, from, to, date, times, previous, airlines, direct);
    }

    static string Compose(bool international, string from, string to, DateTime date, IEnumerable<string> times, FlightSearch previous,
        IEnumerable<string> airlines, bool direct)
    {
        if (date.Date < DateTime.Today) throw new ArgumentException("오늘 이후의 출발 날짜를 선택해 주세요.");
        var selected = new HashSet<string>(times);
        var selectedAirlines = new HashSet<string>(airlines ?? (previous == null ? new string[0] : previous.Airlines),
            StringComparer.OrdinalIgnoreCase);
        if (!selected.IsSubsetOf(TimeSlots)) throw new ArgumentException("출발 시간대를 다시 선택해 주세요.");
        if (previous != null && previous.From == from && previous.To == to && previous.Date == date.Date &&
            selected.SetEquals(previous.Times) && selectedAirlines.SetEquals(previous.Airlines) && previous.Direct == direct)
            return previous.original;
        var query = new List<string>(previous == null
            ? (international ? new[] { "adult=1", "fareType=Y" } : new[] { "adult=1", "fareType=YC" })
            : previous.otherParameters);
        if (international)
        {
            query.RemoveAll(p => Uri.UnescapeDataString(p.Split('=')[0]) == "isDirect");
            if (direct) query.Add("isDirect=true");
        }
        if (selectedAirlines.Count > 0)
            query.Add("airline=" + string.Join(",", selectedAirlines.OrderBy(code => code, StringComparer.Ordinal).Select(Uri.EscapeDataString)));
        if (selected.Count > 0 && selected.Count < TimeSlots.Length)
            query.Add("depTime0=" + string.Join(",", TimeSlots.Where(selected.Contains)));
        string fromPart = from, toPart = to;
        if (!international)
        {
            fromPart += ":" + (previous != null && previous.From == from ? previous.fromType : "airport");
            toPart += ":" + (previous != null && previous.To == to ? previous.toType : "airport");
        }
        else
        {
            if (previous != null && previous.From == from && previous.fromType.Length > 0) fromPart += ":" + previous.fromType;
            if (previous != null && previous.To == to && previous.toType.Length > 0) toPart += ":" + previous.toType;
        }
        return Host + (international ? "international/" : "domestic/") + fromPart + "-" + toPart + "-" +
            date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + (query.Count > 0 ? "?" + string.Join("&", query) : "") +
            (previous == null ? "" : previous.fragment);
    }
}
