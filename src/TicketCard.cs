using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class TicketStyle
{
    internal static readonly Color Paper = Color.FromArgb(250, 247, 239);
    internal static readonly Color Face = Color.FromArgb(255, 253, 247);
    internal static readonly Color Forest = Color.FromArgb(18, 67, 47);
    internal static readonly Color Orange = Color.FromArgb(204, 78, 13);
    internal static readonly Color Line = Color.FromArgb(208, 199, 177);
    internal static readonly Color Muted = Color.FromArgb(93, 96, 83);
}

internal sealed class TicketRoute
{
    internal static readonly Dictionary<string, string> Cities = new Dictionary<string, string> {
        {"CJU","제주"}, {"GMP","김포"}, {"PUS","부산"}, {"ICN","인천"}, {"TAE","대구"},
        {"KWJ","광주"}, {"CJJ","청주"}, {"USN","울산"}, {"RSU","여수"}, {"HIN","사천"},
        {"KPO","포항경주"}, {"KUV","군산"}, {"WJU","원주"}, {"YNY","양양"}, {"MWX","무안"}
    };
    // 국제선 공항 이름(카드 표시와 입력 제안용). 여기에 없는 코드도 감시할 수 있고, 그때는 코드로 보인다.
    internal static readonly Dictionary<string, string> Airports = new Dictionary<string, string> {
        {"ICN","인천"}, {"GMP","김포"}, {"PUS","부산"}, {"CJU","제주"}, {"TAE","대구"}, {"CJJ","청주"}, {"MWX","무안"}, {"YNY","양양"},
        {"NRT","도쿄 나리타"}, {"HND","도쿄 하네다"}, {"KIX","오사카 간사이"}, {"FUK","후쿠오카"}, {"CTS","삿포로"},
        {"OKA","오키나와"}, {"NGO","나고야"}, {"KOJ","가고시마"}, {"KMJ","구마모토"}, {"OIT","오이타"}, {"MYJ","마쓰야마"},
        {"TPE","타이베이 타오위안"}, {"TSA","타이베이 쑹산"}, {"KHH","가오슝"}, {"HKG","홍콩"}, {"MFM","마카오"},
        {"PVG","상하이 푸둥"}, {"SHA","상하이 훙차오"}, {"PEK","베이징 서우두"}, {"PKX","베이징 다싱"}, {"TAO","칭다오"},
        {"BKK","방콕"}, {"HKT","푸껫"}, {"CNX","치앙마이"}, {"DAD","다낭"}, {"CXR","나트랑"}, {"PQC","푸꾸옥"},
        {"SGN","호찌민"}, {"HAN","하노이"}, {"CEB","세부"}, {"MNL","마닐라"}, {"SIN","싱가포르"}, {"KUL","쿠알라룸푸르"},
        {"BKI","코타키나발루"}, {"DPS","발리"}, {"GUM","괌"}, {"SPN","사이판"}, {"ULN","울란바토르"},
        {"HNL","호놀룰루"}, {"LAX","로스앤젤레스"}, {"SFO","샌프란시스코"}, {"JFK","뉴욕"}, {"SEA","시애틀"},
        {"YVR","밴쿠버"}, {"SYD","시드니"}, {"CDG","파리"}, {"LHR","런던"}, {"FRA","프랑크푸르트"}, {"FCO","로마"},
        {"BCN","바르셀로나"}, {"IST","이스탄불"}, {"DXB","두바이"}
    };
    internal readonly string CitiesLabel, Codes, DateLabel, TimeLabel;

    internal static string PlaceName(string code)
    {
        string name;
        return Cities.TryGetValue(code, out name) || Airports.TryGetValue(code, out name) ? name : code;
    }

    internal TicketRoute(string url, string time)
    {
        TimeLabel = time.Replace("~", "–");
        var match = Regex.Match(Uri.UnescapeDataString(url),
            @"/([A-Z]{3})(?::(?:airport|city))?-([A-Z]{3})(?::(?:airport|city))?-(\d{8})");
        if (!match.Success)
        {
            CitiesLabel = "네이버 항공권";
            Codes = url;
            DateLabel = "";
            return;
        }
        string from = match.Groups[1].Value, to = match.Groups[2].Value;
        CitiesLabel = PlaceName(from) + " → " + PlaceName(to);
        Codes = from + " → " + to;
        DateTime date;
        DateLabel = DateTime.TryParseExact(match.Groups[3].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date) ? date.ToString("M월 d일 (ddd)", CultureInfo.GetCultureInfo("ko-KR")) : "";
    }
}

internal sealed class TicketCard : Button
{
    internal readonly Watch Watch;
    internal readonly TicketRoute Route;
    internal bool Selected;
    bool monitoring;

    internal TicketCard(Watch watch, string time)
    {
        Watch = watch;
        Route = new TicketRoute(watch.Url, time);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = TicketStyle.Paper;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        AccessibleRole = AccessibleRole.PushButton;
    }

    internal void UpdateState(bool selected, bool running)
    {
        Selected = selected;
        monitoring = running;
        AccessibleName = Route.CitiesLabel + ", " + Route.DateLabel + ", " + Route.TimeLabel +
            ", " + Watch.DisplayStatus(running);
        Invalidate();
    }

    static void DrawText(Graphics g, string text, float size, FontStyle style, Color color,
        float x, float y, float width, float height, StringAlignment align)
    {
        using (var font = new Font("맑은 고딕", size, style, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color))
        using (var format = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
            g.DrawString(text, font, brush, new RectangleF(x, y, Math.Max(1, width), height), format);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(TicketStyle.Paper);
        float scale = g.DpiX / 96f;
        var original = g.Save();
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float width = Width / scale, height = Height / scale;
        bool compact = height < 170;
        float right = width - 3, bottom = height - 3, notch = height * .54f;
        float stub = Math.Min(100, width * .21f);
        using (var shape = new GraphicsPath())
        using (var paper = new SolidBrush(TicketStyle.Face))
        using (var outline = new Pen(Selected ? TicketStyle.Orange : TicketStyle.Line, Selected ? 3 : 1.5f))
        {
            shape.AddArc(3, 3, 24, 24, 180, 90);
            shape.AddArc(right - 24, 3, 24, 24, 270, 90);
            shape.AddArc(right - 9, notch - 9, 18, 18, 270, -180);
            shape.AddArc(right - 24, bottom - 24, 24, 24, 0, 90);
            shape.AddArc(3, bottom - 24, 24, 24, 90, 90);
            shape.AddArc(-6, notch - 9, 18, 18, 90, -180);
            shape.CloseFigure();
            g.FillPath(paper, shape);
            float band = height - (compact ? 34 : 43);
            string status = Watch.DisplayStatus(monitoring);
            bool uncertain = status.StartsWith("확인 못 함");
            bool available = Watch.CurrentFares(monitoring).Count > 0;
            Color bandColor = uncertain ? Color.FromArgb(254, 225, 177) :
                available ? TicketStyle.Forest : Color.FromArgb(233, 228, 215);
            var clip = g.Save();
            g.SetClip(shape);
            using (var brush = new SolidBrush(bandColor)) g.FillRectangle(brush, 0, band, width, height - band);
            if (Selected)
            {
                using (var brush = new SolidBrush(TicketStyle.Orange))
                    g.FillPolygon(brush, new PointF[] { new PointF(right - 30, band), new PointF(right, band),
                        new PointF(right, bottom), new PointF(right - 48, bottom) });
            }
            g.Restore(clip);
            using (var seam = new Pen(TicketStyle.Line, 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawLine(seam, stub, 10, stub, band - 5);
            DrawText(g, "✈", compact ? 24 : 31, FontStyle.Regular, Selected ? TicketStyle.Orange : TicketStyle.Muted,
                7, 12, stub - 12, compact ? 36 : 51, StringAlignment.Center);
            DrawText(g, Watch.Available ? "감시 켜짐" : "감시 꺼짐", compact ? 11 : 12, FontStyle.Bold, TicketStyle.Muted,
                6, compact ? 49 : 63, stub - 10, 22, StringAlignment.Center);
            float x = stub + 14, room = right - x - 12;
            DrawText(g, Route.CitiesLabel, compact ? 14 : 16, FontStyle.Bold, TicketStyle.Forest,
                x, compact ? 6 : 9, room, compact ? 21 : 26, StringAlignment.Near);
            DrawText(g, Route.Codes, compact ? 29 : (width > 470 ? 41 : 33), FontStyle.Bold, TicketStyle.Forest,
                x, compact ? 25 : 32, room, compact ? 39 : 53, StringAlignment.Near);
            DrawText(g, Route.DateLabel, compact ? 12 : 14, FontStyle.Bold, TicketStyle.Muted,
                x, compact ? 64 : 88, room, compact ? 19 : 22, StringAlignment.Near);
            DrawText(g, Route.TimeLabel, compact ? 12 : 14, FontStyle.Regular, TicketStyle.Muted,
                x, compact ? 83 : 110, room, compact ? 21 : 24, StringAlignment.Near);
            DrawText(g, available ? "✓  " + status : status, compact ? 15 : 18, FontStyle.Bold, available ? Color.White : TicketStyle.Forest,
                19, band, right - 68, bottom - band, StringAlignment.Near);
            DrawText(g, "›", 25, FontStyle.Bold, Selected || available ? Color.White : TicketStyle.Muted,
                right - 30, band - 1, 22, bottom - band, StringAlignment.Center);
            g.DrawPath(outline, shape);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle((int)x, 10, Math.Max(1, (int)room), compact ? 94 : 120));
        }
        g.Restore(original);
    }
}
