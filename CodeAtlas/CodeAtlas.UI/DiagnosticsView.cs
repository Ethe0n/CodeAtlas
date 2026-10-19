using CodeAtlas.Lint;
using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class DiagnosticsView : UserControl
{
  private readonly VirtualModelGrid<LintDiagnostic> _grid = new();
  private readonly ComboBox _severity = new() { DropDownStyle = ComboBoxStyle.DropDownList, FormattingEnabled = true, Width = 115 };
  private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, FormattingEnabled = true, Width = 155 };
  private readonly System.Windows.Forms.Label _summary = new()
  {
    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
    Text = "프로젝트 탐색기에서 프로젝트를 선택하세요."
  };
  private readonly TextBox _details = new()
  {
    Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
    ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window,
    BorderStyle = BorderStyle.FixedSingle
  };
  private LintResult? _result;
  private string _projectName = "";
  private readonly Dictionary<(string ProjectId, string SymbolId), (string Label, string QualifiedLabel)> _targets = new();

  public DiagnosticsView()
  {
    Dock = DockStyle.Fill;
    Padding = new Padding(8);
    _severity.Format += (_, args) =>
    {
      if (args.ListItem is LintSeverity value) args.Value = FormatSeverity(value);
    };
    _category.Format += (_, args) =>
    {
      if (args.ListItem is LintCategory value) args.Value = FormatCategory(value);
    };
    _severity.Items.Add("전체 수준");
    _severity.Items.AddRange(Enum.GetValues<LintSeverity>().Cast<object>().ToArray());
    _category.Items.Add("전체 분류");
    _category.Items.AddRange(Enum.GetValues<LintCategory>().Cast<object>().ToArray());
    _severity.SelectedIndex = _category.SelectedIndex = 0;

    var filters = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
      AutoScroll = true, Margin = Padding.Empty
    };
    filters.Controls.Add(_severity);
    filters.Controls.Add(_category);

    _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
    _grid.AddColumn("수준", item => FormatSeverity(item.Severity), 65, 75);
    _grid.AddColumn("규칙", item => item.RuleId, 75, 75);
    _grid.AddColumn("분류", item => FormatCategory(item.Category), 100, 100);
    _grid.AddColumn("진단 내용", item => item.Title, 230, 160);
    _grid.AddColumn("대상", item => FormatTarget(item), 230, 210);
    _grid.AddColumn("프로젝트", item => item.ProjectName, 105, 95);
    _grid.AddColumn("파일", item => item.FilePath ?? "", 135, 100);
    _grid.SelectionChanged += (_, _) => ShowSelectedDetails();
    _grid.CurrentCellChanged += (_, _) => ShowSelectedDetails();
    _grid.ItemActivated += item => DiagnosticActivated?.Invoke(item);
    _grid.CellToolTipTextNeeded += (_, args) =>
    {
      if (args.ColumnIndex == 4 && _grid.GetItem(args.RowIndex) is { } item)
        args.ToolTipText = FormatTarget(item, qualified: true);
    };

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

  public void ShowResult(ProjectStructure project, LintResult result)
  {
    _targets.Clear();
    foreach (var type in project.Types)
    {
      _targets[(project.Id, type.SymbolId)] = ($"타입: {type.Name}", $"타입: {type.FullName}");
      foreach (var method in type.Methods)
      {
        var signatureStart = method.SymbolId.IndexOf('(', StringComparison.Ordinal);
        var signature = signatureStart >= 0 ? method.SymbolId[signatureStart..] : "()";
        _targets[(project.Id, method.SymbolId)] =
            ($"함수: {method.Name}{signature}", $"함수: {type.FullName}.{method.Name}{signature}");
      }
      foreach (var field in type.Fields)
        _targets[(project.Id, field.SymbolId)] = ($"변수: {field.Name}", $"변수: {type.FullName}.{field.Name}");
    }
    _projectName = project.Name;
    _result = result;
    RefreshItems();
  }

  public void ClearResult(string message = "프로젝트 탐색기에서 프로젝트를 선택하세요.")
  {
    _result = null;
    _projectName = "";
    _targets.Clear();
    _grid.SetItems(Array.Empty<LintDiagnostic>());
    _grid.CurrentCell = null;
    _details.Clear();
    _summary.Text = message;
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
    _summary.Text = $"{_projectName} | 진단 {_result.Diagnostics.Count}건 중 {items.Length}건 표시" +
        (_result.IncompleteProjectCount > 0 ? $" | 분석 불완전: {_result.IncompleteProjectCount}개 프로젝트" : "") +
        (_result.Diagnostics.Count == 0 ? " | 현재 v1 규칙에서 발견된 항목이 없습니다." : "");
  }

  private void ShowSelectedDetails()
  {
    var item = _grid.CurrentCell is { } cell ? _grid.GetItem(cell.RowIndex) : null;
    _details.Text = item is null ? "" : string.Join(Environment.NewLine,
    [
      $"{item.RuleId} | {FormatSeverity(item.Severity)} | {FormatCategory(item.Category)}",
      item.Title,
      $"대상: {FormatTarget(item, qualified: true)}",
      $"판단: {(item.Assessment == LintAssessment.ManualReview ? "직접 검토 필요" : "해당 패턴 발견")}",
      "", item.Message, "", "권장 조치", item.Recommendation,
      "", $"프로젝트: {item.ProjectName}", $"심볼 ID: {item.SymbolId ?? "(프로젝트 전체)"}",
      $"위치: {item.FilePath ?? "(알 수 없음)"}:{item.SourceLocation?.StartLine.ToString() ?? "?"}",
      $"참고 자료: {item.HelpLink ?? "CodeAtlas v1 진단 규칙"}"
    ]);
  }

  private string FormatTarget(LintDiagnostic item, bool qualified = false) => item.SymbolId is null
      ? "프로젝트 전체"
      : _targets.TryGetValue((item.ProjectId, item.SymbolId), out var target)
          ? qualified ? target.QualifiedLabel : target.Label
          : $"대상 확인 불가 ({item.SymbolId})";

  private static string FormatSeverity(LintSeverity severity) => severity switch
  {
    LintSeverity.Info => "정보",
    LintSeverity.Warning => "경고",
    _ => severity.ToString()
  };

  private static string FormatCategory(LintCategory category) => category switch
  {
    LintCategory.AnalysisCoverage => "분석 범위",
    LintCategory.Reliability => "안정성",
    LintCategory.Security => "보안",
    LintCategory.Interop => "COM 상호 운용",
    LintCategory.Maintainability => "유지보수성",
    _ => category.ToString()
  };
}
