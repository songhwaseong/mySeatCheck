using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

public partial class MainForm
{
    Watch selectedWatch;
    TableLayoutPanel dashboardLayout;
    Panel deck;
    Panel dashboard, seatsPanel;
    Label heading, detailStatus, emptySeats, checksSummary;
    Button emptyDeck, openRoute, editRoute, alertRoute, copyRoute, watchToggle, removeRoute, liveButton, logButton, checkButton;
    Button domesticTab, internationalTab;
    DataGridView fares, checksTable;
    TableLayoutPanel historyView;
    // 조회 기록 표와 요약에 쓰는 최근 기록(오래된 순). 파일에는 더 많이 남아 있다.
    readonly List<CheckRecord> checkRecords = new List<CheckRecord>();
    const int HistoryRows = 500, SummaryWindow = 20;
    const long SlowCheckMs = 10000;

    static Button ActionButton(string caption, Color background, Color foreground, EventHandler action)
    {
        var button = new Button { Text = caption, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = background, ForeColor = foreground,
            Padding = new Padding(10, 5, 10, 5), Margin = new Padding(4, 0, 0, 0), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = background == TicketStyle.Forest ? Color.FromArgb(91, 127, 105) : TicketStyle.Line;
        button.Click += action;
        return button;
    }

    // 윈도우 기본 아이콘 글꼴. 윈도우 11은 Segoe Fluent Icons, 10은 Segoe MDL2 Assets(같은 코드 포인트).
    static readonly string IconFont = new System.Drawing.Text.InstalledFontCollection().Families
        .Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    static readonly ToolTip ButtonTips = new ToolTip { InitialDelay = 300 };

    internal static class Glyph
    {
        internal const string Play = "", Pause = "", Settings = "", Add = "",
            Refresh = "", OpenExternal = "", Edit = "", Watch = "", Unwatch = "",
            Delete = "", Globe = "", History = "", Back = "", Swap = "", Bell = "",
            Help = "", Copy = "", Shuffle = "";
        internal const string Search = "";
    }

    // 글자 대신 아이콘만 보이는 버튼. 원래 문구는 툴팁과 화면 낭독기 이름으로 남긴다.
    static Button IconButton(string glyph, string label, Color background, Color foreground, EventHandler action)
    {
        var button = ActionButton("", background, foreground, action);
        button.Font = new Font(IconFont, 11f);
        button.Padding = new Padding(8, 5, 8, 5);
        SetIcon(button, glyph, label);
        return button;
    }

    static void SetIcon(Button button, string glyph, string label)
    {
        button.Text = glyph;
        button.AccessibleName = label;
        ButtonTips.SetToolTip(button, label);
    }

    static Label Caption(string text, float size, Color color, bool bold)
    {
        return new Label { Text = text, AutoEllipsis = true, UseMnemonic = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, ForeColor = color,
            Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    }

    static FlowLayoutPanel ButtonRow()
    {
        return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
    }

    void CheckNow()
    {
        if (!settings.Running) SetRunning(true);
        else foreach (var w in settings.Watches) w.RunSoon();
    }

    void BuildUi()
    {
        BackColor = TicketStyle.Paper;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = TicketStyle.Forest,
            Padding = new Padding(22, 0, 18, 0), ColumnCount = 3, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = Caption("✈  SeatWatch", 20, Color.White, true);
        brand.AutoSize = true;
        brand.Anchor = AnchorStyles.Left;
        brand.Dock = DockStyle.None;
        header.Controls.Add(brand, 0, 0);
        // 탑승권 목록을 국내선·국제선으로 나눠 보는 탭. 감시와 알림은 탭과 상관없이 모두 계속한다.
        var kinds = ButtonRow();
        kinds.Anchor = AnchorStyles.Left;
        kinds.Margin = new Padding(24, 0, 0, 0);
        domesticTab = ActionButton("국내선", TicketStyle.Forest, Color.White, (s, e) => ShowRouteKind(false));
        internationalTab = ActionButton("국제선", TicketStyle.Forest, Color.White, (s, e) => ShowRouteKind(true));
        kinds.Controls.Add(domesticTab);
        kinds.Controls.Add(internationalTab);
        header.Controls.Add(kinds, 1, 0);
        var commands = ButtonRow();
        runBtn = IconButton(Glyph.Pause, "", TicketStyle.Orange, Color.White, (s, e) => SetRunning(!settings.Running));
        commands.Controls.Add(runBtn);
        commands.Controls.Add(IconButton(Glyph.Settings, "공통 알림 설정 · 모든 노선 기본값", TicketStyle.Forest, Color.White, (s, e) => ShowSettings()));
        commands.Controls.Add(IconButton(Glyph.Add, "감시 추가", TicketStyle.Forest, Color.White, (s, e) => ShowWatchDialog(null)));
        checkButton = IconButton(Glyph.Refresh, "지금 확인", TicketStyle.Face, TicketStyle.Forest, (s, e) => CheckNow());
        commands.Controls.Add(checkButton);
        header.Controls.Add(commands, 2, 0);
        layout.Controls.Add(header, 0, 0);

        var workspace = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        layout.Controls.Add(workspace, 0, 1);
        // 조회용 WebView2는 항상 같은 부모와 크기를 유지한다. 앞에 놓인 패널만 바꾼다.
        web = new WebView2 { Dock = DockStyle.Fill, TabStop = false };
        logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = TicketStyle.Face, ForeColor = TicketStyle.Forest, BorderStyle = BorderStyle.None, Margin = Padding.Empty };
        historyView = BuildHistoryView();
        dashboard = new Panel { BackColor = TicketStyle.Paper, Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 8) };
        workspace.Controls.Add(web);
        workspace.Controls.Add(historyView);
        workspace.Controls.Add(dashboard);
        dashboard.BringToFront();

        dashboardLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        dashboardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dashboardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
        dashboardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dashboard.Controls.Add(dashboardLayout);
        var deckHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        deck = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty,
            AccessibleName = "감시 노선 탑승권" };
        emptyDeck = ActionButton("＋  첫 감시 노선 추가", TicketStyle.Face, TicketStyle.Forest, (s, e) => ShowWatchDialog(null));
        emptyDeck.Dock = DockStyle.Fill;
        deckHost.Controls.Add(deck);
        deckHost.Controls.Add(emptyDeck);
        dashboardLayout.Controls.Add(deckHost, 0, 0);

        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = TicketStyle.Face,
            Margin = Padding.Empty, Padding = new Padding(18, 8, 18, 10) };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        dashboardLayout.Controls.Add(detail, 0, 1);
        var detailHead = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        detailHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading = Caption("", 13, TicketStyle.Forest, true);
        detailHead.Controls.Add(heading, 0, 0);
        openRoute = IconButton(Glyph.OpenExternal, "네이버 화면 열기", TicketStyle.Face, TicketStyle.Forest, (s, e) => OpenSelectedRoute());
        openRoute.Anchor = AnchorStyles.Right;
        detailHead.Controls.Add(openRoute, 1, 0);
        detail.Controls.Add(detailHead, 0, 0);

        seatsPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        fares = BuildFareTable();
        emptySeats = Caption("", 12, TicketStyle.Muted, false);
        emptySeats.Dock = DockStyle.None;
        emptySeats.TextAlign = ContentAlignment.MiddleCenter;
        emptySeats.BackColor = TicketStyle.Face;
        seatsPanel.Controls.Add(fares);
        seatsPanel.Controls.Add(emptySeats);
        seatsPanel.Resize += (s, e) => PositionEmptyMessage();
        detail.Controls.Add(seatsPanel, 0, 1);

        var detailFoot = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        detailFoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailFoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detailStatus = Caption("", 9, TicketStyle.Muted, false);
        detailFoot.Controls.Add(detailStatus, 0, 0);
        var routeButtons = ButtonRow();
        editRoute = IconButton(Glyph.Edit, "조건 수정", TicketStyle.Face, TicketStyle.Forest, (s, e) => ShowWatchDialog(selectedWatch));
        routeButtons.Controls.Add(editRoute);
        alertRoute = IconButton(Glyph.Bell, "이 노선의 알림·조회 간격", TicketStyle.Face, TicketStyle.Forest, (s, e) => ShowRouteAlerts());
        routeButtons.Controls.Add(alertRoute);
        copyRoute = IconButton(Glyph.Copy, "다른 날짜로 노선 복제", TicketStyle.Face, TicketStyle.Forest, (s, e) => CopySelected());
        routeButtons.Controls.Add(copyRoute);
        watchToggle = IconButton(Glyph.Unwatch, "노선 감시 끄기", TicketStyle.Face, TicketStyle.Forest, (s, e) => ToggleSelected());
        removeRoute = IconButton(Glyph.Delete, "노선 삭제", TicketStyle.Face, TicketStyle.Muted, (s, e) => DeleteSelected());
        routeButtons.Controls.Add(watchToggle);
        routeButtons.Controls.Add(removeRoute);
        detailFoot.Controls.Add(routeButtons, 1, 0);
        detail.Controls.Add(detailFoot, 0, 2);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty,
            Padding = new Padding(20, 0, 18, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nextLabel = Caption("", 10, TicketStyle.Muted, false);
        footer.Controls.Add(nextLabel, 0, 0);
        var views = ButtonRow();
        liveButton = IconButton(Glyph.Globe, "조회 화면", TicketStyle.Face, TicketStyle.Forest, (s, e) => ShowView(web.TabStop ? dashboard : (Control)web));
        logButton = IconButton(Glyph.History, "조회 기록", TicketStyle.Face, TicketStyle.Forest, (s, e) => ShowView(historyView.Visible ? dashboard : (Control)historyView));
        views.Controls.Add(liveButton);
        views.Controls.Add(logButton);
        footer.Controls.Add(views, 1, 0);
        layout.Controls.Add(footer, 0, 2);
        dashboardLayout.SizeChanged += (s, e) => SizeTickets();
    }

    void ShowView(Control view)
    {
        dashboard.Visible = view == dashboard;
        historyView.Visible = view == historyView;
        web.TabStop = view == web;
        view.BringToFront();
        if (view == web) SetIcon(liveButton, Glyph.Back, "탑승권 목록으로");
        else SetIcon(liveButton, Glyph.Globe, "조회 화면");
        if (view == historyView)
        {
            SetIcon(logButton, Glyph.Back, "탑승권 목록으로");
            // 원문은 가장 최근 줄이 보이도록 연다(시작할 때 채운 글은 맨 위에 머물러 있다).
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }
        else SetIcon(logButton, Glyph.History, "조회 기록");
    }

    // ───────────── 조회 기록 화면 ─────────────
    // 위: 최근 조회 요약 · 가운데: 조회마다 한 줄(최근 것이 위) · 아래: log.txt 원문
    TableLayoutPanel BuildHistoryView()
    {
        var view = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = TicketStyle.Face,
            Margin = Padding.Empty, Padding = new Padding(18, 8, 18, 10), Visible = false };
        view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        view.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        view.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        view.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        view.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        checksSummary = Caption("", 11, TicketStyle.Forest, true);
        view.Controls.Add(checksSummary, 0, 0);
        checksTable = BuildChecksTable();
        view.Controls.Add(checksTable, 0, 1);
        var logCaption = Caption("조회 기록 원문 · app\\data\\log.txt", 9, TicketStyle.Muted, false);
        logCaption.TextAlign = ContentAlignment.BottomLeft;
        view.Controls.Add(logCaption, 0, 2);
        view.Controls.Add(logBox, 0, 3);
        return view;
    }

    DataGridView BuildChecksTable()
    {
        var table = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = TicketStyle.Face, BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            RowHeadersVisible = false, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = TicketStyle.Line, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, Margin = Padding.Empty,
            AccessibleName = "조회 소요 시간 기록" };
        table.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = TicketStyle.Forest,
            ForeColor = Color.White, SelectionBackColor = TicketStyle.Forest, SelectionForeColor = Color.White,
            Padding = new Padding(8, 0, 6, 0), Font = new Font(Font, FontStyle.Bold) };
        table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        table.ColumnHeadersHeight = 32;
        table.RowTemplate.Height = 30;
        table.DefaultCellStyle = new DataGridViewCellStyle { BackColor = TicketStyle.Face, ForeColor = TicketStyle.Forest,
            SelectionBackColor = Color.FromArgb(239, 233, 211), SelectionForeColor = TicketStyle.Forest,
            Font = new Font("맑은 고딕", 10f), Padding = new Padding(8, 0, 6, 0) };
        var columns = new[] { "시각", "노선 · 시간대", "소요", "단계(초)", "시도", "결과" };
        var weights = new[] { 95, 150, 55, 165, 40, 140 };
        for (int i = 0; i < columns.Length; i++)
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = columns[i], FillWeight = weights[i],
                SortMode = DataGridViewColumnSortMode.NotSortable });
        var took = table.Columns[2];
        took.DefaultCellStyle.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
        took.DefaultCellStyle.Alignment = took.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        table.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        table.Columns[3].DefaultCellStyle.ForeColor = TicketStyle.Muted;
        return table;
    }

    // 시작할 때 이전 실행의 기록을 다시 채운다.
    void LoadHistory()
    {
        logBox.Text = CheckHistory.ReadTextTail(LogPath, 30000);
        checkRecords.AddRange(CheckHistory.ReadTail(ChecksPath, HistoryRows, json));
        foreach (var record in checkRecords) InsertCheckRow(record);
        checksSummary.Text = CheckHistory.Summary(checkRecords, SummaryWindow);
    }

    void AddCheckRecord(CheckRecord record)
    {
        checkRecords.Add(record);
        if (checkRecords.Count > HistoryRows) checkRecords.RemoveAt(0);
        InsertCheckRow(record);
        while (checksTable.Rows.Count > HistoryRows) checksTable.Rows.RemoveAt(checksTable.Rows.Count - 1);
        checksSummary.Text = CheckHistory.Summary(checkRecords, SummaryWindow);
    }

    void InsertCheckRow(CheckRecord r)
    {
        string time = r.Time.Length >= 19 ? r.Time.Substring(5) : r.Time; // MM-dd HH:mm:ss
        checksTable.Rows.Insert(0, time, r.Route + (r.Range.Length > 0 ? " · " + r.Range : ""),
            CheckHistory.Seconds(r.TotalMs), CheckHistory.Stages(r), r.Attempts > 0 ? r.Attempts.ToString() : "", r.Result);
        var row = checksTable.Rows[0];
        if (r.TotalMs >= SlowCheckMs) row.Cells[2].Style.ForeColor = TicketStyle.Orange;
        if (!r.Ok) row.Cells[5].Style.ForeColor = r.Cancelled ? TicketStyle.Muted : TicketStyle.Orange;
    }

    DataGridView BuildFareTable()
    {
        var table = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = TicketStyle.Face, BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            RowHeadersVisible = false, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = TicketStyle.Line, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AccessibleName = "선택한 노선 운임표" };
        table.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = TicketStyle.Forest,
            ForeColor = Color.White, SelectionBackColor = TicketStyle.Forest, SelectionForeColor = Color.White,
            Padding = new Padding(12, 0, 8, 0), Font = new Font(Font, FontStyle.Bold) };
        table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        table.ColumnHeadersHeight = 39;
        table.RowTemplate.Height = 52;
        table.DefaultCellStyle = new DataGridViewCellStyle { BackColor = TicketStyle.Face, ForeColor = TicketStyle.Forest,
            SelectionBackColor = Color.FromArgb(239, 233, 211), SelectionForeColor = TicketStyle.Forest,
            Font = new Font("맑은 고딕", 12f), Padding = new Padding(12, 0, 8, 0) };
        foreach (var name in new[] { "항공사 · 좌석 등급", "출발", "도착", "운임" })
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, SortMode = DataGridViewColumnSortMode.NotSortable });
        table.Columns[0].FillWeight = 180;
        var fare = table.Columns[3];
        fare.DefaultCellStyle.ForeColor = TicketStyle.Orange;
        fare.DefaultCellStyle.Font = new Font("맑은 고딕", 14f, FontStyle.Bold);
        fare.DefaultCellStyle.Alignment = fare.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        table.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var link = table.Rows[e.RowIndex].Cells[0] as DataGridViewLinkCell;
            if (link == null) return;
            try { Process.Start((string)link.Tag); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "항공사 홈페이지 열기"); }
        };
        return table;
    }

    void PositionEmptyMessage()
    {
        int top = fares.ColumnHeadersHeight + 2;
        emptySeats.SetBounds(0, top, seatsPanel.ClientSize.Width, Math.Max(0, seatsPanel.ClientSize.Height - top));
    }

    void UpdateCheckAvailability()
    {
        bool anyEnabled = settings.Watches.Any(w => w.Available);
        checkButton.Enabled = trayCheckItem.Enabled = anyEnabled && !webFailed && !busy;
        if (!anyEnabled) nextLabel.Text = "감시 중인 노선 없음";
    }

    List<Watch> VisibleWatches()
    {
        return settings.Watches.Where(w => FlightSearch.IsInternational(w.Url) == settings.InternationalTab).ToList();
    }

    void ShowRouteKind(bool international)
    {
        ShowView(dashboard);
        if (settings.InternationalTab == international) return;
        settings.InternationalTab = international;
        SaveSettings();
        RefreshList();
    }

    // 선택한 탭은 밝은 바탕, 나머지는 머리 띠와 같은 색. 탭 이름 옆에 노선 수를 붙인다.
    void UpdateTabs()
    {
        int international = settings.Watches.Count(w => FlightSearch.IsInternational(w.Url));
        StyleTab(domesticTab, "국내선", settings.Watches.Count - international, !settings.InternationalTab);
        StyleTab(internationalTab, "국제선", international, settings.InternationalTab);
        emptyDeck.Text = settings.InternationalTab ? "＋  첫 국제선 감시 노선 추가" : "＋  첫 국내선 감시 노선 추가";
    }

    static void StyleTab(Button tab, string name, int count, bool selected)
    {
        tab.Text = count > 0 ? name + "  " + count : name;
        tab.AccessibleName = name + " 탭, 노선 " + count + "개" + (selected ? ", 선택됨" : "");
        tab.BackColor = selected ? TicketStyle.Face : TicketStyle.Forest;
        tab.ForeColor = selected ? TicketStyle.Forest : Color.White;
        tab.Font = new Font("맑은 고딕", 10f, selected ? FontStyle.Bold : FontStyle.Regular);
        tab.FlatAppearance.BorderColor = selected ? TicketStyle.Face : Color.FromArgb(91, 127, 105);
    }

    void RefreshList()
    {
        UpdateCheckAvailability();
        UpdateTabs();
        var visible = VisibleWatches();
        if (!visible.Contains(selectedWatch)) selectedWatch = visible.FirstOrDefault();
        deck.SuspendLayout();
        foreach (var card in deck.Controls.OfType<TicketCard>().ToArray())
            if (!visible.Contains(card.Watch)) card.Dispose();
        foreach (var watch in visible)
        {
            var card = deck.Controls.OfType<TicketCard>().FirstOrDefault(t => t.Watch == watch);
            if (card == null)
            {
                var condition = FlightSearch.ConditionOf(watch.Url);
                card = new TicketCard(watch, RangeLabel(watch.Url) +
                    (condition.Length > 0 ? "  ·  " + condition : "") +
                    (watch.MaxPrice > 0 ? "  ·  " + Watch.PriceLabel(watch.MaxPrice) : ""));
                card.Click += (s, e) => SelectTicket((TicketCard)s);
                card.DoubleClick += (s, e) =>
                {
                    if (!webReady || busy || FlightSearch.Read(((TicketCard)s).Watch.Url, true) == null) return;
                    SelectTicket((TicketCard)s);
                    web.CoreWebView2.Navigate(FlightSearch.PageOf(selectedWatch.Url));
                    ShowView(web);
                };
                deck.Controls.Add(card);
                deck.Controls.SetChildIndex(card, Math.Min(visible.IndexOf(watch), deck.Controls.Count - 1));
            }
            card.UpdateState(watch == selectedWatch, settings.Running);
        }
        SizeTickets();
        deck.ResumeLayout();
        emptyDeck.Visible = selectedWatch == null;
        if (emptyDeck.Visible) emptyDeck.BringToFront();
        RefreshDetails();
    }

    void SelectTicket(TicketCard card)
    {
        selectedWatch = card.Watch;
        foreach (TicketCard item in deck.Controls) item.UpdateState(item == card, settings.Running);
        deck.ScrollControlIntoView(card);
        RefreshDetails();
    }

    void SizeTickets()
    {
        int width = dashboardLayout.ClientSize.Width;
        if (width <= 0) return;

        float scale = CurrentAutoScaleDimensions.Width / 96f;
        int gap = (int)Math.Ceiling(10 * scale);
        int minWidth = (int)Math.Ceiling(280 * scale);
        int count = deck.Controls.Count;
        int height = (int)Math.Round((count >= 3 ? 144 : 196) * scale);
        int rowHeight = height + gap;
        int columns = Math.Min(Math.Max(1, count), Math.Max(1, (width + gap) / (minWidth + gap)));
        int rows = Math.Max(1, (count + columns - 1) / columns);
        int availableRows = Math.Max(1, (dashboardLayout.ClientSize.Height - (int)Math.Ceiling(215 * scale)) / rowHeight);
        int visibleRows = Math.Min(2, availableRows);
        if (rows > visibleRows)
        {
            // 스크롤바가 생길 때도 마지막 카드가 가로로 넘치지 않도록 폭을 먼저 확보한다.
            width = Math.Max(1, width - SystemInformation.VerticalScrollBarWidth);
            columns = Math.Min(columns, Math.Max(1, (width + gap) / (minWidth + gap)));
            rows = (count + columns - 1) / columns;
        }

        int viewportHeight = Math.Min(rows, visibleRows) * rowHeight;
        var rowStyle = dashboardLayout.RowStyles[0];
        if (rowStyle.Height != viewportHeight) rowStyle.Height = viewportHeight;
        int cardWidth = Math.Max(1, (width - (columns - 1) * gap) / columns);
        if (count == 1) cardWidth = Math.Min(width, Math.Max(minWidth, width * 54 / 100));

        deck.SuspendLayout();
        var origin = deck.AutoScrollPosition;
        for (int i = 0; i < count; i++)
            deck.Controls[i].SetBounds(origin.X + i % columns * (cardWidth + gap),
                origin.Y + i / columns * rowHeight, cardWidth, height);
        deck.AutoScrollMinSize = new Size(0, rows * rowHeight);
        deck.ResumeLayout();
    }

    // 기존 settings.json에 저장된 문자열의 항공사·등급을 함께 표시해 공백이 들어간 이름도 유지한다.
    static string[] FareColumns(string seat)
    {
        // 국제선 도착 시각에는 날짜가 바뀐 만큼 +1 이 붙는다(09:30+1 → 09:30 (+1일)).
        var parts = Regex.Match(seat ?? "", @"^(\d{2}:\d{2})→(\d{2}:\d{2})(?:\+(\d))?\s+(.+)\s+(\d[\d.,\s]*원)$");
        return parts.Success ? new[] { parts.Groups[4].Value, parts.Groups[1].Value,
                parts.Groups[2].Value + (parts.Groups[3].Success ? " (+" + parts.Groups[3].Value + "일)" : ""), parts.Groups[5].Value }
            : new[] { seat ?? "", "", "", "" };
    }

    // 저장된 운임은 "항공사 좌석등급" 형식이다. 이름 중간의 공백은 허용하고 단어 경계에서만 일치시킨다.
    // 국제선의 "아시아나항공, 일본항공 직항"은 쉼표를 빼고 첫 항공사로 찾는다.
    static string AirlineSite(string label)
    {
        string name = "";
        foreach (string word in label.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            name += word.TrimEnd(',');
            var airline = Airline.FindByName(name);
            if (airline != null) return airline.Site;
        }
        return null;
    }

    void AddFareRow(string seat)
    {
        var columns = FareColumns(seat);
        var row = fares.Rows[fares.Rows.Add(columns)];
        var site = columns[1].Length > 0 ? AirlineSite(columns[0]) : null;
        if (site == null) return;
        row.Cells[0] = new DataGridViewLinkCell {
            Value = columns[0], Tag = site, ToolTipText = "항공사 공식 홈페이지 열기 · " + new Uri(site).Host,
            LinkColor = TicketStyle.Forest, ActiveLinkColor = TicketStyle.Orange,
            TrackVisitedState = false, LinkBehavior = LinkBehavior.AlwaysUnderline
        };
    }

    void RefreshDetails()
    {
        var w = selectedWatch;
        editRoute.Enabled = alertRoute.Enabled = removeRoute.Enabled = w != null;
        openRoute.Enabled = copyRoute.Enabled = w != null && FlightSearch.Read(w.Url, true) != null;
        watchToggle.Enabled = w != null && w.InactiveReason == null;
        fares.Rows.Clear();
        fares.Columns[0].HeaderText = w != null && FlightSearch.IsInternational(w.Url) ? "항공사 · 직항/경유" : "항공사 · 좌석 등급";
        if (w == null)
        {
            heading.Text = "항공편 운임";
            detailStatus.Text = "";
            emptySeats.Text = "감시할 노선을 추가하면 항공편이 여기에 표시됩니다.";
        }
        else
        {
            var ticket = deck.Controls.OfType<TicketCard>().First(c => c.Watch == w);
            heading.Text = string.Join("   ·   ", new[] { ticket.Route.CitiesLabel, ticket.Route.DateLabel, ticket.Route.TimeLabel }
                .Where(t => !string.IsNullOrEmpty(t)));
            var status = w.DisplayStatus(settings.Running);
            bool uncertain = status.StartsWith("확인 못 함");
            detailStatus.Text = status + (string.IsNullOrEmpty(w.LastCheck) ? "" : "   ·   마지막 조회 시도 " + w.LastCheck +
                (string.IsNullOrEmpty(w.LastDuration) ? "" : " (" + w.LastDuration + " 걸림)")) +
                (w.HiddenSchedules > 0 && w.Unconfirmed == null ? "   ·   접힌 동일가 일정 " + w.HiddenSchedules + "편 미확인" : "");
            detailStatus.Text += "\n마지막 정상 확인 " + (string.IsNullOrEmpty(w.LastSuccess) ? "기록 없음" : w.LastSuccess) +
                (w.FailureStreak > 0 ? " · 연속 전체 확인 실패 " + w.FailureStreak + "회" : "") +
                "   ·   " + w.AlertSummary(settings);
            ButtonTips.SetToolTip(detailStatus, detailStatus.Text);
            detailStatus.ForeColor = uncertain || w.FailureStreak > 0 ? TicketStyle.Orange : TicketStyle.Muted;
            if (w.Enabled) SetIcon(watchToggle, Glyph.Unwatch, "노선 감시 끄기");
            else SetIcon(watchToggle, Glyph.Watch, "노선 감시 켜기");
            foreach (string seat in w.CurrentFares(settings.Running)) AddFareRow(seat);
            emptySeats.Text = w.InactiveReason != null ? w.InactiveReason : !w.Enabled ? "노선 감시를 켜면 현재 운임을 조회합니다." :
                status == "좌석 없음" ? "이 시간대에 조회된 좌석이 없습니다." :
                status.StartsWith("금액 초과") ?Watch.PriceLabel(w.MaxPrice) + "인 좌석이 없습니다. 조회된 좌석 " + w.OverPrice + "개는 모두 더 비쌉니다."
                    : status;
        }
        fares.ClearSelection();
        emptySeats.Visible = fares.Rows.Count == 0;
        if (emptySeats.Visible) { PositionEmptyMessage(); emptySeats.BringToFront(); }
    }

    void ToggleSelected()
    {
        var w = selectedWatch;
        if (w == null || w.InactiveReason != null) return;
        w.Enabled = !w.Enabled;
        w.Unconfirmed = Watch.Waiting;
        if (w.Enabled) w.RunSoon(); // 다시 켠 이 노선만 바로 조회한다
        if (!w.Enabled && activeWatch == w) activeCheck.Cancel();
        SaveSettings();
        RefreshList();
    }

    void OpenSelectedRoute()
    {
        if (selectedWatch == null) return;
        OpenHttps(FlightSearch.PageOf(selectedWatch.Url));
    }

    void CopySelected()
    {
        var source = selectedWatch;
        if (source == null) return;
        var search = FlightSearch.Read(source.Url, true);
        if (search == null) return;
        using (var dialog = Dialog("다른 날짜로 노선 복제", 195))
        {
            var date = new DateTimePicker { MinDate = DateTime.Today, AccessibleName = "새 출발 날짜",
                Format = DateTimePickerFormat.Long, Dock = DockStyle.Top };
            date.Value = search.Date >= date.MinDate && search.Date < date.MaxDate.Date ? search.Date.AddDays(1) : DateTime.Today;
            var first = new CheckBox { Text = "첫 조회에서 발견한 좌석도 알림", Dock = DockStyle.Top, Checked = source.NotifyFirst };
            var add = ActionButton("새 노선 추가", TicketStyle.Forest, Color.White, (s, e) =>
            {
                if (SaveWatch(dialog, search.WithDate(date.Value), null, source.MaxPrice, first.Checked, source))
                    dialog.DialogResult = DialogResult.OK;
            });
            add.Dock = DockStyle.Bottom;
            dialog.Controls.Add(first);
            dialog.Controls.Add(date);
            var caption = FieldLabel("새 출발 날짜 · 기존 노선의 검색 조건, 최대 금액, 알림을 복사합니다.");
            caption.Dock = DockStyle.Top;
            dialog.Controls.Add(caption);
            dialog.Controls.Add(add);
            dialog.AcceptButton = add;
            dialog.ShowDialog(this);
        }
    }

    // 노선별 알림·조회 간격. 감시 조건(주소)과 따로 저장하므로 바꿔도 이전 좌석 목록과 알림 비교는 그대로 남는다.
    void ShowRouteAlerts()
    {
        var w = selectedWatch;
        if (w == null) return;
        using (var dialog = Dialog("노선 알림 · " + LabelOf(w.Url), 360))
        {
            var form = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 3 };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 4; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            int common = Math.Max(MinInterval, Math.Min(60, settings.IntervalMin));

            var ownInterval = new CheckBox { Text = "조회 간격 따로", Checked = w.IntervalMin > 0, AutoSize = true, Anchor = AnchorStyles.Left };
            var interval = new NumericUpDown { Minimum = MinInterval, Maximum = 60, Width = 60, AccessibleName = "이 노선의 조회 간격(분)",
                Value = w.IntervalMin > 0 ? Math.Max(MinInterval, Math.Min(60, w.IntervalMin)) : common, Enabled = w.IntervalMin > 0 };
            ownInterval.CheckedChanged += (s, e) => { interval.Enabled = ownInterval.Checked; if (ownInterval.Checked) interval.Focus(); };
            var intervalRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            intervalRow.Controls.Add(interval);
            intervalRow.Controls.Add(new Label { Text = "분마다 · 끄면 공통 " + common + "분", AutoSize = true, ForeColor = TicketStyle.Muted,
                Margin = new Padding(3, 5, 0, 0) });

            var pc = new CheckBox { Text = "PC 알림", Checked = w.AlertPc, AutoSize = true, Anchor = AnchorStyles.Left };
            var pcHint = new Label { Text = "알림 영역 풍선과 소리", AutoSize = true, ForeColor = TicketStyle.Muted, Anchor = AnchorStyles.Left };
            var ntfy = new CheckBox { Text = "ntfy", Checked = w.AlertNtfy, AutoSize = true, Anchor = AnchorStyles.Left };
            var topic = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = w.NtfyTopic,
                AccessibleName = "이 노선의 ntfy 주제, 비우면 공통 주제" };
            var telegram = new CheckBox { Text = "텔레그램", Checked = w.AlertTelegram, AutoSize = true, Anchor = AnchorStyles.Left };
            var chat = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = w.TelegramChat,
                AccessibleName = "이 노선의 텔레그램 채팅 ID, 비우면 공통 채팅 ID" };
            // 다른 사람이 같은 봇에서 Start 를 누르면 그 사람의 채팅 ID를 찾을 수 있다. 봇 토큰은 공통 설정의 것.
            var token = new TextBox { Text = settings.TelegramToken };
            dialog.Disposed += (s, e) => token.Dispose();
            Button find = null;
            find = IconButton(Glyph.Search, "채팅 ID 찾기 · 받을 사람이 봇에서 Start 를 누른 뒤", TicketStyle.Face, TicketStyle.Forest,
                (s, e) => LookUpTelegramChat(dialog, find, token, chat));
            Action applyOn = () =>
            {
                topic.Enabled = ntfy.Checked;
                chat.Enabled = telegram.Checked;
                find.Enabled = telegram.Checked && IsTelegramToken(settings.TelegramToken);
            };
            ntfy.CheckedChanged += (s, e) => applyOn();
            telegram.CheckedChanged += (s, e) => applyOn();
            applyOn();

            var rows = new Control[][] {
                new Control[] { ownInterval, intervalRow, null }, new Control[] { pc, pcHint, null },
                new Control[] { ntfy, topic, null }, new Control[] { telegram, chat, find } };
            for (int i = 0; i < rows.Length; i++)
            {
                form.Controls.Add(rows[i][0], 0, i);
                form.Controls.Add(rows[i][1], 1, i);
                if (rows[i][2] == null) continue;
                rows[i][2].Anchor = AnchorStyles.Left;
                form.Controls.Add(rows[i][2], 2, i);
            }

            // 공통 설정에서 꺼 두었거나 비어 있는 채널은 여기서 켜도 보내지 않는다는 것을 알려 준다.
            var notes = new List<string> { "주제·채팅 ID를 비우면 공통 알림 설정의 값으로 보냅니다." };
            if (!settings.NtfyOn) notes.Add("ntfy는 공통 알림 설정에서 꺼져 있어 보내지 않습니다.");
            if (!settings.TelegramOn) notes.Add("텔레그램은 공통 알림 설정에서 꺼져 있어 보내지 않습니다.");
            else if (!IsTelegramToken(settings.TelegramToken)) notes.Add("텔레그램 봇 토큰은 공통 알림 설정에서 먼저 넣어 주세요.");
            var hint = new Label { Text = string.Join("\n", notes), Dock = DockStyle.Fill, ForeColor = TicketStyle.Muted,
                UseMnemonic = false, Padding = new Padding(0, 8, 0, 0) };
            form.Controls.Add(hint, 0, 4);
            form.SetColumnSpan(hint, 3);

            Func<Watch> entered = () => new Watch { AlertPc = pc.Checked, AlertNtfy = ntfy.Checked, AlertTelegram = telegram.Checked,
                NtfyTopic = topic.Text.Trim(), TelegramChat = chat.Text.Trim(), IntervalMin = ownInterval.Checked ? (int)interval.Value : 0 };
            var buttons = ButtonRow();
            Button test = null;
            test = IconButton(Glyph.Bell, "이 노선 설정으로 시험 알림 보내기", TicketStyle.Face, TicketStyle.Forest, (s, e) =>
            {
                var value = entered();
                var target = value.AlertTarget(settings);
                if (!value.AlertPc && !target.UsesNtfy() && !target.UsesTelegram())
                {
                    MessageBox.Show(dialog, "켜 둔 알림이 없어 이 노선은 화면과 조회 기록에만 표시됩니다.", "시험 알림");
                    return;
                }
                test.Enabled = false;
                var msg = "알림 시험 · " + LabelOf(w.Url) + " · 진에어 18:05 할인석";
                var page = FlightSearch.PageOf(w.Url);
                var airlines = new[] { Airline.FindByCode("LJ") };
                if (value.AlertPc) DesktopAlert("좌석이 생겼어요!", msg);
                var sends = new List<Task<string>>();
                if (target.UsesNtfy()) sends.Add(SendMobile(false, target, "좌석이 생겼어요!", msg, page, airlines));
                if (target.UsesTelegram()) sends.Add(SendMobile(true, target, "좌석이 생겼어요!", msg, page, airlines));
                Task.WhenAll(sends).ContinueWith(t =>
                {
                    var problems = t.Result.Where(r => r != null).ToArray();
                    if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                    dialog.BeginInvoke(new Action(() =>
                    {
                        if (dialog.IsDisposed) return;
                        test.Enabled = true;
                        if (problems.Length > 0) MessageBox.Show(dialog, "보내지 못한 곳이 있습니다.\n\n" + string.Join("\n", problems), "시험 알림");
                    }));
                });
            });
            var save = ActionButton("저장", TicketStyle.Forest, Color.White, (s, e) =>
            {
                var value = entered();
                if (value.TelegramChat.Length > 0 && !Regex.IsMatch(value.TelegramChat, @"^(-?\d+|@\w+)$"))
                {
                    MessageBox.Show(dialog, "텔레그램 채팅 ID는 숫자(그룹은 -로 시작)나 @채널이름입니다.", dialog.Text);
                    return;
                }
                if (!settings.Watches.Contains(w)) { dialog.DialogResult = DialogResult.Cancel; return; }
                var old = new Watch();
                old.CopyAlertsFrom(w);
                var oldNtfy = w.PendingNtfy;
                var oldTelegram = w.PendingTelegram;
                var before = w.AlertTarget(settings);
                w.CopyAlertsFrom(value);
                w.ResetPending(before, w.AlertTarget(settings));
                if (!SaveSettings())
                {
                    w.CopyAlertsFrom(old);
                    w.PendingNtfy = oldNtfy;
                    w.PendingTelegram = oldTelegram;
                    return;
                }
                Log("노선 알림 변경: " + LabelOf(w.Url) + " · " + w.AlertSummary(settings));
                dialog.DialogResult = DialogResult.OK;
            });
            var cancel = ActionButton("취소", TicketStyle.Face, TicketStyle.Muted, (s, e) => dialog.DialogResult = DialogResult.Cancel);
            buttons.Controls.Add(test);
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            form.Controls.Add(buttons, 0, 5);
            form.SetColumnSpan(buttons, 3);
            dialog.Controls.Add(form);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            if (dialog.ShowDialog(this) == DialogResult.OK) RefreshDetails();
        }
    }

    Form Dialog(string title, int height)
    {
        return new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, Font = Font,
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = TicketStyle.Paper, ClientSize = new Size(520, height), Padding = new Padding(18) };
    }

    // 국제선 입력 제안: 공항과 '도쿄 전체' 같은 도시(모든 공항 검색)
    static IEnumerable<KeyValuePair<string, string>> InternationalPlaces
    {
        get { return TicketRoute.Airports.Concat(TicketRoute.CityGroups); }
    }

    // 국내선은 목록에서만 고른다. 국제선은 목록에 없는 공항도 세 글자 코드로 직접 입력할 수 있다.
    static ComboBox AirportBox(string name, string selected, bool international)
    {
        var box = new ComboBox { DropDownStyle = international ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill, AccessibleName = name, DisplayMember = "Value", ValueMember = "Key" };
        box.DataSource = (international ? InternationalPlaces : TicketRoute.Cities).OrderBy(p => p.Value, StringComparer.Create(
            System.Globalization.CultureInfo.GetCultureInfo("ko-KR"), false))
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value + " (" + p.Key + ")")).ToArray();
        box.BindingContext = new BindingContext();
        if (international)
        {
            box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            box.AutoCompleteSource = AutoCompleteSource.ListItems;
            if (InternationalPlaces.Any(p => p.Key == selected)) box.SelectedValue = selected;
            else { box.SelectedIndex = -1; box.Text = selected; }
        }
        else box.SelectedValue = selected;
        return box;
    }

    // "도쿄 나리타 (NRT)", "nrt", "방콕", "도쿄 전체"처럼 입력한 글자에서 코드를 꺼낸다. 못 찾으면 입력 그대로(검사는 FlightSearch).
    internal static string AirportCode(string text)
    {
        text = (text ?? "").Trim();
        var m = Regex.Match(text, @"\(([A-Za-z]{3})\)\s*$");
        if (!m.Success) m = Regex.Match(text, @"^([A-Za-z]{3})$");
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        var named = InternationalPlaces.FirstOrDefault(p => p.Value == text);
        return named.Key ?? text;
    }

    static Button AirportSwapButton(ComboBox from, ComboBox to)
    {
        var swap = IconButton(Glyph.Swap, "출발지와 도착지 교환", TicketStyle.Face, TicketStyle.Forest, (s, e) =>
        {
            if (from.DropDownStyle == ComboBoxStyle.DropDown)
            {
                var text = from.Text;
                from.Text = to.Text;
                to.Text = text;
                return;
            }
            var departure = from.SelectedValue;
            from.SelectedValue = to.SelectedValue;
            to.SelectedValue = departure;
        });
        swap.Padding = new Padding(6, 0, 6, 0);
        swap.Margin = new Padding(4, 0, 4, 0);
        swap.Anchor = AnchorStyles.None;
        return swap;
    }

    static Button AirlinePicker(ContextMenuStrip menu, HashSet<string> selected, bool international)
    {
        var names = (international ? Airline.Domestic.Concat(Airline.Foreign) : Airline.Domestic)
            .ToDictionary(a => a.Code, a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var code in selected)
            if (!names.ContainsKey(code)) names.Add(code, code);
        var button = ActionButton("", TicketStyle.Face, TicketStyle.Forest, (s, e) =>
        {
            var picker = (Button)s;
            menu.Show(picker, new Point(0, picker.Height));
        });
        button.Anchor = AnchorStyles.Left;
        var all = new ToolStripMenuItem("전체 항공사");
        menu.Items.Add(all);
        menu.Items.Add(new ToolStripSeparator());
        var options = names.OrderBy(pair => pair.Value).Select(pair => new ToolStripMenuItem(pair.Value)
            { Tag = pair.Key, CheckOnClick = true, Checked = selected.Contains(pair.Key) }).ToArray();
        menu.Items.AddRange(options);
        Action update = () =>
        {
            all.Checked = selected.Count == 0;
            var caption = all.Checked ? "전체" : selected.Count == 1 ? names[selected.First()] : selected.Count + "개 선택";
            button.Text = caption + " ▾";
            button.AccessibleName = "항공사 선택, " + caption;
        };
        foreach (var option in options)
        {
            option.CheckedChanged += (s, e) =>
            {
                var item = (ToolStripMenuItem)s;
                var code = (string)item.Tag;
                if (item.Checked) selected.Add(code);
                else selected.Remove(code);
                update();
            };
        }
        all.Click += (s, e) => { foreach (var option in options) option.Checked = false; };
        menu.Closing += (s, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
        update();
        return button;
    }

    void ShowWatchDialog(Watch previous)
    {
        var saved = previous == null ? null : FlightSearch.Read(previous.Url);
        // 새 노선은 지금 보고 있는 탭의 종류로, 수정은 저장된 주소의 종류로 연다. 주소 붙여넣기는 두 종류 모두 받는다.
        bool international = previous != null ? FlightSearch.IsInternational(previous.Url) : settings.InternationalTab;
        string kind = international ? "국제선" : "국내선";
        using (var dialog = Dialog(previous == null ? kind + " 감시 노선 추가" : kind + " 감시 조건 수정", international ? 600 : 540))
        using (var airlineMenu = new ContextMenuStrip())
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var conditions = new TabPage("조건 선택") { BackColor = TicketStyle.Face, Padding = new Padding(14) };
            // 복합 여정이나 직접 해석할 수 없는 필터는 원본 주소로 편집한다.
            bool canEditFields = previous == null || saved != null;
            if (canEditFields) tabs.TabPages.Add(conditions);
            var raw = new TabPage("주소 붙여넣기") { BackColor = TicketStyle.Face, Padding = new Padding(14) };
            tabs.TabPages.Add(raw);
            var from = AirportBox("출발지", saved == null ? (international ? "ICN" : "CJU") : saved.From, international);
            var to = AirportBox("도착지", saved == null ? (international ? "NRT" : "GMP") : saved.To, international);
            // 국제선 조건. 새 노선은 카드 할인을 뺀 '모든 결제수단' 가격으로 본다(카드 조건 가격이 3~4만 원 싸게 보임).
            var trip = saved != null ? saved.Trip : new TripOptions { NoCardBenefit = true };
            var stops = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "직항/경유" };
            stops.Items.AddRange(new object[] { "직항·경유 모두", "직항만", "경유 1회까지", "경유 2회까지" });
            stops.SelectedIndex = trip.MaxStops + 1;
            var limitHours = new CheckBox { Text = "소요시간", AutoSize = true, Checked = trip.MaxHours > 0,
                ForeColor = TicketStyle.Forest, Margin = new Padding(3, 5, 4, 0) };
            var maxHours = new NumericUpDown { Minimum = 1, Maximum = 99, Width = 56, TextAlign = HorizontalAlignment.Right,
                AccessibleName = "최대 소요시간(시간)", Enabled = trip.MaxHours > 0, Value = trip.MaxHours > 0 ? trip.MaxHours : 10 };
            limitHours.CheckedChanged += (s, e) => { maxHours.Enabled = limitHours.Checked; if (limitHours.Checked) maxHours.Focus(); };
            var noCard = new CheckBox { Text = "카드 할인 빼고 보기", AutoSize = true, Checked = trip.NoCardBenefit,
                ForeColor = TicketStyle.Forest, Margin = new Padding(24, 5, 0, 0) };
            ButtonTips.SetToolTip(noCard, "특정 카드로만 받는 할인을 빼고, 어떤 결제수단으로도 낼 수 있는 가격으로 비교합니다.");
            var tripRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 0) };
            tripRow.Controls.Add(limitHours);
            tripRow.Controls.Add(maxHours);
            tripRow.Controls.Add(new Label { Text = "시간 이내", AutoSize = true, ForeColor = TicketStyle.Muted, Margin = new Padding(3, 5, 0, 0) });
            tripRow.Controls.Add(noCard);
            var date = new DateTimePicker { AccessibleName = "출발 날짜", Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy년 MM월 dd일", Dock = DockStyle.Fill };
            date.Value = saved == null ? DateTime.Today : saved.Date;
            date.MinDate = date.Value.Date < DateTime.Today ? date.Value.Date : DateTime.Today;

            var airlineCodes = new HashSet<string>(saved == null ? new string[0] : saved.Airlines, StringComparer.OrdinalIgnoreCase);
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 9 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            fields.Controls.Add(FieldLabel("출발지"), 0, 0);
            fields.Controls.Add(FieldLabel("도착지"), 2, 0);
            fields.Controls.Add(from, 0, 1);
            fields.Controls.Add(AirportSwapButton(from, to), 1, 1);
            fields.Controls.Add(to, 2, 1);
            fields.Controls.Add(FieldLabel("출발 날짜"), 0, 2);
            fields.Controls.Add(date, 0, 3);
            if (international)
            {
                fields.Controls.Add(FieldLabel("직항/경유"), 2, 2);
                fields.Controls.Add(stops, 2, 3);
            }
            var timeLabel = FieldLabel("출발 시간대 · 복수 선택 가능, 선택하지 않으면 하루 전체");
            fields.Controls.Add(timeLabel, 0, 4);
            fields.SetColumnSpan(timeLabel, 3);
            var times = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            for (int i = 0; i < 3; i++) times.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            for (int i = 0; i < FlightSearch.TimeSlots.Length; i++)
            {
                string slot = FlightSearch.TimeSlots[i];
                times.Controls.Add(new CheckBox { Text = slot.Replace("-", " – "), Tag = slot, AutoSize = true,
                    Checked = saved != null && saved.Times.Contains(slot), Margin = new Padding(3, 6, 2, 4) }, i % 3, i / 3);
            }
            fields.Controls.Add(times, 0, 5);
            fields.SetColumnSpan(times, 3);
            fields.Controls.Add(FieldLabel("항공사 · 복수 선택 가능"), 0, 6);
            var airlineButton = AirlinePicker(airlineMenu, airlineCodes, international);
            fields.Controls.Add(airlineButton, 0, 7);
            fields.SetColumnSpan(airlineButton, 3);
            if (international)
            {
                fields.Controls.Add(tripRow, 0, 8);
                fields.SetColumnSpan(tripRow, 3);
            }
            conditions.Controls.Add(fields);
            if (!canEditFields) conditions.Dispose();

            var title = Caption("네이버 항공권 국내선·국제선 편도 검색 주소", 12, TicketStyle.Forest, true);
            title.Dock = DockStyle.Top;
            title.Height = 35;
            var address = new TextBox { Dock = DockStyle.Top, AccessibleName = title.Text, Text = previous == null ? "" : previous.Url };
            raw.Controls.Add(address);
            raw.Controls.Add(title);
            // 금액은 네이버 주소에 없는 조건이라 두 탭 모두에 적용되도록 탭 바깥에 둔다.
            var priceRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false,
                Padding = new Padding(0, 10, 0, 0) };
            bool hadLimit = previous != null && previous.MaxPrice > 0;
            var limitPrice = new CheckBox { Text = "최대 금액", AutoSize = true, Checked = hadLimit,
                ForeColor = TicketStyle.Forest, Margin = new Padding(3, 5, 4, 0) };
            var maxPrice = new NumericUpDown { Minimum = 1000, Maximum = 10000000, Increment = 10000, ThousandsSeparator = true,
                Width = 110, TextAlign = HorizontalAlignment.Right, AccessibleName = "최대 금액(원)", Enabled = hadLimit,
                Value = hadLimit ? Math.Min(10000000, Math.Max(1000, previous.MaxPrice)) : 100000 };
            var priceHint = new Label { AutoSize = true, ForeColor = TicketStyle.Muted, Margin = new Padding(3, 5, 0, 0) };
            Action updateHint = () => priceHint.Text = !international ? "원 이하 좌석만 표시하고 알림 · 성인 1명 편도 운임"
                : noCard.Checked ? "원 이하 좌석만 표시하고 알림 · 모든 결제수단 운임" : "원 이하 좌석만 표시하고 알림 · 카드 조건 운임";
            noCard.CheckedChanged += (s, e) => updateHint();
            updateHint();
            limitPrice.CheckedChanged += (s, e) => { maxPrice.Enabled = limitPrice.Checked; if (limitPrice.Checked) maxPrice.Focus(); };
            priceRow.Controls.Add(limitPrice);
            priceRow.Controls.Add(maxPrice);
            priceRow.Controls.Add(priceHint);
            var first = new CheckBox { Text = "첫 조회에서 발견한 좌석도 알림", Dock = DockStyle.Bottom,
                AutoSize = true, Checked = previous != null && previous.NotifyFirst };
            var buttons = ButtonRow();
            buttons.Dock = DockStyle.Bottom;
            buttons.Padding = new Padding(0, 10, 0, 0);
            var save = ActionButton(previous == null ? "감시 추가" : "변경 저장", TicketStyle.Forest, Color.White, (s, e) =>
            {
                string url = address.Text;
                if (tabs.SelectedTab == conditions)
                {
                    try
                    {
                        var slots = times.Controls.OfType<CheckBox>().Where(c => c.Checked).Select(c => (string)c.Tag);
                        url = international
                            ? FlightSearch.BuildInternational(AirportCode(from.Text), AirportCode(to.Text), date.Value, slots,
                                new TripOptions { MaxStops = stops.SelectedIndex - 1, MaxHours = limitHours.Checked ? (int)maxHours.Value : 0,
                                    NoCardBenefit = noCard.Checked }, saved, airlineCodes)
                            : FlightSearch.Build((string)from.SelectedValue, (string)to.SelectedValue, date.Value, slots, saved, airlineCodes);
                    }
                    catch (ArgumentException ex) { MessageBox.Show(dialog, ex.Message, dialog.Text); return; }
                }
                if (SaveWatch(dialog, url, previous, limitPrice.Checked ? (long)maxPrice.Value : 0, first.Checked)) dialog.DialogResult = DialogResult.OK;
            });
            var cancel = ActionButton("취소", TicketStyle.Face, TicketStyle.Muted, (s, e) => dialog.DialogResult = DialogResult.Cancel);
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            dialog.Controls.Add(tabs);
            dialog.Controls.Add(first);
            dialog.Controls.Add(priceRow);
            dialog.Controls.Add(buttons);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            dialog.Shown += (s, e) => { if (canEditFields) from.Focus(); else address.Focus(); };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                ShowView(dashboard);
                var ticket = deck.Controls.OfType<TicketCard>().First(c => c.Watch == selectedWatch);
                deck.ScrollControlIntoView(ticket);
                ticket.Focus();
            }
        }
    }

    static Label FieldLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, ForeColor = TicketStyle.Forest, Margin = new Padding(3, 9, 0, 4) };
    }

    // ntfy.sh 주제는 비밀번호가 없어 이름이 곧 열쇠다. 짐작하기 어렵게 무작위 글자를 붙인다.
    static string RandomTopic()
    {
        const string letters = "abcdefghijkmnpqrstuvwxyz23456789";
        var bytes = new byte[10];
        using (var random = new System.Security.Cryptography.RNGCryptoServiceProvider()) random.GetBytes(bytes);
        return "seatwatch-" + new string(bytes.Select(b => letters[b % letters.Length]).ToArray());
    }

    static TabPage PhoneSteps(string phone, string storeText, string storeUrl, string steps)
    {
        var tab = new TabPage(phone) { BackColor = TicketStyle.Face, Padding = new Padding(12, 10, 12, 8) };
        tab.Controls.Add(new Label { Text = steps, Dock = DockStyle.Fill, ForeColor = TicketStyle.Forest, UseMnemonic = false });
        var store = StoreLink(storeText, storeUrl);
        store.AutoSize = false;
        store.Dock = DockStyle.Top;
        store.Height = 26;
        tab.Controls.Add(store);
        return tab;
    }

    static LinkLabel StoreLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 18, 0),
            LinkColor = TicketStyle.Forest, ActiveLinkColor = TicketStyle.Orange, LinkBehavior = LinkBehavior.AlwaysUnderline };
        ButtonTips.SetToolTip(link, url);
        link.LinkClicked += (s, e) =>
        {
            try { Process.Start(url); }
            catch (Exception ex) { MessageBox.Show(link.FindForm(), ex.Message, text); }
        };
        return link;
    }

    void ShowNtfyHelp(Form owner, TextBox topic)
    {
        using (var dialog = Dialog("ntfy 알림 설정 방법", 500))
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var title = Caption("ntfy로 좌석 알림 받기 · 쓰는 휴대폰을 고르세요", 12, TicketStyle.Forest, true);
            title.Dock = DockStyle.Top;
            title.Height = 32;
            page.Controls.Add(title);
            int textWidth = dialog.ClientSize.Width - dialog.Padding.Horizontal - 8;

            // 안드로이드(구글 플레이판)는 즉시 전달을 켜야 Firebase 지연 없이 오고, 아이폰은 알림 허용·집중 모드가 관건이다.
            var phones = new TabControl { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 232,
                Margin = new Padding(0, 0, 0, 8), AccessibleName = "휴대폰 종류" };
            phones.TabPages.Add(PhoneSteps("안드로이드", "1.  Play 스토어에서 ntfy 앱 설치  ↗",
                "https://play.google.com/store/apps/details?id=io.heckel.ntfy",
                "2.  앱을 열고 오른쪽 아래 ＋ 를 누릅니다.\n" +
                "3.  아래 주제 이름을 입력합니다.\n" +
                "     'Use another server'는 체크하지 않습니다.\n" +
                "4.  'Instant delivery in doze mode'를 체크하고 Subscribe.\n" +
                "     화면 위에 ntfy 상시 알림이 생기는 것은 정상입니다.\n" +
                "5.  알림 권한을 물으면 '허용'을 누릅니다.\n" +
                "6.  휴대폰 설정 → 애플리케이션 → ntfy → 배터리에서\n" +
                "     '제한 없음'을 고릅니다. 제한하면 알림이 늦게 옵니다.\n" +
                "7.  PC에서 종 버튼(시험 알림)으로 확인한 뒤 저장합니다."));
            phones.TabPages.Add(PhoneSteps("아이폰", "1.  App Store에서 ntfy 앱 설치  ↗",
                "https://apps.apple.com/app/ntfy/id1625396347",
                "2.  앱을 열고 오른쪽 위 ＋ 를 누릅니다.\n" +
                "3.  아래 주제 이름을 입력하고 Subscribe 를 누릅니다.\n" +
                "     'Use another server'는 켜지 않습니다.\n" +
                "4.  알림을 보내도 되는지 물으면 '허용'을 누릅니다.\n" +
                "     이미 거절했다면 설정 → ntfy → 알림에서\n" +
                "     '알림 허용'을 켭니다.\n" +
                "5.  집중 모드를 쓰면 설정 → 집중 모드 → 쓰는 모드 →\n" +
                "     허용된 앱에 ntfy를 추가해야 알림이 울립니다.\n" +
                "6.  PC에서 종 버튼(시험 알림)으로 확인한 뒤 저장합니다."));
            page.Controls.Add(phones);

            page.Controls.Add(FieldLabel("주제 이름 · 휴대폰 앱에도 똑같이 입력"));
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var name = new TextBox { ReadOnly = true, BackColor = TicketStyle.Face, AccessibleName = "주제 이름",
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Consolas", 11f) };
            Button copy = null;
            Action show = () =>
            {
                string current = topic.Text.Trim();
                name.Text = current.Length > 0 ? current : "(아직 없음 · 오른쪽 버튼으로 만들기)";
                copy.Enabled = current.Length > 0;
            };
            copy = IconButton(Glyph.Copy, "주제 이름 복사", TicketStyle.Face, TicketStyle.Forest, (s, e) =>
            {
                try { Clipboard.SetText(topic.Text.Trim()); }
                catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "주제 이름 복사"); }
            });
            var shuffle = IconButton(Glyph.Shuffle, "무작위 주제 만들기", TicketStyle.Forest, Color.White, (s, e) =>
            {
                if (topic.Text.Trim().Length > 0 && MessageBox.Show(dialog,
                    "주제를 바꾸면 휴대폰 ntfy 앱에서도 새 주제로 다시 구독해야 합니다. 바꿀까요?",
                    "무작위 주제 만들기", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                topic.Text = RandomTopic();
                show();
            });
            copy.Anchor = shuffle.Anchor = AnchorStyles.Left;
            row.Controls.Add(name, 0, 0);
            row.Controls.Add(copy, 1, 0);
            row.Controls.Add(shuffle, 2, 0);
            page.Controls.Add(row);
            show();

            var warning = new Label { AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = TicketStyle.Orange,
                Margin = new Padding(3, 8, 0, 0), UseMnemonic = false, Text =
                "ntfy.sh 주제는 비밀번호가 없어서 이름을 아는 사람은 누구나 알림을 볼 수 있습니다.\n" +
                "남이 짐작하기 어려운 이름을 쓰세요.\n" +
                "새로 만든 주제는 알림 설정 창에서 저장해야 적용됩니다." };
            page.Controls.Add(warning);

            var buttons = ButtonRow();
            buttons.Dock = DockStyle.Bottom;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            var close = ActionButton("닫기", TicketStyle.Forest, Color.White, (s, e) => dialog.DialogResult = DialogResult.OK);
            buttons.Controls.Add(close);
            dialog.Controls.Add(page);
            dialog.Controls.Add(buttons);
            dialog.AcceptButton = dialog.CancelButton = close;
            dialog.ShowDialog(owner);
        }
    }

    void ShowTelegramHelp(Form owner)
    {
        using (var dialog = Dialog("텔레그램 알림 설정 방법", 445))
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var title = Caption("텔레그램으로 좌석 알림 받기 · 아이폰과 안드로이드 같음", 12, TicketStyle.Forest, true);
            title.Dock = DockStyle.Top;
            title.Height = 32;
            page.Controls.Add(title);
            int textWidth = dialog.ClientSize.Width - dialog.Padding.Horizontal - 8;

            var links = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(3, 0, 0, 6) };
            links.Controls.Add(StoreLink("Play 스토어  ↗", "https://play.google.com/store/apps/details?id=org.telegram.messenger"));
            links.Controls.Add(StoreLink("App Store  ↗", "https://apps.apple.com/app/telegram-messenger/id686449807"));
            links.Controls.Add(StoreLink("PC에서 텔레그램 웹 열기  ↗", "https://web.telegram.org/"));
            page.Controls.Add(links);

            // 토큰은 길어서 휴대폰에서 PC로 옮겨 적기 어렵다. PC의 텔레그램 웹에서 복사하도록 안내한다.
            page.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = TicketStyle.Forest,
                UseMnemonic = false, Margin = new Padding(3, 0, 0, 0), Text =
                "1.  휴대폰에 텔레그램을 설치하고 전화번호로 가입합니다.\n" +
                "2.  PC에서 위 '텔레그램 웹'을 열고 휴대폰으로 로그인합니다.\n" +
                "     토큰을 복사해 붙여 넣기 쉬워집니다. 휴대폰에서 해도 됩니다.\n" +
                "3.  검색에서 @BotFather (파란 체크 표시)를 열고 Start 를 누릅니다.\n" +
                "4.  /newbot 을 보내고 봇 이름(아무거나)을 보냅니다.\n" +
                "5.  봇 아이디를 보냅니다. 영문이고 끝이 bot 이어야 합니다.\n" +
                "     예: myseat_alert_bot\n" +
                "6.  답장에 온 긴 토큰(123456789:AA…)을 '텔레그램 봇 토큰'에 붙여 넣습니다.\n" +
                "7.  답장의 t.me/… 링크로 내 봇을 열고 Start 를 누릅니다.\n" +
                "8.  알림 설정 창에서 채팅 ID 옆 돋보기(찾기) 버튼을 누릅니다.\n" +
                "     Start 를 누르고 24시간 안에 찾아야 합니다.\n" +
                "9.  종 버튼(시험 알림)으로 휴대폰에 오는지 확인한 뒤 저장합니다." });

            page.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = TicketStyle.Orange,
                Margin = new Padding(3, 10, 0, 0), UseMnemonic = false, Text =
                "봇 토큰은 비밀번호처럼 다루세요.\n알려지면 남이 내 봇으로 메시지를 보낼 수 있습니다.\n" +
                "알림이 안 오면 휴대폰 설정에서 텔레그램 알림을 허용하고,\n" +
                "안드로이드는 텔레그램 배터리 사용을 '제한 없음'으로 바꾸세요." });

            var buttons = ButtonRow();
            buttons.Dock = DockStyle.Bottom;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            var close = ActionButton("닫기", TicketStyle.Forest, Color.White, (s, e) => dialog.DialogResult = DialogResult.OK);
            buttons.Controls.Add(close);
            dialog.Controls.Add(page);
            dialog.Controls.Add(buttons);
            dialog.AcceptButton = dialog.CancelButton = close;
            dialog.ShowDialog(owner);
        }
    }

    void LookUpTelegramChat(Form dialog, Button find, TextBox token, TextBox chat)
    {
        string key = token.Text.Trim();
        find.Enabled = false;
        Task.Run(() =>
        {
            string[] found = null;
            string problem = null;
            try { found = FindTelegramChat(key); }
            catch (Exception ex) { problem = ex.Message; }
            if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
            dialog.BeginInvoke(new Action(() =>
            {
                if (dialog.IsDisposed) return;
                find.Enabled = true;
                if (problem != null) MessageBox.Show(dialog, problem, "채팅 ID 찾기");
                else if (found == null)
                    MessageBox.Show(dialog, "봇에게 온 메시지가 없습니다.\n텔레그램에서 내 봇을 열고 Start 를 누른 뒤(이미 눌렀다면 아무 메시지나 보낸 뒤) 다시 찾아 주세요.",
                        "채팅 ID 찾기");
                else
                {
                    chat.Text = found[0];
                    MessageBox.Show(dialog, (found[1].Length > 0 ? found[1] + " 님의 " : "") + "채팅 ID를 찾았습니다.\n종 버튼으로 시험 알림을 보내 보세요.",
                        "채팅 ID 찾기");
                }
            }));
        });
    }

    void ShowSettings()
    {
        using (var dialog = Dialog("공통 알림 설정 · 모든 노선 기본값", 380))
        {
            var form = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 3 };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 4; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var interval = new NumericUpDown { Minimum = MinInterval, Maximum = 60,
                Value = Math.Max(MinInterval, Math.Min(60, settings.IntervalMin)), Anchor = AnchorStyles.Left, AccessibleName = "기본 조회 간격(분)" };
            var topic = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = settings.NtfyTopic, AccessibleName = "ntfy 주제" };
            var token = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = settings.TelegramToken, UseSystemPasswordChar = true, AccessibleName = "텔레그램 봇 토큰" };
            var chat = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = settings.TelegramChat, AccessibleName = "텔레그램 채팅 ID" };
            var help = IconButton(Glyph.Help, "ntfy 알림 설정 방법", TicketStyle.Face, TicketStyle.Forest,
                (s, e) => ShowNtfyHelp(dialog, topic));
            var telegramHelp = IconButton(Glyph.Help, "텔레그램 알림 설정 방법", TicketStyle.Face, TicketStyle.Forest,
                (s, e) => ShowTelegramHelp(dialog));
            Button find = null;
            find = IconButton(Glyph.Search, "채팅 ID 찾기 · 봇에서 Start 를 누른 뒤", TicketStyle.Face, TicketStyle.Forest,
                (s, e) => LookUpTelegramChat(dialog, find, token, chat));
            // 체크를 풀면 칸을 흐리게만 하고 적어 둔 값은 남긴다. 텔레그램은 토큰과 채팅 ID가 함께 있어야 보내므로 체크 하나로 둘 다 켜고 끈다.
            var ntfyOn = new CheckBox { Text = topic.AccessibleName, Checked = settings.NtfyOn, AutoSize = true, Anchor = AnchorStyles.Left };
            var telegramOn = new CheckBox { Text = token.AccessibleName, Checked = settings.TelegramOn, AutoSize = true, Anchor = AnchorStyles.Left };
            Action applyOn = () =>
            {
                topic.Enabled = ntfyOn.Checked;
                token.Enabled = chat.Enabled = find.Enabled = telegramOn.Checked;
            };
            ntfyOn.CheckedChanged += (s, e) => { applyOn(); if (ntfyOn.Checked) topic.Focus(); };
            telegramOn.CheckedChanged += (s, e) => { applyOn(); if (telegramOn.Checked) token.Focus(); };
            // 꺼 둔 채 설정 방법 창에서 새 주제를 만들면 쓰려는 것이므로 켠다.
            topic.TextChanged += (s, e) => { if (!ntfyOn.Checked && topic.Text.Trim().Length > 0) ntfyOn.Checked = true; };
            applyOn();
            var rows = new Control[][] {
                new Control[] { null, interval, null }, new Control[] { ntfyOn, topic, help },
                new Control[] { telegramOn, token, telegramHelp },
                // 위 텔레그램 체크를 따르는 줄임을 보이려고 체크 글자 위치에 맞춰 들여 쓴다.
                new Control[] { new Label { Text = chat.AccessibleName, AutoSize = true, Anchor = AnchorStyles.Left,
                    Margin = new Padding(20, 0, 3, 0) }, chat, find } };
            for (int i = 0; i < rows.Length; i++)
            {
                form.Controls.Add(rows[i][0] ?? new Label { Text = rows[i][1].AccessibleName, AutoSize = true, Anchor = AnchorStyles.Left }, 0, i);
                form.Controls.Add(rows[i][1], 1, i);
                if (rows[i][2] == null) continue;
                rows[i][2].Anchor = AnchorStyles.Left;
                form.Controls.Add(rows[i][2], 2, i);
            }
            // 노선별 설정(노선의 종 버튼)과 역할을 나눈다: 여기는 휴대폰 연결과 기본값, 노선은 '이 노선만 다르게'.
            int own = settings.Watches.Count(w => w.HasOwnAlerts);
            var hint = Caption("모든 노선의 기본값입니다. 노선마다 받는 곳·간격을 바꾸려면 노선을 고른 뒤 종 버튼을 누르세요.\n" +
                "체크를 풀면 노선 설정과 상관없이 모든 노선의 그 휴대폰 알림을 멈춥니다." +
                (own > 0 ? "\n" + own + "개 노선은 따로 정한 알림·간격을 씁니다. 여기서 바꿔도 따로 정한 부분은 그대로입니다." : ""),
                9, TicketStyle.Muted, false);
            hint.AutoEllipsis = false;
            form.Controls.Add(hint, 0, 4);
            form.SetColumnSpan(hint, 3);
            Func<Settings> entered = () => new Settings { NtfyTopic = topic.Text.Trim(),
                TelegramToken = token.Text.Trim(), TelegramChat = chat.Text.Trim(),
                NtfyOn = ntfyOn.Checked, TelegramOn = telegramOn.Checked };
            var buttons = ButtonRow();
            Button test = null;
            test = IconButton(Glyph.Bell, "시험 알림 보내기", TicketStyle.Face, TicketStyle.Forest, (s, e) =>
            {
                var target = entered();
                if (target.TelegramOn && target.TelegramToken.Length > 0 != target.TelegramChat.Length > 0)
                    MessageBox.Show(dialog, "텔레그램은 봇 토큰과 채팅 ID를 모두 채워야 보냅니다.", "시험 알림");
                else if (!target.UsesNtfy() && !target.UsesTelegram())
                    MessageBox.Show(dialog, "체크하고 채워 둔 휴대폰 알림이 없어 PC로만 알립니다.", "시험 알림");
                test.Enabled = false;
                Notify("좌석이 생겼어요!", "알림 시험 · 진에어 18:05 할인석 · 진에어 홈페이지로 가는 버튼이 붙습니다", null,
                    new[] { Airline.FindByCode("LJ") }, target).ContinueWith(t =>
                {
                    if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                    dialog.BeginInvoke(new Action(() =>
                    {
                        if (dialog.IsDisposed) return;
                        test.Enabled = true;
                        if (t.Result.Length > 0) MessageBox.Show(dialog, "보내지 못한 곳이 있습니다.\n\n" + string.Join("\n", t.Result), "시험 알림");
                    }));
                });
            });
            var save = ActionButton("저장", TicketStyle.Forest, Color.White, (s, e) => {
                var target = entered();
                // 꺼 둔 텔레그램은 적다 만 토큰이어도 그대로 저장한다.
                if (target.TelegramOn && target.TelegramToken.Length > 0 && !IsTelegramToken(target.TelegramToken))
                {
                    MessageBox.Show(dialog, "텔레그램 봇 토큰 형식이 아닙니다. BotFather가 보낸 123456789:AA… 모양의 토큰을 넣어 주세요.", dialog.Text);
                    return;
                }
                if (target.TelegramOn && (target.TelegramToken.Length > 0) != (target.TelegramChat.Length > 0))
                {
                    MessageBox.Show(dialog, "텔레그램은 토큰과 채팅 ID를 함께 입력해 주세요.", dialog.Text);
                    return;
                }
                // 노선마다 실제로 보낼 곳(노선 설정을 덮어쓴 것)을 비교한다.
                foreach (var w in settings.Watches) w.ResetPending(w.AlertTarget(settings), w.AlertTarget(target));
                settings.IntervalMin = (int)interval.Value;
                settings.NtfyTopic = target.NtfyTopic;
                settings.TelegramToken = target.TelegramToken;
                settings.TelegramChat = target.TelegramChat;
                settings.NtfyOn = target.NtfyOn;
                settings.TelegramOn = target.TelegramOn;
                if (!SaveSettings()) return;
                dialog.DialogResult = DialogResult.OK;
            });
            var cancel = ActionButton("취소", TicketStyle.Face, TicketStyle.Muted, (s, e) => dialog.DialogResult = DialogResult.Cancel);
            buttons.Controls.Add(test);
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            form.Controls.Add(buttons, 0, 5);
            form.SetColumnSpan(buttons, 3);
            dialog.Controls.Add(form);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            dialog.ShowDialog(this);
        }
    }
}
