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

    static readonly Dictionary<string, Airline> ByName = Domestic
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
        return Domestic.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    // 좌석을 파는 항공사(로고에서 읽은 코드 우선) 중 홈페이지가 있는 곳. 가장 싼 좌석이 있는 항공사부터.
    internal static List<Airline> WithSites(IEnumerable<Card> seats)
    {
        return seats.OrderBy(c => c.Price)
            .Select(c => FindByCode(CodeFor(c.Airline, c.AirlineCode)))
            .Where(a => a != null && a.Site != null).Distinct().ToList();
    }
}

internal sealed class DomesticSearch
{
    internal static readonly string[] TimeSlots = {
        "00:00-06:00", "06:00-09:00", "09:00-12:00", "12:00-15:00",
        "15:00-18:00", "18:00-21:00", "21:00-24:00"
    };
    internal readonly string From, To;
    internal readonly DateTime Date;
    internal readonly string[] Times, Airlines;
    readonly string original, fromType, toType, fragment;
    readonly string[] otherParameters;

    DomesticSearch(Uri uri, Match path, DateTime date, string[] times, string[] airlines, string[] other)
    {
        original = uri.OriginalString;
        From = path.Groups[1].Value;
        fromType = path.Groups[2].Value;
        To = path.Groups[3].Value;
        toType = path.Groups[4].Value;
        Date = date;
        Times = times;
        Airlines = airlines;
        otherParameters = other;
        fragment = uri.Fragment;
    }

    internal static DomesticSearch Read(string url, bool allowCustomTimes = false)
    {
        Uri uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "flight.naver.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0) return null;
        var path = Regex.Match(Uri.UnescapeDataString(uri.AbsolutePath),
            @"^/flights/domestic/([A-Z]{3}):(airport|city)-([A-Z]{3}):(airport|city)-(\d{8})/?$");
        DateTime date;
        if (!path.Success || path.Groups[1].Value == path.Groups[3].Value || !TicketRoute.Cities.ContainsKey(path.Groups[1].Value) ||
            !TicketRoute.Cities.ContainsKey(path.Groups[3].Value) ||
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
        return new DomesticSearch(uri, path, date, times, AirlineCodes(airlineParameters),
            parameters.Where(p => !filters.Contains(p) && !airlineParameters.Contains(p)).ToArray());
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
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !uri.AbsolutePath.StartsWith("/flights/domestic/", StringComparison.Ordinal))
            return new string[0];
        return AirlineCodes(AirlineParameters(uri.Query));
    }

    internal static string Build(string from, string to, DateTime date, IEnumerable<string> times, DomesticSearch previous,
        IEnumerable<string> airlines = null)
    {
        if (from == to) throw new ArgumentException("출발지와 도착지를 다르게 선택해 주세요.");
        if (!TicketRoute.Cities.ContainsKey(from) || !TicketRoute.Cities.ContainsKey(to))
            throw new ArgumentException("국내선 공항을 선택해 주세요.");
        if (date.Date < DateTime.Today) throw new ArgumentException("오늘 이후의 출발 날짜를 선택해 주세요.");
        var selected = new HashSet<string>(times);
        var selectedAirlines = new HashSet<string>(airlines ?? (previous == null ? new string[0] : previous.Airlines),
            StringComparer.OrdinalIgnoreCase);
        if (!selected.IsSubsetOf(TimeSlots)) throw new ArgumentException("출발 시간대를 다시 선택해 주세요.");
        if (previous != null && previous.From == from && previous.To == to && previous.Date == date.Date &&
            selected.SetEquals(previous.Times) && selectedAirlines.SetEquals(previous.Airlines)) return previous.original;
        var query = new List<string>(previous == null ? new[] { "adult=1", "fareType=YC" } : previous.otherParameters);
        if (selectedAirlines.Count > 0)
            query.Add("airline=" + string.Join(",", selectedAirlines.OrderBy(code => code, StringComparer.Ordinal).Select(Uri.EscapeDataString)));
        if (selected.Count > 0 && selected.Count < TimeSlots.Length)
            query.Add("depTime0=" + string.Join(",", TimeSlots.Where(selected.Contains)));
        return "https://flight.naver.com/flights/domestic/" + from + ":" +
            (previous != null && previous.From == from ? previous.fromType : "airport") + "-" + to + ":" +
            (previous != null && previous.To == to ? previous.toType : "airport") + "-" +
            date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + (query.Count > 0 ? "?" + string.Join("&", query) : "") +
            (previous == null ? "" : previous.fragment);
    }
}
