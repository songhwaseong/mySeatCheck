// 좌석 감시 — 네이버 항공권 검색 주소를 몇 분마다 다시 열어, 정한 출발 시간대에 좌석이 생기면
// 윈도우 알림과 휴대폰(ntfy·텔레그램)으로 알린다. 조회 화면은 내장 WebView2에서 열 수 있다.
//
// 컴파일러는 .NET Framework 의 csc(C# 5) — $"" · ?. · => 멤버 · out var 를 쓰지 말 것.
// 지켜야 할 규칙(2026-11-16 실측, D:\my\_flight-watch 에서 옮김):
//   - 화면 없는(headless) 브라우저에선 목록이 안 뜬다 → 보이는 WebView2 로 연다.
//   - 새로고침하면 사이트 시간대 거르기가 빠지고, 그 목록은 내려도 11시대에서 멈춘다
//     → 매번 주소로 새로 열고, 거르기가 빠졌으면 최대 3번 다시 연다. 그래도 빠지면 '없음'으로 치지 않는다.
//   - 목록을 하나도 못 읽은 회차도 '없음'으로 치지 않는다(로딩 실패로 알림이 잘못 울림).
// 국제선(2026-11-20 실측):
//   - 사이트 주소에 출발 시간대·항공사 조건이 없다 → 감시 주소에만 적어 두고(FlightSearch.PageUrl 로 빼고 연다) 앱이 거른다.
//   - 목록은 한 번에 모두 뜨고 스크롤해도 늘지 않는다. '동일가 다른 일정'으로 접힌 편은 읽지 못한다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CancellationToken = System.Threading.CancellationToken;
using CancellationTokenSource = System.Threading.CancellationTokenSource;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

public class Watch
{
    public string Url;
    public bool Enabled = true;
    public string LastCheck = "";
    public string LastDuration = ""; // 마지막 조회에 걸린 시간(예: "2.1초"). 예전 설정 파일에는 없어 빈칸.
    public List<string> Seats = new List<string>();
    public List<string> PrevKeys; // null = 아직 제대로 읽은 적 없음
    public bool NotifyFirst;
    public string LastSuccess = "";
    public int FailureStreak;
    public Dictionary<string, int> PendingNtfy = new Dictionary<string, int>();
    public Dictionary<string, int> PendingTelegram = new Dictionary<string, int>();
    internal string InactiveReason;
    internal Task Delivery;
    internal bool Available { get { return Enabled && InactiveReason == null; } }
    internal const string Partial = "일부 확인";
    public long MaxPrice; // 0 = 금액 제한 없음. 네이버 주소에는 금액 조건이 없어서 앱이 직접 거른다.
    internal int OverPrice; // 마지막 조회에서 최대 금액을 넘어 뺀 좌석 수
    internal int HiddenSchedules; // 국제선 마지막 조회에서 '동일가 다른 일정'으로 접혀 읽지 못한 편 수

    // 실행을 다시 시작하면 저장된 좌석은 알림 비교용으로만 남는다.
    internal const string Waiting = "조회 대기";
    internal string Unconfirmed = Waiting; // null이면 이번 실행에서 조회에 성공함
    static readonly string[] NoFares = new string[0];

    internal IList<string> CurrentFares(bool running)
    {
        if (!running || !Available || (Unconfirmed != null && Unconfirmed != Partial)) return NoFares;
        return Seats;
    }

    internal string DisplayStatus(bool running)
    {
        if (InactiveReason != null) return InactiveReason;
        if (!Enabled) return "노선 감시 꺼짐";
        if (!running) return "전체 감시 멈춤";
        if (Unconfirmed == Partial) return Partial + " · 조건에 맞는 좌석 " + Seats.Count + "개 확인";
        if (Unconfirmed != null) return Unconfirmed;
        if (Seats.Count == 0) return OverPrice > 0 ? "금액 초과 좌석만 " + OverPrice + "개" : "좌석 없음";
        return "좌석 있음 · " + Seats.Count + "개";
    }

    internal bool Allows(long price)
    {
        return MaxPrice <= 0 || price <= MaxPrice;
    }

    internal List<Card> Observe(List<Card> hits, bool complete, Settings target)
    {
        var seen = new HashSet<string>(PrevKeys ?? new List<string>());
        var fresh = hits.Where(c => (PrevKeys != null || NotifyFirst) && !seen.Contains(c.Key)).ToList();
        var keys = new HashSet<string>(hits.Select(c => c.Key));
        UpdatePending(PendingNtfy, target.UsesNtfy(), fresh, keys, complete);
        UpdatePending(PendingTelegram, target.UsesTelegram(), fresh, keys, complete);
        if (!complete) keys.UnionWith(seen);
        PrevKeys = keys.ToList();
        return fresh;
    }

    static void UpdatePending(Dictionary<string, int> pending, bool enabled, List<Card> fresh, HashSet<string> present, bool complete)
    {
        if (!enabled) { pending.Clear(); return; }
        if (complete)
            foreach (var key in pending.Keys.Where(k => !present.Contains(k)).ToList()) pending.Remove(key);
        foreach (var seat in fresh) pending[seat.Key] = 0;
    }

    internal static bool FinishDelivery(Dictionary<string, int> pending, IEnumerable<Card> seats, bool success)
    {
        bool exhausted = false;
        foreach (var seat in seats)
        {
            int attempts;
            if (!pending.TryGetValue(seat.Key, out attempts)) continue;
            if (success) pending.Remove(seat.Key);
            else if (attempts >= 2) { pending.Remove(seat.Key); exhausted = true; }
            else pending[seat.Key] = attempts + 1;
        }
        return exhausted;
    }

    internal void UpdateHealth(bool complete, DateTime finished)
    {
        if (complete) { LastSuccess = finished.ToString("yyyy-MM-dd HH:mm:ss"); FailureStreak = 0; }
        else FailureStreak++;
    }

    internal void EvaluateRoute(DateTime today)
    {
        var route = FlightSearch.Read(Url, true);
        InactiveReason = route == null ? "지원하지 않는 검색 주소" : route.Date < today.Date ? "감시 종료 · 출발일 지남" : null;
        if (InactiveReason == null) return;
        PendingNtfy.Clear();
        PendingTelegram.Clear();
    }

    internal static string PriceLabel(long price)
    {
        if (price <= 0) return "";
        return (price % 10000 == 0 ? (price / 10000).ToString("N0") + "만 원" : price.ToString("N0") + "원") + " 이하";
    }
}

public class Settings
{
    public int IntervalMin = 3;
    public string NtfyTopic = "";
    public string TelegramToken = "", TelegramChat = "";
    public string ProtectedTelegramToken = "";
    // 체크를 풀면 적어 둔 값은 그대로 두고 보내지만 않는다. 예전 설정 파일에는 없으므로 켜짐으로 읽힌다.
    public bool NtfyOn = true, TelegramOn = true;
    public bool Running = true; // 전체 시작·멈춤(다음 실행 때도 이어짐)
    public bool InternationalTab; // 마지막으로 본 탑승권 탭(국내선·국제선). 보기만 나누며 감시는 둘 다 계속한다.
    public List<Watch> Watches = new List<Watch>();

    internal bool UsesNtfy() { return NtfyOn && !string.IsNullOrEmpty(NtfyTopic); }
    internal bool UsesTelegram() { return TelegramOn && !string.IsNullOrEmpty(TelegramToken) && !string.IsNullOrEmpty(TelegramChat); }
    internal Settings Copy() { return (Settings)MemberwiseClone(); }
    internal Settings ForStorage()
    {
        var copy = Copy();
        copy.ProtectedTelegramToken = string.IsNullOrEmpty(TelegramToken) ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(TelegramToken), null, DataProtectionScope.CurrentUser));
        copy.TelegramToken = "";
        return copy;
    }
}

public class Card
{
    public string Airline, AirlineCode, Dep, Arr, Cls;
    public string[] AirlineCodes; // 국제선은 여러 항공사 일정이 한 줄에 나온다(로고 순서)
    public bool International;
    public int Stops = -1;  // 국제선 경유 횟수(직항 0). 모르면 -1
    public int Minutes;     // 국제선 총 소요시간(분). 모르면 0
    public long Price;
    // 국제선은 출발 시각·항공사·직항/경유가 같아도 경유 일정이 다를 수 있어 도착 시각(+1일 포함)까지 넣는다.
    public string Key { get { return Dep + " " + Airline + " " + Cls + (International ? " →" + Arr : ""); } }
    internal string FareText { get { return Airline + " " + Dep + "→" + Arr + " " + Cls + " " + Price.ToString("N0") + "원"; } }
}

public partial class MainForm : Form
{
    const int MinInterval = 2;
    static readonly string AppDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    static readonly string DataDir = Path.Combine(AppDir, "data");
    static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");
    static readonly string LogPath = Path.Combine(DataDir, "log.txt");
    static readonly string ChecksPath = Path.Combine(DataDir, "checks.jsonl");

    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    Settings settings = new Settings();
    readonly SettingsStore store = new SettingsStore(SettingsPath);
    DateTime routeDay;
    bool saveWarning;
    string parseJs;

    WebView2 web;
    TextBox logBox;
    Label nextLabel;
    Button runBtn;
    ToolStripItem trayRunItem, trayCheckItem;
    NotifyIcon tray;
    Timer ticker;

    bool busy, exiting, webReady, webFailed;
    Watch activeWatch;
    CancellationTokenSource activeCheck;
    DateTime nextRun = DateTime.Now.AddSeconds(5);

    public MainForm()
    {
        Text = "좌석 감시";
        Font = new Font("맑은 고딕", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(1180, 750);
        MinimumSize = new Size(880, 585);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = LoadAppIcon(Size.Empty);

        Directory.CreateDirectory(DataDir);
        string recovery;
        settings = store.Load(out recovery);
        UpdateRouteDay();
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("parse.js"))
        using (var r = new StreamReader(s, Encoding.UTF8)) parseJs = r.ReadToEnd();

        BuildUi();
        LoadHistory();
        BuildTray();
        RefreshList();
        if (recovery != null) Shown += (s, e) => { Log(recovery); MessageBox.Show(this, recovery, "설정 복구"); };

        Load += async (s, e) => await InitWebAsync();
        FormClosing += OnClosing;
        Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) { Hide(); ShowTrayHint(); } };

        ticker = new Timer { Interval = 1000 };
        ticker.Tick += async (s, e) => await OnTick();
        ticker.Start();
    }

    // EXE 에 리소스로 들어간 SeatWatch.ico. size 가 비어 있으면 여러 크기를 모두 담은 아이콘.
    static Icon LoadAppIcon(Size size)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SeatWatch.ico"))
        {
            if (s == null) return SystemIcons.Application;
            return size.IsEmpty ? new Icon(s) : new Icon(s, size);
        }
    }

    void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("열기", null, (s, e) => ShowWindow());
        trayRunItem = menu.Items.Add("", null, (s, e) => SetRunning(!settings.Running));
        trayCheckItem = menu.Items.Add("지금 확인", null, (s, e) => CheckNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("끝내기", null, (s, e) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = LoadAppIcon(SystemInformation.SmallIconSize), Text = "좌석 감시", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (s, e) => ShowWindow();
        tray.BalloonTipClicked += (s, e) => ShowWindow();
        UpdateRunUi();
    }

    // 전체 시작·멈춤. 멈추면 조회 중이던 감시 하나는 마저 끝내고 그다음부터 쉰다.
    void SetRunning(bool on)
    {
        if (settings.Running == on) return;
        settings.Running = on;
        if (on)
            foreach (var watch in settings.Watches) watch.Unconfirmed = Watch.Waiting;
        SaveSettings();
        if (on) nextRun = DateTime.Now;
        Log(on ? "▶ 감시 시작" : "■ 감시 멈춤");
        UpdateRunUi();
        RefreshList();
    }

    void UpdateRunUi()
    {
        bool on = settings.Running;
        if (runBtn != null)
        {
            // 버튼은 누르면 일어날 동작을 보여 준다. 켜져 있으면 일시정지, 꺼져 있으면 재생.
            if (on) SetIcon(runBtn, Glyph.Pause, "모니터링 켜짐 · 누르면 끄기");
            else SetIcon(runBtn, Glyph.Play, "모니터링 꺼짐 · 누르면 켜기");
            runBtn.BackColor = on ? TicketStyle.Orange : TicketStyle.Forest;
        }
        if (trayRunItem != null) trayRunItem.Text = on ? "감시 멈춤" : "감시 시작";
        if (tray != null) tray.Text = on ? "좌석 감시 · 감시 중" : "좌석 감시 · 멈춤";
        Text = on ? "SeatWatch · 좌석 감시" : "SeatWatch · 감시 멈춤";
    }

    void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    bool trayHintShown;
    void ShowTrayHint()
    {
        if (trayHintShown) return;
        trayHintShown = true;
        tray.ShowBalloonTip(5000, "좌석 감시", "알림 영역에서 계속 감시합니다. 끝내려면 아이콘 오른쪽 클릭 → 끝내기.", ToolTipIcon.Info);
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowTrayHint();
            return;
        }
        exiting = true;
        if (activeCheck != null) activeCheck.Cancel();
        tray.Visible = false;
        SaveSettings();
    }

    // ───────────── 감시 목록 ─────────────
    bool SaveWatch(Form dialog, string address, Watch previous, long maxPrice, bool notifyFirst)
    {
        var url = address.Trim();
        var search = FlightSearch.Read(url, true);
        if (search == null || search.Date < DateTime.Today)
        {
            MessageBox.Show(dialog, search == null ? "감시할 수 있는 주소는 네이버 항공권 국내선·국제선 편도 검색 결과입니다. 노선·날짜·출발 시간대를 확인해 주세요." :
                "출발일이 지났습니다. 오늘 이후의 날짜를 선택해 주세요.", "좌석 감시");
            return false;
        }
        // 금액은 주소 밖에 저장하므로 주소가 같아도 금액이 바뀌었으면 새 조건으로 저장한다.
        if (previous != null && SamePage(previous.Url, url) && previous.MaxPrice == maxPrice)
        {
            var oldValue = previous.NotifyFirst;
            previous.NotifyFirst = notifyFirst;
            if (SaveSettings()) return true;
            previous.NotifyFirst = oldValue;
            return false;
        }
        if (settings.Watches.Any(w => w != previous && SamePage(w.Url, url))) { MessageBox.Show(dialog, "이미 감시 중인 주소입니다.", "좌석 감시"); return false; }
        var replacement = new Watch { Url = url, Enabled = previous == null || previous.Enabled,
            MaxPrice = Math.Max(0, maxPrice), NotifyFirst = notifyFirst };
        var proposed = settings.Copy();
        proposed.Watches = new List<Watch>(settings.Watches);
        if (previous == null) proposed.Watches.Add(replacement);
        else proposed.Watches[proposed.Watches.IndexOf(previous)] = replacement;
        if (!SaveSettings(proposed)) return false;
        settings.Watches = proposed.Watches;
        selectedWatch = replacement;
        settings.InternationalTab = search.International;
        if (previous != null && previous == activeWatch) activeCheck.Cancel();
        RefreshList();
        Log((previous == null ? "감시 추가: " : "감시 조건 변경: ") + LabelOf(url) + " · " + RangeLabel(url) +
            (search.ConditionLabel.Length > 0 ? " · " + search.ConditionLabel : "") +
            (maxPrice > 0 ? " · " + Watch.PriceLabel(maxPrice) : ""));
        nextRun = DateTime.Now;
        return true;
    }

    void DeleteSelected()
    {
        var w = selectedWatch;
        if (w == null) return;
        if (MessageBox.Show(this, LabelOf(w.Url) + " 감시를 지울까요?", "좌석 감시", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        settings.Watches.Remove(w);
        if (w == activeWatch) activeCheck.Cancel();
        SaveSettings();
        RefreshList();
    }

    // ───────────── 주소 풀이 ─────────────
    static string LabelOf(string url)
    {
        var m = Regex.Match(Uri.UnescapeDataString(url), @"/([A-Z]{3})(?::(?:airport|city))?-([A-Z]{3})(?::(?:airport|city))?-(\d{4})(\d{2})(\d{2})");
        if (!m.Success) return url;
        return m.Groups[1].Value + "→" + m.Groups[2].Value + " " + int.Parse(m.Groups[4].Value) + "/" + int.Parse(m.Groups[5].Value);
    }

    static List<string[]> RangesOf(string url)
    {
        var search = FlightSearch.Read(url, true);
        var raw = search != null ? string.Join(",", search.Times) : "";
        var result = new List<string[]>();
        foreach (var part in raw.Split(','))
        {
            var ab = part.Split('-');
            if (ab.Length == 2) result.Add(new[] { ab[0].Trim(), ab[1].Trim() });
        }
        if (result.Count == 0) result.Add(new[] { "00:00", "23:59" });
        return result;
    }

    static string RangeLabel(string url)
    {
        return string.Join(", ", RangesOf(url).Select(r => r[0] + "~" + r[1]));
    }

    // "HH:MM" 은 글자 비교로 시각 비교가 된다(24:00 도 됨)
    static bool InRange(string t, List<string[]> ranges)
    {
        return ranges.Any(r => string.CompareOrdinal(t, r[0]) >= 0 && string.CompareOrdinal(t, r[1]) <= 0);
    }

    // 국제선의 여러 항공사 일정은 고른 항공사가 하나라도 들어 있으면 맞는 것으로 친다.
    // search 가 있으면 사이트가 거르지 않는 국제선 조건(경유 횟수·소요시간)도 본다.
    internal static bool MatchesWatch(Card card, List<string[]> ranges, HashSet<string> airlines, FlightSearch search = null)
    {
        if (!InRange(card.Dep, ranges)) return false;
        if (search != null && !search.Allows(card)) return false;
        if (airlines.Count == 0) return true;
        if (card.AirlineCodes != null && card.AirlineCodes.Any(airlines.Contains)) return true;
        return airlines.Contains(Airline.CodeFor(card.Airline, card.AirlineCode));
    }

    // ───────────── 조회 ─────────────
    async Task InitWebAsync()
    {
        try
        {
            // 창이 가려지거나 알림 영역으로 숨어도 페이지가 멈추지 않게(자동화를 숨기는 설정이 아니라 백그라운드 절전 끄기)
            var opts = new CoreWebView2EnvironmentOptions("--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows");
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "webview2"), opts);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                if (e.IsUserInitiated) OpenHttps(e.Uri);
            };
            webReady = true;
            Log("준비됨 · " + settings.Watches.Count(w => w.Available) + "개 감시 · " + settings.IntervalMin + "분마다"
                + (settings.Running ? "" : " · 지금은 멈춤(▶ 시작을 누르세요)"));
        }
        catch (Exception ex)
        {
            webFailed = true;
            RefreshList();
            Log("WebView2 를 열 수 없습니다: " + ex.Message);
            MessageBox.Show(this, "WebView2 런타임이 필요합니다.\r\nhttps://developer.microsoft.com/microsoft-edge/webview2/ 에서 'Evergreen Bootstrapper' 를 설치하세요.\r\n\r\n" + ex.Message, "좌석 감시");
        }
    }

    static void OpenHttps(string address)
    {
        Uri uri;
        if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(uri.AbsoluteUri); } catch (Exception ex) { MessageBox.Show(ex.Message, "링크 열기"); }
    }

    void UpdateRouteDay()
    {
        routeDay = DateTime.Today;
        foreach (var w in settings.Watches)
        {
            w.EvaluateRoute(routeDay);
            if (!w.Available && activeWatch == w && activeCheck != null) activeCheck.Cancel();
        }
    }

    async Task OnTick()
    {
        if (routeDay != DateTime.Today) { UpdateRouteDay(); RefreshList(); SaveSettings(); }
        if (busy) { nextLabel.Text = settings.Running ? "조회 중…" : "멈추는 중…"; return; }
        if (!settings.Running) { nextLabel.Text = "멈춤"; return; }
        if (!settings.Watches.Any(w => w.Available)) { nextLabel.Text = "감시 중인 노선 없음"; return; }
        if (!webReady) { nextLabel.Text = webFailed ? "조회 화면을 열 수 없습니다." : "조회 화면 준비 중…"; return; }
        var left = nextRun - DateTime.Now;
        nextLabel.Text = left.TotalSeconds > 0 ? "다음 확인 " + nextRun.ToString("HH:mm:ss") : "";
        if (left.TotalSeconds > 0) return;

        busy = true;
        bool finished = false;
        try
        {
            // 조회 도중 켜거나 추가하거나 조건을 바꾼 노선도 이번 차례에 이어서 조회한다(목록을 미리 복사해 두지 않는다).
            var done = new HashSet<Watch>();
            while (true)
            {
                if (exiting || !settings.Running) return;
                var w = settings.Watches.FirstOrDefault(x => x.Available && !done.Contains(x));
                if (w == null) break;
                done.Add(w);
                using (var check = new CancellationTokenSource())
                {
                    activeWatch = w;
                    activeCheck = check;
                    w.Unconfirmed = "조회 중…";
                    RefreshList();
                    var timing = new CheckTiming();
                    string failure = null;
                    try { await CheckOne(w, check.Token, timing); }
                    catch (OperationCanceledException)
                    {
                        if (!check.IsCancellationRequested) { failure = "확인 못 함(조회 오류)"; throw; }
                    }
                    catch (Exception ex)
                    {
                        if (!check.IsCancellationRequested)
                        {
                            timing.Stop();
                            w.LastCheck = DateTime.Now.ToString("HH:mm:ss");
                            w.Unconfirmed = "확인 못 함(조회 오류)";
                            Log(LabelOf(w.Url) + " · " + ex.Message + " · " + timing.Label);
                        }
                    }
                    finally
                    {
                        activeWatch = null;
                        activeCheck = null;
                        RecordCheck(w, timing, check.IsCancellationRequested, failure);
                    }
                    if (check.IsCancellationRequested) continue;
                }
                RefreshList();
                SaveSettings();
            }
            finished = true;
        }
        catch (Exception ex)
        {
            Log("오류: " + ex.Message);
        }
        finally
        {
            busy = false;
            // 이미 조회한 노선을 이번 차례 중에 다시 켜면 '조회 대기'로 남는다. 그런 노선이 있으면 3분을 기다리지 않는다.
            bool waiting = finished && settings.Running && settings.Watches.Any(x => x.Available && x.Unconfirmed == Watch.Waiting);
            nextRun = waiting ? DateTime.Now : DateTime.Now.AddMinutes(Math.Max(MinInterval, settings.IntervalMin));
            if (!exiting) UpdateCheckAvailability();
        }
    }

    class ScanResult
    {
        public Dictionary<string, Card> All = new Dictionary<string, Card>();
        public bool None, Filtered, Complete, PageBoundary;
        public int HiddenSchedules;
        public string BodyHead = "";
    }

    async Task CheckOne(Watch w, CancellationToken token, CheckTiming timing)
    {
        // 다른 노선은 전송 중에도 조회한다. 같은 노선의 재조회에서만 이전 전송 결과를 기다린다.
        if (w.Delivery != null)
        {
            var previous = w.Delivery;
            w.Delivery = null;
            await previous;
            token.ThrowIfCancellationRequested();
        }
        var ranges = RangesOf(w.Url);
        var airlines = new HashSet<string>(FlightSearch.AirlinesOf(w.Url), StringComparer.OrdinalIgnoreCase);
        var label = LabelOf(w.Url);
        var search = FlightSearch.Read(w.Url, true);
        bool international = search != null && search.International;
        ScanResult r = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            timing.Attempts = attempt;
            r = await CollectOnce(FlightSearch.PageOf(w.Url), international, ranges, airlines, token, timing);
            token.ThrowIfCancellationRequested();
            if (r.None || r.All.Count == 0 || r.Filtered || attempt == 3) break;
            Log(label + " · 사이트 검색 조건이 결과와 다름 → 다시 열기 " + (attempt + 1) + "/3");
            await Task.Delay(3000, token);
        }
        // 여기까지가 네이버 화면을 기다린 시간이다. 아래 정리·알림 준비는 몇 ms라 빼고 잰다.
        timing.Stop();
        if (routeDay != DateTime.Today) { UpdateRouteDay(); token.ThrowIfCancellationRequested(); }
        w.LastCheck = DateTime.Now.ToString("HH:mm:ss");
        var took = " · " + timing.Label;

        if (r.None && r.All.Count == 0)
        {
            w.Unconfirmed = null;
            w.Seats.Clear();
            w.OverPrice = 0;
            w.HiddenSchedules = 0;
            w.Observe(new List<Card>(), true, settings);
            Log(label + " · " + RangeLabel(w.Url) + " 출발 좌석 없음" + took);
            return;
        }
        if (r.All.Count == 0)
        {
            w.Unconfirmed = "확인 못 함(로딩)";
            Log(label + " · 목록을 못 읽음(로딩 실패?) → 이번 조회는 건너뜀" + took);
            File.WriteAllText(Path.Combine(DataDir, "last-page.txt"), r.BodyHead, Encoding.UTF8);
            return;
        }
        if ((!r.Complete && !r.PageBoundary) || r.None || !r.Filtered)
        {
            w.Unconfirmed = r.Filtered ? "확인 못 함(목록 미완료)" : "확인 못 함(거르기)";
            Log(label + " · " + w.Unconfirmed + " → 이전 좌석 목록 유지" + took);
            return;
        }

        // 금액은 사이트 거르기 확인(r.Filtered)과 상관없이 여기서만 거른다. 가격이 내려 기준 안으로 들어온 좌석은 새 좌석으로 알린다.
        var matching = r.All.Values.Where(c => MatchesWatch(c, ranges, airlines, search)).ToList();
        var hits = matching.Where(c => w.Allows(c.Price))
            .OrderBy(c => c.Dep, StringComparer.Ordinal).ThenBy(c => c.Price).ToList();
        w.OverPrice = matching.Count - hits.Count;
        w.HiddenSchedules = r.HiddenSchedules;
        var newOnes = w.Observe(hits, r.Complete, settings);
        w.Seats = hits.Select(c => c.Dep + "→" + c.Arr + " " + c.Airline + " " + c.Cls + " " + c.Price.ToString("N0") + "원").ToList();
        w.Unconfirmed = r.Complete ? null : Watch.Partial;
        Log(label + " · " + RangeLabel(w.Url) + (r.Complete ? " 출발 " : " 출발 · 일부 확인 ") + (hits.Count > 0 ? hits.Count + "개 좌석" + took + "\r\n    " + string.Join("\r\n    ", w.Seats) : "확인된 좌석 0개" + took) +
            (w.OverPrice > 0 ? "\r\n    (" + Watch.PriceLabel(w.MaxPrice) + " 조건으로 " + w.OverPrice + "개 제외)" : "") +
            (r.HiddenSchedules > 0 ? "\r\n    ('동일가 다른 일정'으로 접힌 " + r.HiddenSchedules + "편은 확인하지 않음)" : ""));

        if (newOnes.Count > 0)
        {
            var msg = FareMessage(label, newOnes);
            DesktopAlert("좌석이 생겼어요!", msg);
            Log("★ 알림: " + msg);
        }
        if (w.PendingNtfy.Count > 0 || w.PendingTelegram.Count > 0)
        {
            SaveSettings();
            w.Delivery = Task.WhenAll(Deliver(w, hits, false), Deliver(w, hits, true));
        }
    }

    // 정체나 시도 횟수 제한은 조회를 끝내는 조건일 뿐, 목록 완료의 근거가 아니다.
    // 단계별 시간(이동·첫 카드 대기·스크롤)은 시간 초과나 오류로 끝나도 그때까지 쓴 만큼 더한다.
    // url 은 WebView2 로 여는 네이버 주소(국제선은 앱 전용 조건을 뺀 주소). 국제선은 사이트가 거르지 않으므로
    // 거르기 확인(Filtered)을 하지 않고, 목록이 한 번에 뜨므로 스크롤하지 않는다.
    async Task<ScanResult> CollectOnce(string url, bool international, List<string[]> ranges, HashSet<string> airlines,
        CancellationToken token, CheckTiming timing)
    {
        var r = new ScanResult();
        var stage = Stopwatch.StartNew();
        try { await NavigateAsync(url, token); }
        finally { timing.NavMs += stage.ElapsedMilliseconds; }

        // 첫 카드나 매진 문구가 뜰 때까지 최대 30초
        stage.Restart();
        try
        {
            for (int i = 0; i < 30 && r.All.Count == 0 && !r.None; i++)
            {
                await Task.Delay(1000, token);
                await Scan(r, url, token);
            }
        }
        finally { timing.WaitMs += stage.ElapsedMilliseconds; }
        if (r.None || r.All.Count == 0) return r;

        r.Filtered = international || r.All.Values.All(c => MatchesWatch(c, ranges, airlines));
        if (!r.Filtered || r.Complete || international) return r;

        int still = 0;
        stage.Restart();
        try
        {
            for (int i = 0; i < 80 && still < 8; i++)
            {
                int before = r.All.Count;
                var moved = await web.CoreWebView2.ExecuteScriptAsync(parseJs + ";SeatScan.scrollDown()");
                await Task.Delay(1200, token);
                await Scan(r, url, token);
                r.Filtered = r.All.Values.All(c => MatchesWatch(c, ranges, airlines));
                if (r.Complete || r.None || !r.Filtered) break;
                still = r.All.Count == before && moved != "true" ? still + 1 : 0;
            }
        }
        finally { timing.ScrollMs += stage.ElapsedMilliseconds; }
        return r;
    }

    async Task Scan(ScanResult r, string url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var raw = await web.CoreWebView2.ExecuteScriptAsync(parseJs + ";SeatScan.scanPage()");
        token.ThrowIfCancellationRequested();
        var obj = json.DeserializeObject(raw) as Dictionary<string, object>;
        if (obj == null || !SamePage(url, Convert.ToString(obj["url"])))
            throw new InvalidOperationException("조회 중 검색 페이지가 변경되었습니다.");
        r.PageBoundary = false;
        if (!Equals(obj["ready"], "complete")) { r.Complete = false; return; }
        r.Complete = Equals(obj["complete"], true);
        r.PageBoundary = Equals(obj["pageBoundary"], true);
        if (obj.ContainsKey("none") && obj["none"] is bool && (bool)obj["none"]) r.None = true;
        if (obj.ContainsKey("bodyHead")) r.BodyHead = Convert.ToString(obj["bodyHead"]);
        r.HiddenSchedules = obj.ContainsKey("hiddenSchedules") ? Convert.ToInt32(obj["hiddenSchedules"]) : 0;
        var cards = obj.ContainsKey("cards") ? obj["cards"] as object[] : null;
        if (cards == null) return;
        // 확정 목록은 같은 화면에서 읽은 행만 사용한다. 앞선 로딩 단계의 행은 버린다.
        if (r.Complete || r.PageBoundary) r.All.Clear();
        foreach (var o in cards)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) continue;
            var c = new Card
            {
                Airline = Convert.ToString(d["airline"]),
                AirlineCode = d.ContainsKey("airlineCode") ? Convert.ToString(d["airlineCode"]) : null,
                Dep = Convert.ToString(d["dep"]),
                Arr = Convert.ToString(d["arr"]),
                Cls = Convert.ToString(d["cls"]),
                Price = Convert.ToInt64(d["price"]),
                International = d.ContainsKey("intl") && Equals(d["intl"], true),
                AirlineCodes = d.ContainsKey("airlineCodes") && d["airlineCodes"] is object[]
                    ? ((object[])d["airlineCodes"]).Select(Convert.ToString).ToArray() : null,
                Stops = d.ContainsKey("stops") && d["stops"] != null ? Convert.ToInt32(d["stops"]) : -1,
                Minutes = d.ContainsKey("minutes") && d["minutes"] != null ? Convert.ToInt32(d["minutes"]) : 0
            };
            Card old;
            if (!r.All.TryGetValue(c.Key, out old) || old.Price > c.Price) r.All[c.Key] = c;
        }
    }

    static bool SamePage(string expected, string actual)
    {
        Uri a, b;
        return Uri.TryCreate(expected, UriKind.Absolute, out a) && Uri.TryCreate(actual, UriKind.Absolute, out b)
            && Uri.Compare(a, b, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.Ordinal) == 0;
    }

    async Task NavigateAsync(string url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var core = web.CoreWebView2;
        var completed = new TaskCompletionSource<bool>();
        ulong? navigationId = null;
        EventHandler<CoreWebView2NavigationStartingEventArgs> starting = (s, e) =>
        {
            if (!navigationId.HasValue && !e.IsRedirected && SamePage(url, e.Uri)) navigationId = e.NavigationId;
        };
        EventHandler<CoreWebView2NavigationCompletedEventArgs> done = (s, e) =>
        {
            if (navigationId == e.NavigationId) completed.TrySetResult(e.IsSuccess);
        };
        core.NavigationStarting += starting;
        core.NavigationCompleted += done;
        try
        {
            core.Navigate(url);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var winner = await Task.WhenAny(completed.Task, Task.Delay(45000, deadline.Token));
                deadline.Cancel();
                token.ThrowIfCancellationRequested();
                if (winner != completed.Task) throw new TimeoutException("페이지 이동 시간 초과(45초)");
                if (!await completed.Task || !SamePage(url, core.Source))
                    throw new InvalidOperationException("요청한 검색 페이지로 이동하지 못했습니다.");
            }
        }
        finally
        {
            core.NavigationStarting -= starting;
            core.NavigationCompleted -= done;
        }
    }

    // ───────────── 알림 ─────────────
    // 휴대폰 알림 내용. 항공사가 하나면 알림을 누를 때 그 항공사 홈페이지를 열고 네이버는 버튼으로 둔다.
    // 여러 곳이면 알림은 네이버(비교용)를 열고 항공사 홈페이지 버튼을 ntfy 한도인 3개까지 붙인다.
    internal static Dictionary<string, object> NtfyMessage(string topic, string title, string msg, string naverUrl, IList<Airline> airlines)
    {
        string naver = naverUrl ?? "https://flight.naver.com/";
        var actions = new List<Dictionary<string, object>>();
        string click = naver;
        if (airlines.Count == 1)
        {
            click = airlines[0].Site;
            actions.Add(ViewAction("네이버에서 보기", naver));
        }
        else
            foreach (var airline in airlines.Take(3)) actions.Add(ViewAction(airline.Name, airline.Site));
        var message = new Dictionary<string, object> {
            { "topic", topic }, { "title", title }, { "message", msg }, { "priority", 5 },
            { "tags", new[] { "airplane" } }, { "click", click }
        };
        if (actions.Count > 0) message.Add("actions", actions);
        return message;
    }

    static Dictionary<string, object> ViewAction(string label, string url)
    {
        return new Dictionary<string, object> { { "action", "view" }, { "label", label }, { "url", url }, { "clear", true } };
    }

    // 텔레그램은 알림을 누르면 늘 봇 대화방이 열리므로, 항공사 홈페이지와 네이버는 메시지 아래 버튼으로 붙인다.
    internal static Dictionary<string, object> TelegramMessage(string chat, string title, string msg, string naverUrl, IList<Airline> airlines)
    {
        var rows = airlines.Take(4).Select(a => new object[] { UrlButton(a.Name + " 홈페이지", a.Site) }).ToList();
        rows.Add(new object[] { UrlButton("네이버에서 보기", naverUrl ?? "https://flight.naver.com/") });
        return new Dictionary<string, object> {
            { "chat_id", chat }, { "text", "<b>" + HtmlText(title) + "</b>\n" + HtmlText(msg) }, { "parse_mode", "HTML" },
            { "reply_markup", new Dictionary<string, object> { { "inline_keyboard", rows } } }
        };
    }

    static Dictionary<string, object> UrlButton(string text, string url)
    {
        return new Dictionary<string, object> { { "text", text }, { "url", url } };
    }

    static string HtmlText(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    internal static bool IsTelegramToken(string token)
    {
        return Regex.IsMatch(token ?? "", @"^\d+:[A-Za-z0-9_-]+$");
    }

    internal static string TelegramApi(string token, string method)
    {
        return "https://api.telegram.org/bot" + token + "/" + method;
    }

    // 텔레그램 오류 응답을 알아듣기 쉬운 말로 바꾼다.
    internal static string TelegramProblem(int status, string response)
    {
        response = response ?? "";
        if (status == 401 || status == 404) return "봇 토큰이 틀렸습니다";
        if (status == 403) return "봇이 차단되었거나 봇 대화방에서 아직 Start를 누르지 않았습니다";
        if (status == 400 && response.Contains("chat not found")) return "채팅 ID를 찾을 수 없습니다. 봇 대화방에서 Start를 눌렀는지 확인해 주세요";
        return null;
    }

    // getUpdates 응답에서 가장 최근에 봇에게 말을 건 대화방의 { ID, 이름 }. 없으면 null.
    internal static string[] LatestTelegramChat(string response)
    {
        var root = new JavaScriptSerializer().DeserializeObject(response) as Dictionary<string, object>;
        object updates;
        if (root == null || !root.TryGetValue("result", out updates) || !(updates is object[])) return null;
        foreach (var update in ((object[])updates).Reverse().OfType<Dictionary<string, object>>())
            foreach (var kind in new[] { "message", "my_chat_member", "edited_message" })
            {
                object part, chat, id;
                if (!update.TryGetValue(kind, out part) || !(part is Dictionary<string, object>) ||
                    !((Dictionary<string, object>)part).TryGetValue("chat", out chat) || !(chat is Dictionary<string, object>)) continue;
                var info = (Dictionary<string, object>)chat;
                if (!info.TryGetValue("id", out id)) continue;
                object name;
                if (!info.TryGetValue("first_name", out name) && !info.TryGetValue("title", out name)) name = "";
                return new[] { Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture), Convert.ToString(name) };
            }
        return null;
    }

    static string FareMessage(string label, IList<Card> seats)
    {
        return label + " · " + string.Join(", ", seats.Take(5).Select(c => c.FareText)) +
            (seats.Count > 5 ? " 외 " + (seats.Count - 5) + "개" : "");
    }

    // 같은 조회에서 실제로 다시 읽은 좌석만 보낸다. 다른 채널의 성공 여부와 독립적이다.
    async Task Deliver(Watch w, List<Card> hits, bool telegram)
    {
        var pending = telegram ? w.PendingTelegram : w.PendingNtfy;
        var seats = hits.Where(c => pending.ContainsKey(c.Key)).ToList();
        if (seats.Count == 0) return;
        var channel = telegram ? "텔레그램" : "ntfy";
        var problem = await SendMobile(telegram, settings, "좌석이 생겼어요!", FareMessage(LabelOf(w.Url), seats),
            FlightSearch.PageOf(w.Url), Airline.WithSites(seats));
        if (exiting || !settings.Watches.Contains(w) || pending != (telegram ? w.PendingTelegram : w.PendingNtfy)) return;
        bool exhausted = Watch.FinishDelivery(pending, seats, problem == null);
        Log(problem == null ? channel + " 알림 전송 완료" : "휴대폰 알림 실패 · " + problem +
            (exhausted ? " · 3회 실패한 좌석 재시도 종료" : " · 다음 조회에서 좌석 재확인 후 재시도"));
        SaveSettings();
    }

    Task<string> SendMobile(bool telegram, Settings target, string title, string msg, string url, IList<Airline> airlines)
    {
        if (!telegram) return Post("ntfy", "https://ntfy.sh/", json.Serialize(NtfyMessage(target.NtfyTopic, title, msg, url, airlines)));
        if (!IsTelegramToken(target.TelegramToken)) return Task.FromResult("텔레그램: 봇 토큰 형식이 아닙니다");
        return Post("텔레그램", TelegramApi(target.TelegramToken, "sendMessage"),
            json.Serialize(TelegramMessage(target.TelegramChat, title, msg, url, airlines)));
    }

    void DesktopAlert(string title, string msg)
    {
        tray.ShowBalloonTip(15000, title, msg, ToolTipIcon.Info);
        SystemSounds.Exclamation.Play();
    }

    // 설정 화면에서 보내는 시험 알림은 재전송 목록에 넣지 않는다.
    // 끝나면 실패한 곳의 이유를 돌려준다(모두 성공이면 빈 배열). 실패는 조회 기록에도 남긴다.
    Task<string[]> Notify(string title, string msg, string naverUrl, IList<Airline> airlines, Settings target = null)
    {
        DesktopAlert(title, msg);
        var to = target ?? settings;
        var sends = new List<Task<string>>();
        // 한글 제목은 HTTP 머리글에 못 넣어서 JSON 으로 보낸다
        if (to.UsesNtfy()) sends.Add(SendMobile(false, to, title, msg, naverUrl, airlines));
        if (to.UsesTelegram()) sends.Add(SendMobile(true, to, title, msg, naverUrl, airlines));
        var done = Task.WhenAll(sends).ContinueWith(t => t.Result.Where(r => r != null).ToArray());
        done.ContinueWith(t =>
        {
            if (t.Result.Length > 0 && IsHandleCreated)
                BeginInvoke(new Action(() => { foreach (var problem in t.Result) Log("휴대폰 알림 실패 · " + problem); }));
        });
        return done;
    }

    sealed class MobileClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = (HttpWebRequest)base.GetWebRequest(address);
            request.Timeout = request.ReadWriteTimeout = 8000;
            return request;
        }
    }

    // JSON 을 보내고 실패하면 "곳: 이유"를 돌려준다.
    static Task<string> Post(string channel, string url, string body)
    {
        return Task.Run(() =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new MobileClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    wc.UploadString(url, body);
                }
                return null;
            }
            catch (WebException ex)
            {
                return channel + ": " + (WebProblem(channel, ex) ?? ex.Message);
            }
            catch (Exception ex)
            {
                return channel + ": " + ex.Message;
            }
        });
    }

    internal static string WebProblem(string channel, WebException ex)
    {
        var response = ex.Response as HttpWebResponse;
        if (response == null || channel != "텔레그램") return null;
        string text = "";
        try { using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) text = reader.ReadToEnd(); }
        catch { }
        return TelegramProblem((int)response.StatusCode, text);
    }

    // 봇 대화방에서 Start를 누른 사람의 채팅 ID를 찾는다. 텔레그램은 봇에게 온 메시지를 24시간만 보관한다.
    internal static string[] FindTelegramChat(string token)
    {
        if (!IsTelegramToken(token)) throw new InvalidOperationException("봇 토큰 형식이 아닙니다. BotFather가 보낸 123456789:AA… 모양의 토큰을 넣어 주세요.");
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new MobileClient())
            {
                wc.Encoding = Encoding.UTF8;
                return LatestTelegramChat(wc.DownloadString(TelegramApi(token, "getUpdates")));
            }
        }
        catch (WebException ex)
        {
            throw new InvalidOperationException(WebProblem("텔레그램", ex) ?? ex.Message);
        }
    }

    // ───────────── 기록·설정 ─────────────
    void Log(string line)
    {
        var text = "[" + DateTime.Now.ToString("MM-dd HH:mm:ss") + "] " + line;
        CheckHistory.RotateIfLarge(LogPath, CheckHistory.MaxFileBytes);
        try { File.AppendAllText(LogPath, text + "\r\n", Encoding.UTF8); } catch { }
        if (logBox.TextLength > 60000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 30000);
        logBox.AppendText(text + "\r\n");
    }

    // 조회 하나가 끝날 때마다(오류·취소 포함) checks.jsonl 에 남기고 조회 기록 표에 올린다.
    void RecordCheck(Watch w, CheckTiming timing, bool cancelled, string failure)
    {
        timing.Stop();
        bool ok = !cancelled && failure == null && w.Unconfirmed == null;
        string result = cancelled ? "취소됨" : failure ?? w.DisplayStatus(true);
        if (!cancelled)
        {
            w.LastDuration = timing.Label;
            w.UpdateHealth(ok, DateTime.Now);
        }
        var record = CheckHistory.Create(DateTime.Now, LabelOf(w.Url), RangeLabel(w.Url), timing, result, ok, cancelled,
            ok || (!cancelled && w.Unconfirmed == Watch.Partial) ? w.Seats.Count : 0);
        CheckHistory.Append(ChecksPath, record, json);
        AddCheckRecord(record);
    }

    bool SaveSettings(Settings proposed = null)
    {
        try
        {
            store.Save(proposed ?? settings);
            saveWarning = false;
            return true;
        }
        catch (Exception ex)
        {
            if (!saveWarning)
            {
                saveWarning = true;
                Log("설정 저장 실패 · " + ex.Message);
                MessageBox.Show(this, "변경 사항을 저장하지 못했습니다. 폴더의 쓰기 권한과 여유 공간을 확인해 주세요.\n" + ex.Message, "설정 저장 실패");
            }
            return false;
        }
    }
}

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        bool created;
        using (var mutex = new System.Threading.Mutex(true, "SeatWatch.SingleInstance", out created))
        {
            if (!created) { MessageBox.Show("좌석 감시가 이미 실행 중입니다. 알림 영역 아이콘을 확인하세요.", "좌석 감시"); return; }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm()); }
            catch (IOException ex) { MessageBox.Show(ex.Message, "좌석 감시 · 파일 오류"); }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(ex.Message, "좌석 감시 · 폴더 접근 오류"); }
            catch (CryptographicException ex) { MessageBox.Show(ex.Message, "좌석 감시 · 토큰 보호 오류"); }
        }
    }
}
