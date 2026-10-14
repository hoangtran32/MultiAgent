namespace MultiAgentHubApp;

// Constrain both controls to the actual header viewport. An implicit AutoSize row
// in a nested TableLayoutPanel can retain a scaled preferred height and clip a docked button.
internal sealed class SidebarHeader : Panel
{
    private Control? choice;
    private Button? search;
    private bool arranging;

    public SidebarHeader() { DoubleBuffered = true; }

    public void SetChoice(Control control)
    {
        if (choice != null) Controls.Remove(choice);
        choice = control; Prepare(control); Controls.Add(control); PerformLayout();
    }

    public void SetSearchButton(Button button)
    {
        if (search != null) Controls.Remove(search);
        search = button; Prepare(button); Controls.Add(button); PerformLayout();
    }

    private static void Prepare(Control control)
    {
        control.AutoSize = false; control.Dock = DockStyle.None; control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        control.Margin = Padding.Empty; control.MinimumSize = Size.Empty; control.MaximumSize = Size.Empty;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (arranging) return;
        arranging = true;
        try {
            int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            int width = ClientSize.Width, height = ClientSize.Height;
            int searchSize = Math.Min(D(32), Math.Min(width, height));
            int gap = D(4);
            search?.SetBounds(Math.Max(0, width - searchSize), Math.Max(0, (height - searchSize) / 2), searchSize, searchSize);
            if (choice != null) {
                choice.Padding = new Padding(D(10), D(4), D(10), D(4));
                choice.SetBounds(0, 0, Math.Max(0, width - searchSize - gap), height);
            }
        } finally { arranging = false; }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); PerformLayout(); Invalidate(true); }
}
