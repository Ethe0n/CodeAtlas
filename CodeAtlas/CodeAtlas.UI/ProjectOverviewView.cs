using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class ProjectOverviewView : UserControl
{
  private static readonly Color AccentColor = Color.FromArgb(36, 91, 138);
  private static readonly Color SecondaryTextColor = Color.FromArgb(96, 103, 112);
  private static readonly Color DividerColor = Color.FromArgb(224, 227, 231);
  private static readonly Color SummaryBackColor = Color.FromArgb(246, 248, 250);
  private static readonly Color SuccessColor = Color.FromArgb(38, 116, 72);
  private static readonly Color WarningColor = Color.FromArgb(174, 108, 18);
  private static readonly Color ErrorColor = Color.FromArgb(180, 54, 54);

  private readonly Panel _scrollPanel = new()
  {
    AutoScroll = true,
    BackColor = SystemColors.Window,
    Dock = DockStyle.Fill
  };

  private readonly TableLayoutPanel _content = new()
  {
    AutoSize = true,
    AutoSizeMode = AutoSizeMode.GrowAndShrink,
    BackColor = SystemColors.Window,
    ColumnCount = 1,
    Dock = DockStyle.Top,
    Padding = new Padding(28, 24, 28, 32)
  };

  public ProjectOverviewView()
  {
    Dock = DockStyle.Fill;
    BackColor = SystemColors.Window;
    _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _scrollPanel.Controls.Add(_content);
    Controls.Add(_scrollPanel);
  }

  public event Action<string>? TypeSelected;

  public void ShowProject(ProjectStructure project)
  {
    var types = project.Types;
    var methods = types.SelectMany(type => type.Methods).Where(method => !method.IsGenerated).ToArray();
    var generatedMethods = types
        .SelectMany(type => type.GeneratedMethods)
        .Concat(types.SelectMany(type => type.Methods).Where(method => method.IsGenerated))
        .Select(method => method.SymbolId)
        .Distinct(StringComparer.Ordinal)
        .Count();
    var fields = types.SelectMany(type => type.Fields).Count(field => !field.IsGenerated);
    var properties = types.SelectMany(type => type.Properties).Count(property => !property.IsGenerated);
    var controlCount = types.Sum(type => type.UiControls.Count);
    var eventCount = types.Sum(type => type.UiEventHandlers.Count);
    var topLevelTypes = types.Count(type => type.ContainingTypeSymbolId is null);
    var nestedTypes = types.Count - topLevelTypes;
    var internalCalls = project.Calls.Count(call => call.IsProjectInternal);
    var externalCalls = project.Calls.Count(call => !call.IsProjectInternal);
    var fieldReads = project.FieldUsages.Count(usage =>
        usage.UsageKind is FieldUsageKind.Read or FieldUsageKind.ReadWrite);
    var fieldWrites = project.FieldUsages.Count(usage =>
        usage.UsageKind is FieldUsageKind.Write or FieldUsageKind.ReadWrite);
    var dependencyRelations = project.TypeDependencies.Count;
    var dependencyPairs = project.TypeDependencies
        .Select(dependency => (dependency.SourceTypeSymbolId, dependency.TargetTypeSymbolId))
        .Distinct()
        .Count();

    _content.SuspendLayout();
    ClearOverview();

    AddContent(CreateHeader(project));
    AddContent(CreateStructureSummary(
        types.Count,
        methods.Length,
        fields,
        properties,
        controlCount,
        eventCount));
    AddContent(CreateCompositionLine(topLevelTypes, nestedTypes, generatedMethods));
    AddContent(CreateAnalysisSection(
        internalCalls,
        externalCalls,
        fieldReads,
        fieldWrites,
        dependencyRelations,
        dependencyPairs));
    if (types.Count > 0)
    {
      AddContent(CreateTypesSection(types));
    }

    if (project.Diagnostics.Count > 0)
    {
      AddContent(CreateDiagnosticsSection(project.Diagnostics));
    }

    _content.ResumeLayout(performLayout: true);
    _scrollPanel.AutoScrollPosition = Point.Empty;
  }

  public void ClearOverview()
  {
    _content.Controls.Clear();
    _content.RowStyles.Clear();
    _content.RowCount = 0;
  }

  private Control CreateHeader(ProjectStructure project)
  {
    var header = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 1,
      Dock = DockStyle.Top,
      Margin = new Padding(0, 0, 0, 18)
    };
    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      Font = new Font(Font.FontFamily, 19, FontStyle.Bold),
      ForeColor = SystemColors.ControlText,
      Margin = new Padding(0),
      Text = project.Name
    });
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      Font = new Font(Font, FontStyle.Bold),
      ForeColor = GetStatusColor(project.AnalysisStatus),
      Margin = new Padding(1, 7, 0, 0),
      Text = $"{FormatLanguage(project.Language)}  |  {FormatStatus(project.AnalysisStatus)}"
    });
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoEllipsis = true,
      Dock = DockStyle.Top,
      ForeColor = SecondaryTextColor,
      Height = 24,
      Margin = new Padding(1, 7, 0, 0),
      Text = project.FilePath ?? "Project file is not available",
      TextAlign = ContentAlignment.MiddleLeft
    });
    return header;
  }

  private Control CreateStructureSummary(
      int typeCount,
      int methodCount,
      int fieldCount,
      int propertyCount,
      int controlCount,
      int eventCount)
  {
    var summary = new TableLayoutPanel
    {
      BackColor = SummaryBackColor,
      ColumnCount = 6,
      Dock = DockStyle.Top,
      Height = 76,
      Margin = new Padding(0),
      Padding = new Padding(8)
    };
    for (var index = 0; index < summary.ColumnCount; index++)
    {
      summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / summary.ColumnCount));
    }

    summary.Controls.Add(CreateMetric(typeCount, "Types"), 0, 0);
    summary.Controls.Add(CreateMetric(methodCount, "Methods"), 1, 0);
    summary.Controls.Add(CreateMetric(fieldCount, "Fields"), 2, 0);
    summary.Controls.Add(CreateMetric(propertyCount, "Properties"), 3, 0);
    summary.Controls.Add(CreateMetric(controlCount, "Controls"), 4, 0);
    summary.Controls.Add(CreateMetric(eventCount, "Events"), 5, 0);
    return summary;
  }

  private Control CreateCompositionLine(int topLevelTypes, int nestedTypes, int generatedMethods)
  {
    return new System.Windows.Forms.Label
    {
      AutoSize = true,
      ForeColor = SecondaryTextColor,
      Margin = new Padding(2, 8, 0, 2),
      Text = $"{topLevelTypes} top-level types  |  {nestedTypes} nested types  |  {generatedMethods} generated methods"
    };
  }

  private Control CreateAnalysisSection(
      int internalCalls,
      int externalCalls,
      int fieldReads,
      int fieldWrites,
      int dependencyRelations,
      int dependencyPairs)
  {
    var metrics = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      BackColor = SummaryBackColor,
      ColumnCount = 3,
      Dock = DockStyle.Top,
      Padding = new Padding(8)
    };
    for (var index = 0; index < metrics.ColumnCount; index++)
    {
      metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / metrics.ColumnCount));
    }

    metrics.Controls.Add(CreateAnalysisMetric(internalCalls, "Internal calls"), 0, 0);
    metrics.Controls.Add(CreateAnalysisMetric(externalCalls, "External calls"), 1, 0);
    metrics.Controls.Add(CreateAnalysisMetric(dependencyRelations, "Dependencies"), 2, 0);
    metrics.Controls.Add(CreateAnalysisMetric(fieldReads, "Field reads"), 0, 1);
    metrics.Controls.Add(CreateAnalysisMetric(fieldWrites, "Field writes"), 1, 1);
    metrics.Controls.Add(CreateAnalysisMetric(dependencyPairs, "Type pairs"), 2, 1);
    return CreateSection("Analysis", metrics);
  }

  private Control CreateTypesSection(IReadOnlyList<TypeStructure> types)
  {
    var table = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 6,
      Dock = DockStyle.Top,
      Margin = new Padding(0)
    };
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11));
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));
    AddTypeTableRow(table, "Name", "Kind", "Methods", "Fields", "Properties", "UI", true, null);

    var duplicateNames = types
        .GroupBy(type => type.Name, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToHashSet(StringComparer.Ordinal);
    foreach (var entry in OrderTypes(types))
    {
      var type = entry.Type;
      var displayName = duplicateNames.Contains(type.Name) ? type.FullName : type.Name;
      displayName = $"{new string(' ', entry.Depth * 3)}{displayName}";
      AddTypeTableRow(
          table,
          displayName,
          type.Kind,
          FormatCount(type.Methods.Count(method => !method.IsGenerated)),
          FormatCount(type.Fields.Count(field => !field.IsGenerated)),
          FormatCount(type.Properties.Count(property => !property.IsGenerated)),
          FormatCount(type.UiControls.Count),
          false,
          () => TypeSelected?.Invoke(type.SymbolId));
    }

    return CreateSection("Types", table);
  }

  private Control CreateDiagnosticsSection(IReadOnlyList<ProjectAnalysisDiagnostic> diagnostics)
  {
    var list = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 2,
      Dock = DockStyle.Top
    };
    list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
    list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

    foreach (var diagnostic in diagnostics)
    {
      var color = diagnostic.Severity switch
      {
        ProjectAnalysisDiagnosticSeverity.Error => ErrorColor,
        ProjectAnalysisDiagnosticSeverity.Warning => WarningColor,
        _ => AccentColor
      };
      list.Controls.Add(new System.Windows.Forms.Label
      {
        AutoSize = true,
        Font = new Font(Font, FontStyle.Bold),
        ForeColor = color,
        Margin = new Padding(0, 7, 12, 7),
        Text = diagnostic.Stage
      });
      list.Controls.Add(new System.Windows.Forms.Label
      {
        AutoSize = true,
        Dock = DockStyle.Fill,
        ForeColor = SystemColors.ControlText,
        Margin = new Padding(0, 7, 0, 7),
        MaximumSize = new Size(760, 0),
        Text = diagnostic.Message
      });
    }

    return CreateSection("Diagnostics", list);
  }

  private Control CreateMetric(int value, string label)
  {
    var metric = new TableLayoutPanel
    {
      ColumnCount = 1,
      Dock = DockStyle.Fill,
      Margin = new Padding(0)
    };
    metric.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    metric.Controls.Add(new System.Windows.Forms.Label
    {
      Dock = DockStyle.Fill,
      Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
      ForeColor = SystemColors.ControlText,
      Margin = new Padding(0),
      Text = value.ToString(),
      TextAlign = ContentAlignment.BottomCenter
    });
    metric.Controls.Add(new System.Windows.Forms.Label
    {
      Dock = DockStyle.Fill,
      ForeColor = SecondaryTextColor,
      Margin = new Padding(0),
      Text = label,
      TextAlign = ContentAlignment.TopCenter
    });
    return metric;
  }

  private Control CreateAnalysisMetric(int value, string label)
  {
    var metric = new TableLayoutPanel
    {
      ColumnCount = 2,
      Dock = DockStyle.Fill,
      Height = 38,
      Margin = new Padding(10, 3, 10, 3)
    };
    metric.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    metric.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
    metric.Controls.Add(new System.Windows.Forms.Label
    {
      Dock = DockStyle.Fill,
      ForeColor = SecondaryTextColor,
      Margin = new Padding(0),
      Text = label,
      TextAlign = ContentAlignment.MiddleLeft
    }, 0, 0);
    metric.Controls.Add(new System.Windows.Forms.Label
    {
      Dock = DockStyle.Fill,
      Font = new Font(Font, FontStyle.Bold),
      ForeColor = SystemColors.ControlText,
      Margin = new Padding(0),
      Text = value.ToString(),
      TextAlign = ContentAlignment.MiddleRight
    }, 1, 0);
    return metric;
  }

  private Control CreateSection(string title, Control content)
  {
    var section = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 1,
      Dock = DockStyle.Top,
      Margin = new Padding(0, 18, 0, 0)
    };
    section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    section.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
      ForeColor = AccentColor,
      Margin = new Padding(0, 0, 0, 6),
      Text = title
    });
    section.Controls.Add(new Panel
    {
      BackColor = DividerColor,
      Dock = DockStyle.Top,
      Height = 1,
      Margin = new Padding(0, 0, 0, 7)
    });
    section.Controls.Add(content);
    return section;
  }

  private void AddTypeTableRow(
      TableLayoutPanel table,
      string name,
      string kind,
      string methods,
      string fields,
      string properties,
      string uiControls,
      bool isHeader,
      Action? action)
  {
    var rowIndex = table.RowCount++;
    table.RowStyles.Add(new RowStyle(SizeType.Absolute, isHeader ? 30 : 34));
    var values = new[] { name, kind, methods, fields, properties, uiControls };
    for (var columnIndex = 0; columnIndex < values.Length; columnIndex++)
    {
      var label = new System.Windows.Forms.Label
      {
        AutoEllipsis = true,
        Cursor = action is null ? Cursors.Default : Cursors.Hand,
        Dock = DockStyle.Fill,
        Font = isHeader ? new Font(Font, FontStyle.Bold) : Font,
        ForeColor = isHeader ? SecondaryTextColor : SystemColors.ControlText,
        Margin = new Padding(columnIndex == 0 ? 3 : 8, 0, 3, 0),
        Text = values[columnIndex],
        TextAlign = columnIndex < 2 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight
      };
      if (action is not null)
      {
        label.DoubleClick += (_, _) => action();
      }

      table.Controls.Add(label, columnIndex, rowIndex);
    }

    if (!isHeader)
    {
      var divider = new Panel
      {
        BackColor = DividerColor,
        Dock = DockStyle.Bottom,
        Height = 1,
        Margin = new Padding(0)
      };
      table.Controls.Add(divider, 0, rowIndex);
      table.SetColumnSpan(divider, table.ColumnCount);
    }
  }

  private void AddContent(Control control)
  {
    var row = _content.RowCount++;
    _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    _content.Controls.Add(control, 0, row);
  }

  private static IReadOnlyList<TypeListEntry> OrderTypes(IReadOnlyList<TypeStructure> types)
  {
    var result = new List<TypeListEntry>();
    var added = new HashSet<string>(StringComparer.Ordinal);
    var nestedByParent = types
        .Where(type => type.ContainingTypeSymbolId is not null)
        .GroupBy(type => type.ContainingTypeSymbolId!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.OrderBy(type => type.FullName).ToArray(), StringComparer.Ordinal);

    void Add(TypeStructure type, int depth)
    {
      if (!added.Add(type.SymbolId))
      {
        return;
      }

      result.Add(new TypeListEntry(type, depth));
      if (nestedByParent.TryGetValue(type.SymbolId, out var nestedTypes))
      {
        foreach (var nestedType in nestedTypes)
        {
          Add(nestedType, depth + 1);
        }
      }
    }

    foreach (var type in types
                 .Where(type => type.ContainingTypeSymbolId is null)
                 .OrderBy(type => type.FullName, StringComparer.Ordinal))
    {
      Add(type, 0);
    }

    foreach (var type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
    {
      Add(type, 0);
    }

    return result;
  }

  private static string FormatCount(int value)
  {
    return value == 0 ? "-" : value.ToString();
  }

  private static string FormatLanguage(string language)
  {
    return string.Equals(language, "Visual Basic", StringComparison.OrdinalIgnoreCase)
        ? "Visual Basic"
        : language;
  }

  private static string FormatStatus(ProjectAnalysisStatus status)
  {
    return status switch
    {
      ProjectAnalysisStatus.Full => "Full analysis",
      ProjectAnalysisStatus.Partial => "Partial analysis",
      ProjectAnalysisStatus.Failed => "Analysis failed",
      _ => status.ToString()
    };
  }

  private static Color GetStatusColor(ProjectAnalysisStatus status)
  {
    return status switch
    {
      ProjectAnalysisStatus.Full => SuccessColor,
      ProjectAnalysisStatus.Partial => WarningColor,
      ProjectAnalysisStatus.Failed => ErrorColor,
      _ => AccentColor
    };
  }

  private sealed record TypeListEntry(TypeStructure Type, int Depth);
}
