using CodeAtlas.Lint;

namespace CodeAtlas.UI;

internal sealed class DiagnosticsView : UserControl
{
  private readonly VirtualModelGrid<LintDiagnostic> _grid = new();
  private readonly ComboBox _severity = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
  private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155 };
  private readonly System.Windows.Forms.Label _summary = new()
  {
    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "No solution analyzed."
  };
  private readonly TextBox _details = new()
  {
    Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
    ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window,
    BorderStyle = BorderStyle.FixedSingle
  };
  private LintResult? _result;

  public DiagnosticsView()
  {
    Dock = DockStyle.Fill;
    Padding = new Padding(8);
    _severity.Items.Add("All levels");
    _severity.Items.AddRange(Enum.GetValues<LintSeverity>().Cast<object>().ToArray());
    _category.Items.Add("All categories");
    _category.Items.AddRange(Enum.GetValues<LintCategory>().Cast<object>().ToArray());
    _severity.SelectedIndex = _category.SelectedIndex = 0;

    var filters = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
      AutoScroll = true, Margin = Padding.Empty
    };
    filters.Controls.Add(_severity);
    filters.Controls.Add(_category);

    _grid.AddColumn("Level", item => item.Severity.ToString(), 65, 65);
    _grid.AddColumn("Rule", item => item.RuleId, 75, 75);
    _grid.AddColumn("Category", item => item.Category.ToString(), 100, 100);
    _grid.AddColumn("Finding", item => item.Title, 230, 160);
    _grid.AddColumn("Project", item => item.ProjectName, 105, 95);
    _grid.AddColumn("File", item => item.FilePath ?? "", 135, 100);
    _grid.AddColumn("Line", item => item.SourceLocation?.StartLine, 50, 50);
    _grid.SelectionChanged += (_, _) => ShowSelectedDetails();
    _grid.CurrentCellChanged += (_, _) => ShowSelectedDetails();
    _grid.ItemActivated += item => DiagnosticActivated?.Invoke(item);

    var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
    layout.Controls.Add(filters, 0, 0);
    layout.Controls.Add(_summary, 0, 1);
    layout.Controls.Add(_grid, 0, 2);
    layout.Controls.Add(_details, 0, 3);
    Controls.Add(layout);
    _severity.SelectedIndexChanged += (_, _) => RefreshItems();
    _category.SelectedIndexChanged += (_, _) => RefreshItems();
  }

  public event Action<LintDiagnostic>? DiagnosticActivated;

  public void ShowResult(LintResult result)
  {
    _result = result;
    RefreshItems();
  }

  private void RefreshItems()
  {
    if (_result is null) return;
    var items = _result.Diagnostics.Where(item =>
        (_severity.SelectedItem is not LintSeverity level || item.Severity == level) &&
        (_category.SelectedItem is not LintCategory category || item.Category == category)).ToArray();
    _grid.SetItems(items);
    _grid.CurrentCell = null;
    _details.Clear();
    _summary.Text = $"{items.Length} / {_result.Diagnostics.Count} findings; {_result.ProjectCount} project(s)" +
        (_result.IncompleteProjectCount > 0 ? $"; {_result.IncompleteProjectCount} incomplete" : "") +
        (_result.Diagnostics.Count == 0 ? ". No findings from the v1 rules." : "");
  }

  private void ShowSelectedDetails()
  {
    var item = _grid.CurrentCell is { } cell ? _grid.GetItem(cell.RowIndex) : null;
    _details.Text = item is null ? "" : string.Join(Environment.NewLine,
    [
      $"{item.RuleId} | {item.Severity} | {item.Category}",
      item.Title,
      $"Assessment: {(item.Assessment == LintAssessment.ManualReview ? "Manual review required" : "Pattern detected")}",
      "", item.Message, "", "Recommendation", item.Recommendation,
      "", $"Project: {item.ProjectName}", $"Symbol: {item.SymbolId ?? "(project)"}",
      $"Location: {item.FilePath ?? "(unknown)"}:{item.SourceLocation?.StartLine.ToString() ?? "?"}",
      $"Reference: {item.HelpLink ?? "CodeAtlas v1 rule"}"
    ]);
  }
}
