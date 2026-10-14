using System.Runtime.InteropServices;
namespace MultiAgentHubApp;

/// <summary>Labels are drawn with ellipsis; native horizontal scrolling must not measure their full text.</summary>
internal sealed class SidebarTree : TreeView
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        const int TvmSetExtendedStyle = 0x112C, TvsExDoubleBuffer = 0x0004;
        SendMessage(Handle, TvmSetExtendedStyle, new IntPtr(TvsExDoubleBuffer), new IntPtr(TvsExDoubleBuffer));
    }
    public void InvalidateRow(TreeNode? node)
    {
        if (node?.TreeView != this || node.Bounds.Height == 0) return;
        Invalidate(new Rectangle(0, node.Bounds.Top, ClientSize.Width, ItemHeight), false);
    }
    protected override CreateParams CreateParams
    {
        get {
            var parameters = base.CreateParams;
            const int TvsNoHScroll = 0x8000;
            parameters.Style |= TvsNoHScroll;
            return parameters;
        }
    }
}
