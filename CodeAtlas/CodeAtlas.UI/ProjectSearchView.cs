namespace CodeAtlas.UI;

internal sealed class ProjectSearchView : UserControl
{
  private readonly ComboBox _scope = new()
  {
    Dock = DockStyle.Fill,
    DropDownStyle = ComboBoxStyle.DropDownList,
    AccessibleName = "Search category",
    Margin = new Padding(0, 0, 6, 0)
  };
  private readonly TextBox _query = new()
  {
    Dock = DockStyle.Fill,
    PlaceholderText = "Search names...",
    AccessibleName = "Search names",
    Margin = new Padding(0, 0, 4, 0)
  };
  private readonly Button _clearButton = new()
  {
    Dock = DockStyle.Fill,
    Text = "\u00D7",
    AccessibleName = "Clear search",
    Enabled = false,
    Margin = Padding.Empty,
    Padding = Padding.Empty
  };
  private readonly System.Windows.Forms.Label _resultCount = new()
  {
    Dock = DockStyle.Top,
    Height = 24,
    TextAlign = ContentAlignment.MiddleLeft,
    Padding = new Padding(6, 0, 6, 0)
  };
  private readonly ListView _results = new()
  {
    Dock = DockStyle.Fill,
    View = View.Details,
    FullRowSelect = true,
    MultiSelect = false,
    HideSelection = false,
    ShowItemToolTips = true,
    AccessibleName = "Search results"
  };
  private readonly Panel _resultsHost = new() { Dock = DockStyle.Fill, Visible = false };
  private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 250 };
  private readonly ToolTip _toolTip = new();
  private IReadOnlyList<ProjectSearchEntry> _entries = Array.Empty<ProjectSearchEntry>();

  public ProjectSearchView()
  {
    Dock = DockStyle.Top;
    Height = 38;
    _scope.Items.AddRange(["All", "Project", "Class", "Module", "Method"]);
    _scope.SelectedIndex = 0;

    var searchBar = new TableLayoutPanel
    {
      Dock = DockStyle.Top,
      Height = 38,
      ColumnCount = 3,
      RowCount = 1,
      Padding = new Padding(4, 6, 4, 6),
      Margin = Padding.Empty
    };
    searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
    searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
    searchBar.Controls.Add(_scope, 0, 0);
    searchBar.Controls.Add(_query, 1, 0);
    searchBar.Controls.Add(_clearButton, 2, 0);

    _results.Columns.Add("Name", 140);
    _results.Columns.Add("Kind", 64);
    _results.Columns.Add("Location", 180);
    _resultsHost.Controls.Add(_results);
    _resultsHost.Controls.Add(_resultCount);
    Controls.Add(_resultsHost);
    Controls.Add(searchBar);
    _toolTip.SetToolTip(_clearButton, "Clear search");

    _query.TextChanged += (_, _) =>
    {
      _searchTimer.Stop();
      _clearButton.Enabled = _query.Text.Length > 0;
      if (string.IsNullOrWhiteSpace(_query.Text))
      {
        UpdateResults();
      }
      else
      {
        _searchTimer.Start();
      }
    };
    _scope.SelectedIndexChanged += (_, _) => UpdateResults();
    _searchTimer.Tick += (_, _) => UpdateResults();
    _clearButton.Click += (_, _) =>
    {
      _query.Clear();
      _query.Focus();
    };
    _query.KeyDown += OnQueryKeyDown;
    _results.KeyDown += (_, args) =>
    {
      if (args.KeyCode == Keys.Escape)
      {
        _query.Clear();
        _query.Focus();
        args.SuppressKeyPress = true;
      }
    };
    _results.ItemSelectionChanged += (_, args) =>
    {
      if (args.IsSelected && args.Item?.Tag is ProjectSearchEntry entry)
      {
        NodeSelected?.Invoke(entry.Node);
      }
    };
    _results.Resize += (_, _) => ResizeResultColumns();
  }

  public event Action<TreeNode>? NodeSelected;

  public void SetEntries(IReadOnlyList<ProjectSearchEntry> entries)
  {
    _entries = entries;
    UpdateResults();
  }

  public void FocusSearch()
  {
    _query.Focus();
    _query.SelectAll();
  }

  private void UpdateResults()
  {
    _searchTimer.Stop();
    var query = _query.Text.Trim();
    _results.BeginUpdate();
    try
    {
      _results.Items.Clear();
      if (query.Length == 0)
      {
        _resultsHost.Visible = false;
        Height = 38;
        return;
      }

      var scope = _scope.SelectedItem as string;
      foreach (var entry in _entries.Where(entry =>
          (scope == "All" || entry.Kind == scope) &&
          (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           entry.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))))
      {
        var item = new ListViewItem(entry.DisplayName)
        {
          Tag = entry,
          ToolTipText = $"{entry.QualifiedName}{Environment.NewLine}{entry.Location}"
        };
        item.SubItems.Add(entry.Kind);
        item.SubItems.Add(entry.Location);
        _results.Items.Add(item);
      }

      _resultCount.Text = _results.Items.Count == 0
          ? "No matches"
          : $"{_results.Items.Count} result(s)";
      _results.Visible = _results.Items.Count > 0;
      Height = _results.Items.Count == 0
          ? 66
          : Math.Min(226, 88 + (Math.Min(_results.Items.Count, 6) * 22) + SystemInformation.HorizontalScrollBarHeight);
      _resultsHost.Visible = true;
      ResizeResultColumns();
    }
    finally
    {
      _results.EndUpdate();
    }
  }

  private void ResizeResultColumns()
  {
    var availableWidth = Math.Max(0, _results.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
    _results.Columns[0].Width = Math.Max(110, (int)(availableWidth * 0.43));
    _results.Columns[1].Width = 64;
    _results.Columns[2].Width = Math.Max(110, availableWidth - _results.Columns[0].Width - 64);
  }

  private void OnQueryKeyDown(object? sender, KeyEventArgs args)
  {
    if (args.KeyCode == Keys.Escape)
    {
      _query.Clear();
      args.SuppressKeyPress = true;
    }
    else if (args.KeyCode is Keys.Enter or Keys.Down)
    {
      UpdateResults();
      if (_results.Items.Count > 0)
      {
        _results.Focus();
        _results.Items[0].Selected = true;
        _results.Items[0].Focused = true;
      }

      args.SuppressKeyPress = true;
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _searchTimer.Dispose();
      _toolTip.Dispose();
    }

    base.Dispose(disposing);
  }
}

internal sealed record ProjectSearchEntry(
    string Kind,
    string Name,
    string QualifiedName,
    string DisplayName,
    string Location,
    TreeNode Node);
