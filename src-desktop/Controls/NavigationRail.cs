namespace MultiAgentHubApp;

internal sealed class NavigationRail : Panel
{
    private readonly List<Button> navigation = [];
    private bool arranging;
    private bool chatSurface;
    public event Action? NewRequested, SearchRequested, ProjectRequested;
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
    }
    public void SetSurface(string surface)
    {
        bool chat = surface == "chat";
        if (chatSurface == chat) return;
        chatSurface = chat; navigation[2].Visible = !chat; PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (arranging || navigation is not { Count: > 0 }) return;
        arranging = true;
        try {
            int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            int size = Math.Min(D(32), Math.Max(1, ClientSize.Width - D(8)));
            int x = Math.Max(0, (ClientSize.Width - size) / 2);
            int row = 0;
            for (int i = 0; i < navigation.Count; i++) {
                if (chatSurface && i == 2) continue;
                navigation[i].SetBounds(x, D(12 + row++ * 45), size, size);
            }
        } finally { arranging = false; }
    }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); PerformLayout(); }
    public void ApplyTheme() { BackColor = Ui.Rail; PerformLayout(); }
}
