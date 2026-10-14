namespace MultiAgentHubApp;

/// <summary>Native scroll host with stable cards and viewport anchors during streaming.</summary>
internal sealed class MessageFeed : Panel
{
    private readonly Dictionary<string, MessageCard> cards = [];
    private readonly List<MessageCard> ordered = [];
    private readonly Button older;
    private IReadOnlyList<ChatSection> sections = [];
    private string topic = "";
    private int first;
    private bool follow = true, arranging, queued, dirty;
    private int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private sealed record ViewportMark(string Key, int Top);
    public MessageFeed()
    {
        Dock = DockStyle.Fill; AutoScroll = true; DoubleBuffered = true; ResizeRedraw = true; BackColor = Ui.Background;
        older = Ui.Button("Hiển thị tin nhắn cũ", (_, _) => { follow = false; first = Math.Max(0, first - 80); Sync(); SetScroll(0); });
        Controls.Add(older); older.Visible = false;
    }
    public void ClearMessages()
    {
        foreach (var card in cards.Values) { Controls.Remove(card); card.Dispose(); }
        cards.Clear(); ordered.Clear(); sections = []; topic = ""; first = 0; follow = true;
        older.Visible = false; AutoScrollMinSize = Size.Empty; AutoScrollPosition = Point.Empty;
    }
    public void Apply(IReadOnlyList<ChatSection> value)
    {
        string nextTopic = value.FirstOrDefault()?.Key ?? "";
        if (topic != nextTopic) { ClearMessages(); topic = nextTopic; first = Math.Max(0, value.Count - 80); }
        sections = value; Sync();
    }
    private ViewportMark? CaptureAnchor() => ordered.FirstOrDefault(c => c.Bottom > 0 && c.Top < ClientSize.Height) is { } card ? new(card.Key, card.Top) : null;
    private void Sync()
    {
        var anchor = CaptureAnchor(); var visible = sections.Skip(first).ToList();
        var needed = visible.Select(s => s.Key).ToHashSet();
        bool changed = !ordered.Select(c => c.Key).SequenceEqual(visible.Select(s => s.Key));
        arranging = true; SuspendLayout();
        try {
            foreach (string key in cards.Keys.Where(k => !needed.Contains(k)).ToList()) { var card = cards[key]; Controls.Remove(card); card.Dispose(); cards.Remove(key); }
            ordered.Clear();
            foreach (var section in visible) {
                if (!cards.TryGetValue(section.Key, out var card)) {
                    card = new MessageCard(); cards.Add(section.Key, card); Controls.Add(card);
                    card.HeightRequested += ScheduleArrange; card.Interacting += () => follow = false;
                    card.Wheel += ScrollWheel; card.Navigate += Navigate;
                }
                changed |= card.Apply(section); ordered.Add(card);
            }
            older.Visible = first > 0;
        } finally { ResumeLayout(false); arranging = false; }
        if (changed || dirty) Arrange(anchor);
    }
    private void ScheduleArrange()
    {
        if (arranging) { dirty = true; return; }
        if (queued || !IsHandleCreated || IsDisposed || Disposing) return;
        queued = true;
        BeginInvoke((Action)(() => { queued = false; if (!IsDisposed && !Disposing) Arrange(CaptureAnchor()); }));
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ScheduleArrange(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ScheduleArrange(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) ScheduleArrange(); }
    protected override Point ScrollToControl(Control activeControl) => activeControl is RichTextBox ? DisplayRectangle.Location : base.ScrollToControl(activeControl);
    private void Arrange(ViewportMark? anchor)
    {
        if (arranging || IsDisposed || Disposing) return;
        arranging = true; dirty = false;
        try {
            int scroll = AutoScrollPosition.Y, anchorY = -1;
            SuspendLayout();
            try {
                int available = Math.Max(D(160), ClientSize.Width - D(48));
                int width = Math.Min(D(780), available), x = Math.Max(D(12), (ClientSize.Width - width) / 2);
                int y = D(12);
                if (first > 0) {
                    var size = older.GetPreferredSize(Size.Empty);
                    older.SetBounds(x, y + scroll, Math.Min(width, size.Width), size.Height); y += size.Height + D(18);
                }
                foreach (var card in ordered) {
                    int cardWidth = card.User ? (int)(width * 0.72) : width;
                    int height = card.Measure(cardWidth);
                    if (card.Key == anchor?.Key) anchorY = y;
                    card.SetBounds(x + width - cardWidth, y + scroll, cardWidth, height);
                    y += height + D(18);
                }
                AutoScrollMinSize = new Size(0, y + D(12));
            } finally {
                // ScrollableControl recalculates its native range during layout. Flush it
                // before restoring the viewport; otherwise new messages remain beyond the old maximum.
                ResumeLayout(true);
            }
            int maximum = ScrollMaximum;
            int target = follow ? maximum : anchorY >= 0 && anchor != null ? anchorY - anchor.Top : -scroll;
            AutoScrollPosition = new Point(0, Math.Clamp(target, 0, maximum));
        } finally { arranging = false; }
        if (dirty) ScheduleArrange();
    }
    private int ScrollMaximum => Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
    private bool AtBottom() => -AutoScrollPosition.Y >= ScrollMaximum - D(2);
    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); if (!arranging) follow = AtBottom(); }
    protected override void OnMouseWheel(MouseEventArgs e) { if (e is HandledMouseEventArgs handled) handled.Handled = true; ScrollWheel(e.Delta); }
    private void ScrollWheel(int delta)
    {
        int lines = SystemInformation.MouseWheelScrollLines;
        int distance = lines < 0 ? ClientSize.Height : D(20) * Math.Max(1, lines);
        SetScroll(-AutoScrollPosition.Y - (int)Math.Round(delta / 120d * distance));
    }
    private void SetScroll(int position)
    {
        AutoScrollPosition = new Point(0, Math.Clamp(position, 0, ScrollMaximum));
        follow = AtBottom();
    }
    private void Navigate(Keys key)
    {
        if (key == Keys.End) ScrollToEnd();
        else if (key == Keys.Home) SetScroll(0);
        else SetScroll(-AutoScrollPosition.Y + (key == Keys.PageDown ? 1 : -1) * Math.Max(D(40), ClientSize.Height - D(40)));
    }
    public void ScrollToEnd() { SetScroll(ScrollMaximum); follow = true; }
    public void ApplyTheme() { BackColor = Ui.Background; foreach (var card in cards.Values) card.ApplyTheme(); }
}
