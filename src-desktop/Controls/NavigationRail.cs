namespace MultiAgentHubApp;

internal sealed class NavigationRail : Panel
{
    private readonly List<Button> navigation = [];
    private readonly Button settings;
    private bool arranging;
    public event Action? NewRequested, SearchRequested, ProjectRequested, SettingsRequested;
    public NavigationRail()
    {
        Dock = DockStyle.Left; Width = 46; BackColor = Ui.Rail;
        foreach (var item in new (string icon, string label, Action action)[] {
            ("home", "Cuộc trò chuyện mới", () => NewRequested?.Invoke()),
            ("chat", "Tìm cuộc trò chuyện", () => SearchRequested?.Invoke()),
            ("folder", "Thêm project", () => ProjectRequested?.Invoke()),
            ("clock", "Tìm trong lịch sử", () => SearchRequested?.Invoke()) }) {
            var button = Ui.IconButton(item.icon, item.label, (_, _) => item.action());
            button.Margin = Padding.Empty; navigation.Add(button); Controls.Add(button);
        }
        settings = Ui.IconButton("settings", "Agent, model và tài khoản", (_, _) => SettingsRequested?.Invoke()); settings.Margin = Padding.Empty; Controls.Add(settings);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (arranging || navigation is not { Count: > 0 } || settings == null) return;
        arranging = true;
        try {
            int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            int size = Math.Min(D(32), Math.Max(1, ClientSize.Width - D(8)));
            int x = Math.Max(0, (ClientSize.Width - size) / 2);
            for (int i = 0; i < navigation.Count; i++) navigation[i].SetBounds(x, D(12 + i * 45), size, size);
            settings.SetBounds(x, Math.Max(D(12), ClientSize.Height - D(12) - size), size, size);
        } finally { arranging = false; }
    }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); PerformLayout(); }
    public void ApplyTheme() { BackColor = Ui.Rail; PerformLayout(); }
}
