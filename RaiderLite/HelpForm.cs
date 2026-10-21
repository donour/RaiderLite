using System.Diagnostics;
using System.Runtime.InteropServices;
using RaiderLite.Core.Help;

namespace RaiderLite;

/// <summary>
/// The help window: contents and search on the left, the page on the right. One window is
/// shared by the whole application; <see cref="ShowTopic"/> opens it at a page.
/// </summary>
public sealed class HelpForm : Form
{
    private static HelpForm? _instance;
    private static HelpLibrary? _library;

    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search the help (Ctrl+F)", AccessibleName = "Search the help" };
    private readonly TreeView _contents = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None, ShowLines = false, FullRowSelect = true };
    private readonly Panel _left = new() { Dock = DockStyle.Left, Width = 260, Padding = new Padding(6) };
    private readonly Splitter _splitter = new() { Dock = DockStyle.Left, Width = 4 };
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly HelpPage _page = new();
    private readonly ToolStrip _bar = new() { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Padding = new Padding(4, 2, 4, 2) };
    private readonly ToolStripButton _backButton;
    private readonly ToolStripButton _forwardButton;
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 150 };

    private readonly Stack<Place> _back = new();
    private readonly Stack<Place> _forward = new();
    private Place? _current;
    private HelpTopic? _rendered;
    private int _renderedWidth;
    private string[] _terms = [];
    private string[] _renderedTerms = [];
    private bool _syncingTree;

    /// <summary>A place in the help: a page, and a heading on it or null for the top.</summary>
    private sealed record Place(HelpTopic Topic, string? Anchor);

    /// <summary>The help pages, read from the application's resources on first use.</summary>
    public static HelpLibrary Library => _library ??= HelpLibrary.FromAssembly(typeof(HelpForm).Assembly);

    /// <summary>
    /// Opens the help window (or brings it forward) at <paramref name="link"/>: a page id, or
    /// "id#anchor" for a heading on it. Without a link it stays where it was, else opens the first page.
    /// </summary>
    public static void ShowTopic(string? link = null)
    {
        // A modal dialog disables the windows that were open when it appeared, the help among
        // them; a new window opened from the dialog works.
        if (_instance is { IsDisposed: false } old && !NativeMethods.IsWindowEnabled(old.Handle))
        {
            old.Close();
            _instance = null;
        }
        if (_instance == null || _instance.IsDisposed)
        {
            _instance = new HelpForm();
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == FormWindowState.Minimized)
                _instance.WindowState = FormWindowState.Normal;
            _instance.Activate();
        }
        if (link != null)
            _instance.Navigate(link);
        else if (_instance._current == null)
            _instance.Navigate(Library.Topics[0].Id);
    }

    private HelpForm()
    {
        Text = "RaiderLite Help";
        Font = UiFonts.Regular;
        ShowIcon = true;
        Icon = SystemIcons.Application;
        MinimumSize = new Size(560, 360);
        PlaceWindow();

        _backButton = BarButton("Back", IconFont.Back, "Back (Alt+Left)", GoBack);
        _forwardButton = BarButton("Forward", IconFont.Forward, "Forward (Alt+Right)", GoForward);
        _bar.Items.Add(new ToolStripSeparator());
        BarButton("Contents", IconFont.Help, "The first page of the help", () => Navigate(Library.Topics[0].Id));

        _contents.Font = UiFonts.Bold;
        _contents.ItemHeight = LogicalToDeviceUnits(24);
        _left.Width = LogicalToDeviceUnits(260);
        _left.Controls.Add(_contents);
        _left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6 });
        _left.Controls.Add(_search);
        // A margin around the page text; the page fills the rest.
        _pageHost.Padding = new Padding(LogicalToDeviceUnits(24), LogicalToDeviceUnits(14), LogicalToDeviceUnits(8), 0);
        _pageHost.Controls.Add(_page);

        // Dock order: added last docks first.
        Controls.Add(_pageHost);
        Controls.Add(_splitter);
        Controls.Add(_left);
        Controls.Add(_bar);

        FillContents(Library.Topics, expand: false);
        UpdateButtons();
        ActiveControl = _contents;

        _contents.AfterSelect += (_, e) =>
        {
            if (!_syncingTree && e.Node?.Tag is string link)
                Navigate(link);
        };
        _search.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplySearch();
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _search.TextLength > 0)
            {
                _search.Clear();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode is Keys.Down or Keys.Enter)
            {
                _searchTimer.Stop();
                ApplySearch();
                _contents.Focus();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _page.LinkClicked += (_, e) => OnLinkClicked(e);
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            if (_current != null && PageWidthTwips() != _renderedWidth)
                Render(_current.Topic, keepScroll: true);
        };
        _pageHost.Resize += (_, _) =>
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
    }

    private ToolStripButton BarButton(string text, char glyph, string tip, Action click)
    {
        var button = new ToolStripButton(text)
        {
            Image = IconFont.Render(glyph, LogicalToDeviceUnits(16), Color.FromArgb(50, 54, 62)),
            ImageScaling = ToolStripItemImageScaling.None,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = tip,
            AutoToolTip = false,
        };
        button.Click += (_, _) => click();
        _bar.Items.Add(button);
        return button;
    }

    private void PlaceWindow()
    {
        var saved = AppSettings.Current.HelpWindowBounds;
        if (saved.Width >= MinimumSize.Width && saved.Height >= MinimumSize.Height &&
            Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = saved;
            return;
        }
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(LogicalToDeviceUnits(980), area.Width - 40), Math.Min(LogicalToDeviceUnits(760), area.Height - 40));
        StartPosition = FormStartPosition.CenterScreen;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        AppSettings.Current.HelpWindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (ReferenceEquals(_instance, this))
            _instance = null;
        _searchTimer.Dispose();
        _resizeTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.Left:
            case Keys.BrowserBack:
                GoBack();
                return true;
            case Keys.Alt | Keys.Right:
            case Keys.BrowserForward:
                GoForward();
                return true;
            case Keys.Control | Keys.F:
                _search.Focus();
                _search.SelectAll();
                return true;
            case Keys.Escape when !_search.Focused || _search.TextLength == 0:
                Close();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ----- contents and search -----

    /// <summary>Lists <paramref name="topics"/> with their sections under them.</summary>
    private void FillContents(IReadOnlyList<HelpTopic> topics, bool expand)
    {
        _contents.BeginUpdate();
        _contents.Nodes.Clear();
        foreach (var topic in topics)
        {
            // Pages take the tree's bold font; a bold NodeFont on a regular tree would be clipped.
            var node = new TreeNode(topic.Title) { Tag = topic.Id };
            foreach (var section in topic.Sections)
                node.Nodes.Add(new TreeNode(section.Text) { Tag = $"{topic.Id}#{section.Anchor}", NodeFont = UiFonts.Regular });
            _contents.Nodes.Add(node);
            if (expand)
                node.Expand();
        }
        if (topics.Count == 0)
            _contents.Nodes.Add(new TreeNode($"No pages contain \"{_search.Text.Trim()}\".") { ForeColor = SystemColors.GrayText, NodeFont = UiFonts.Regular });
        _contents.EndUpdate();
        SelectContentsNode();
    }

    private void ApplySearch()
    {
        var terms = HelpLibrary.SearchTerms(_search.Text);
        if (terms.SequenceEqual(_terms, StringComparer.OrdinalIgnoreCase))
            return;
        _terms = terms;
        var found = Library.Search(_search.Text);
        FillContents(found, expand: false);
        if (terms.Length > 0 && found.Count > 0 && (_current == null || !found.Contains(_current.Topic)))
            Navigate(found[0].Id);
        else if (_current != null)
            Show(_current);
    }

    /// <summary>Selects the current page's node (its section's, when at a section) without navigating.</summary>
    private void SelectContentsNode()
    {
        if (_current == null)
            return;
        var topicNode = _contents.Nodes.Cast<TreeNode>().FirstOrDefault(n => Equals(n.Tag, _current.Topic.Id));
        var node = topicNode;
        if (topicNode != null && _current.Anchor != null)
            node = topicNode.Nodes.Cast<TreeNode>().FirstOrDefault(n => Equals(n.Tag, $"{_current.Topic.Id}#{_current.Anchor}")) ?? topicNode;
        _syncingTree = true;
        try
        {
            foreach (TreeNode other in _contents.Nodes)
            {
                if (other != topicNode && other.IsExpanded)
                    other.Collapse();
            }
            if (topicNode != null && !topicNode.IsExpanded)
                topicNode.Expand();
            _contents.SelectedNode = node;
        }
        finally
        {
            _syncingTree = false;
        }
    }

    // ----- navigation -----

    /// <summary>Follows a link: a page id, "id#anchor", "#anchor" on the current page, or a web address.</summary>
    public void Navigate(string link)
    {
        var target = Library.Resolve(link, _current?.Topic);
        if (target?.Url is { } url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }
        if (target?.Topic == null)
            return;
        var location = new Place(target.Topic, target.Anchor);
        if (location == _current)
        {
            ScrollTo(location);
            return;
        }
        if (_current != null)
            _back.Push(_current);
        _forward.Clear();
        Show(location);
    }

    private void GoBack()
    {
        if (_back.Count == 0)
            return;
        if (_current != null)
            _forward.Push(_current);
        Show(_back.Pop());
    }

    private void GoForward()
    {
        if (_forward.Count == 0)
            return;
        if (_current != null)
            _back.Push(_current);
        Show(_forward.Pop());
    }

    private void UpdateButtons()
    {
        _backButton.Enabled = _back.Count > 0;
        _forwardButton.Enabled = _forward.Count > 0;
    }

    private void Show(Place location)
    {
        _current = location;
        if (!ReferenceEquals(_rendered, location.Topic) || PageWidthTwips() != _renderedWidth || !_terms.SequenceEqual(_renderedTerms))
            Render(location.Topic, keepScroll: false);
        ScrollTo(location);
        SelectContentsNode();
        UpdateButtons();
        Text = $"{location.Topic.Title} - RaiderLite Help";
    }

    // ----- the page -----

    private int PageWidthTwips()
    {
        // Leave room for the vertical scrollbar, so tables never cause a horizontal one.
        int pixels = _page.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - LogicalToDeviceUnits(12);
        // Round to a step, so small resizes don't re-render.
        return Math.Max(2000, HelpRtf.PixelsToTwips(pixels, DeviceDpi) / 240 * 240);
    }

    private void Render(HelpTopic topic, bool keepScroll)
    {
        int firstChar = keepScroll ? _page.GetCharIndexFromPosition(new Point(1, 1)) : 0;
        _renderedWidth = PageWidthTwips();
        _page.SetRedraw(false);
        try
        {
            _page.Rtf = HelpRtf.Render(topic, _renderedWidth);
            _rendered = topic;
            _renderedTerms = _terms;
            HighlightTerms();
            if (keepScroll)
                _page.ScrollToChar(firstChar, 0);
        }
        finally
        {
            _page.SetRedraw(true);
        }
    }

    /// <summary>Marks every occurrence of the search words on the page.</summary>
    private void HighlightTerms()
    {
        var highlight = Color.FromArgb(255, 236, 160);
        foreach (var term in _terms)
        {
            for (int start = 0; start < _page.TextLength;)
            {
                int found = _page.Find(term, start, RichTextBoxFinds.None);
                if (found < 0)
                    break;
                _page.SelectionBackColor = highlight;
                start = found + term.Length;
            }
        }
        _page.Select(0, 0);
    }

    /// <summary>Scrolls the location's heading to the top; without an anchor, to the first search match or the top.</summary>
    private void ScrollTo(Place location)
    {
        int index = 0;
        if (location.Anchor != null && location.Topic.FindHeading(location.Anchor) is { } heading)
            index = Math.Max(0, HeadingIndex(heading.Text));
        else if (_terms.Length > 0)
            index = Math.Max(0, _page.Find(_terms[0], 0, RichTextBoxFinds.None));
        _page.Select(index, 0);
        // A heading goes to the top; a match a few lines down, with its context.
        _page.ScrollToChar(index, LogicalToDeviceUnits(location.Anchor != null ? 8 : 80));
    }

    /// <summary>Where a heading starts: its text at the start of a line.</summary>
    private int HeadingIndex(string text)
    {
        var page = _page.Text;
        for (int i = page.IndexOf(text, StringComparison.Ordinal); i >= 0; i = page.IndexOf(text, i + 1, StringComparison.Ordinal))
        {
            if (i == 0 || page[i - 1] == '\n')
                return i;
        }
        return -1;
    }

    private void OnLinkClicked(LinkClickedEventArgs e)
    {
        // A HYPERLINK field reports its target; some RichEdit versions report its text instead.
        var link = e.LinkText ?? "";
        if (Library.Resolve(link, _current?.Topic) == null && _current != null)
        {
            var targets = _current.Topic.AllRuns().Where(r => r.Link != null && r.Text == link).Select(r => r.Link!).Distinct().ToList();
            if (targets.Count >= 1)
                link = targets[0];
        }
        // After the click is done with: RichEdit would otherwise scroll the new page to where it was clicked.
        BeginInvoke(() => Navigate(link));
    }

    /// <summary>A read-only rich text box for the page, without a caret.</summary>
    private sealed class HelpPage : RichTextBox
    {
        public HelpPage()
        {
            Dock = DockStyle.Fill;
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            BackColor = SystemColors.Window;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            DetectUrls = false;
            ShortcutsEnabled = true;
            TabStop = true;
        }

        public void SetRedraw(bool on)
        {
            NativeMethods.SendMessage(Handle, NativeMethods.WM_SETREDRAW, on ? 1 : 0, 0);
            if (on)
                Invalidate();
        }

        /// <summary>Scrolls so the character at <paramref name="index"/> is <paramref name="below"/> pixels from the top.</summary>
        public void ScrollToChar(int index, int below)
        {
            var pos = Point.Empty;
            NativeMethods.SendMessage(Handle, NativeMethods.EM_GETSCROLLPOS, 0, ref pos);
            pos.Y = Math.Max(0, pos.Y + GetPositionFromCharIndex(index).Y - below);
            NativeMethods.SendMessage(Handle, NativeMethods.EM_SETSCROLLPOS, 0, ref pos);
        }

        protected override void OnSelectionChanged(EventArgs e)
        {
            base.OnSelectionChanged(e);
            if (Focused)
                NativeMethods.HideCaret(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // The caret would suggest the page can be typed in.
            if (m.Msg is NativeMethods.WM_SETFOCUS or NativeMethods.WM_LBUTTONUP or NativeMethods.WM_KEYUP)
                NativeMethods.HideCaret(Handle);
        }
    }

    private static class NativeMethods
    {
        public const int WM_SETREDRAW = 0x000B;
        public const int WM_SETFOCUS = 0x0007;
        public const int WM_KEYUP = 0x0101;
        public const int WM_LBUTTONUP = 0x0202;
        public const int EM_GETSCROLLPOS = 0x0400 + 221;
        public const int EM_SETSCROLLPOS = 0x0400 + 222;

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref Point lParam);

        [DllImport("user32.dll")]
        public static extern bool HideCaret(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hWnd);
    }
}
