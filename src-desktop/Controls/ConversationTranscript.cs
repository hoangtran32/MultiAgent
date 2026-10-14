using System.Runtime.InteropServices;

namespace MultiAgentHubApp;

/// <summary>Native scrolling stays enabled. Streaming updates preserve the user's viewport and selection.</summary>
internal class ConversationTranscript : RichTextBox
{
    private bool changingDocument;
    public event Action<bool>? UserScrolled;
    private const int EmGetFirstVisibleLine = 0x00CE, EmLineScroll = 0x00B6, WmSetRedraw = 0x000B;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint Size, Mask;
        public int Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);

    public void SetDocument(string rtf, bool followEnd)
    {
        int start = SelectionStart, length = SelectionLength;
        int top = IsHandleCreated ? SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
        changingDocument = true;
        try {
            if (IsHandleCreated) SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            Rtf = rtf;
            if (followEnd) { Select(TextLength, 0); ScrollToCaret(); }
            else {
                Select(Math.Min(start, TextLength), Math.Min(length, Math.Max(0, TextLength - start)));
                int current = SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
                SendMessage(Handle, EmLineScroll, IntPtr.Zero, new IntPtr(top - current));
            }
        } finally {
            if (IsHandleCreated) SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            changingDocument = false; Invalidate();
        }
    }
    public void SetPlainText(string value, bool? followEnd = null)
    {
        string previous = Text; if (previous == value) return;
        bool follow = followEnd ?? (SelectionLength == 0 && IsAtBottom());
        int start = SelectionStart, length = SelectionLength;
        int top = IsHandleCreated ? SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
        changingDocument = true;
        try {
            if (IsHandleCreated) SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            if (value.StartsWith(previous, StringComparison.Ordinal)) AppendText(value[previous.Length..]); else Text = value;
            if (follow) { Select(TextLength, 0); ScrollToCaret(); }
            else {
                Select(Math.Min(start, TextLength), Math.Min(length, Math.Max(0, TextLength - start)));
                int current = SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
                SendMessage(Handle, EmLineScroll, IntPtr.Zero, new IntPtr(top - current));
            }
        } finally {
            if (IsHandleCreated) SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            changingDocument = false; Invalidate();
        }
    }
    private bool IsAtBottom()
    {
        if (TextLength == 0) return true;
        var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x07 };
        if (!GetScrollInfo(Handle, 1, ref info)) return false;
        return info.Position >= Math.Max(info.Minimum, info.Maximum - (int)info.Page + 1) - 1;
    }
    protected override void WndProc(ref Message message)
    {
        bool scroll = !changingDocument && (message.Msg is 0x0115 or 0x020A || message.Msg == 0x0100 &&
            (Keys)message.WParam.ToInt32() is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End);
        base.WndProc(ref message);
        if (scroll && !IsDisposed) UserScrolled?.Invoke(IsAtBottom());
    }
}
