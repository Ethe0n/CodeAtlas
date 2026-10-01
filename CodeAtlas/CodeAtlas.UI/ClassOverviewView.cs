using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class ClassOverviewView : UserControl
{
  private static readonly Color AccentColor = Color.FromArgb(36, 91, 138);
  private static readonly Color SecondaryTextColor = Color.FromArgb(96, 103, 112);
  private static readonly Color DividerColor = Color.FromArgb(224, 227, 231);
  private static readonly Color SummaryBackColor = Color.FromArgb(246, 248, 250);

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

  public ClassOverviewView()
  {
    Dock = DockStyle.Fill;
    BackColor = SystemColors.Window;
    _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _scrollPanel.Controls.Add(_content);
    Controls.Add(_scrollPanel);
  }

  public event Action<string>? MethodSelected;

  public event Action<string>? FieldSelected;

  public void ShowType(TypeStructure type)
  {
    var methods = type.Methods.Where(method => !method.IsGenerated).ToArray();
    var fields = type.Fields.Where(field => !field.IsGenerated).ToArray();
    var properties = type.Properties.Where(property => !property.IsGenerated).ToArray();

    _content.SuspendLayout();
    ClearOverview();

    AddContent(CreateHeader(type));
    AddContent(CreateSummaryStrip(
        methods.Length,
        fields.Length,
        properties.Length,
        type.UiControls.Count,
        type.UiEventHandlers.Count));
    if (methods.Length > 0)
    {
      AddContent(CreateMethodsSection(methods));
    }

    if (fields.Length > 0)
    {
      AddContent(CreateFieldsSection(fields));
    }

    if (properties.Length > 0)
    {
      AddContent(CreatePropertiesSection(properties));
    }

    if (type.UiControls.Count > 0 || type.UiEventHandlers.Count > 0)
    {
      AddContent(CreateUserInterfaceSection(type.UiControls, type.UiEventHandlers));
    }

    AddContent(CreateSourcesSection(type.FilePaths));

    _content.ResumeLayout(performLayout: true);
    _scrollPanel.AutoScrollPosition = Point.Empty;
  }

  public void ClearOverview()
  {
    _content.Controls.Clear();
    _content.RowStyles.Clear();
    _content.RowCount = 0;
  }

  private Control CreateHeader(TypeStructure type)
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
      Text = type.Name
    });
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      ForeColor = SecondaryTextColor,
      Margin = new Padding(1, 5, 0, 0),
      Text = type.FullName
    });
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      Font = new Font(Font, FontStyle.Bold),
      ForeColor = AccentColor,
      Margin = new Padding(1, 11, 0, 0),
      Text = FormatTypeDescription(type)
    });
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      ForeColor = SecondaryTextColor,
      Margin = new Padding(1, 7, 0, 0),
      Text = $"Inherits {FormatOptional(type.BaseType)}"
    });

    return header;
  }

  private Control CreateSummaryStrip(
      int methodCount,
      int fieldCount,
      int propertyCount,
      int controlCount,
      int eventCount)
  {
    var summary = new TableLayoutPanel
    {
      BackColor = SummaryBackColor,
      ColumnCount = 5,
      Dock = DockStyle.Top,
      Height = 76,
      Margin = new Padding(0, 0, 0, 10),
      Padding = new Padding(8, 8, 8, 8)
    };

    for (var index = 0; index < summary.ColumnCount; index++)
    {
      summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
    }

    summary.Controls.Add(CreateMetric(methodCount, "Methods"), 0, 0);
    summary.Controls.Add(CreateMetric(fieldCount, "Fields"), 1, 0);
    summary.Controls.Add(CreateMetric(propertyCount, "Properties"), 2, 0);
    summary.Controls.Add(CreateMetric(controlCount, "Controls"), 3, 0);
    summary.Controls.Add(CreateMetric(eventCount, "Events"), 4, 0);
    return summary;
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

  private Control CreateMethodsSection(IReadOnlyList<MethodStructure> methods)
  {
    var rows = methods.Select(method => CreateMemberRow(
        FormatMethodSignature(method),
        method.Accessibility,
        () => MethodSelected?.Invoke(method.SymbolId),
        monospace: true));
    return CreateSection("Methods", rows);
  }

  private Control CreateFieldsSection(IReadOnlyList<FieldStructure> fields)
  {
    var rows = fields.Select(field => CreateMemberRow(
        field.Name,
        $"{ShortTypeName(field.Type)}  |  {field.Accessibility}",
        () => FieldSelected?.Invoke(field.SymbolId),
        monospace: true));
    return CreateSection("Fields", rows);
  }

  private Control CreatePropertiesSection(IReadOnlyList<PropertyStructure> properties)
  {
    var rows = properties.Select(property => CreateMemberRow(
        property.Name,
        $"{ShortTypeName(property.Type)}  |  {property.Accessibility}",
        action: null,
        monospace: true));
    return CreateSection("Properties", rows);
  }

  private Control CreateUserInterfaceSection(
      IReadOnlyList<UiControlInfo> controls,
      IReadOnlyList<UiEventHandlerInfo> eventHandlers)
  {
    var rows = controls
        .Select(control => CreateMemberRow(
            control.Name,
            ShortTypeName(control.Type),
            action: null,
            monospace: true))
        .Concat(eventHandlers.Select(handler => CreateMemberRow(
            $"{handler.ControlName}.{handler.EventName}",
            $"-> {handler.HandlerMethodName}",
            () => MethodSelected?.Invoke(handler.HandlerMethodSymbolId),
            monospace: true)));
    return CreateSection("User Interface", rows);
  }

  private Control CreateSourcesSection(IReadOnlyList<string> filePaths)
  {
    var rows = filePaths.Select(path => CreateMemberRow(
        Path.GetFileName(path),
        path,
        action: null,
        monospace: false));
    return CreateSection("Source Files", rows);
  }

  private Control CreateSection(string title, IEnumerable<Control> rows)
  {
    var section = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 1,
      Dock = DockStyle.Top,
      Margin = new Padding(0, 14, 0, 0)
    };
    section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

    var heading = new System.Windows.Forms.Label
    {
      AutoSize = true,
      Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
      ForeColor = AccentColor,
      Margin = new Padding(0, 0, 0, 6),
      Text = title
    };
    section.Controls.Add(heading);
    section.Controls.Add(new Panel
    {
      BackColor = DividerColor,
      Dock = DockStyle.Top,
      Height = 1,
      Margin = new Padding(0, 0, 0, 1)
    });

    var hasRows = false;
    foreach (var row in rows)
    {
      section.Controls.Add(row);
      hasRows = true;
    }

    if (!hasRows)
    {
      section.Controls.Add(new System.Windows.Forms.Label
      {
        AutoSize = true,
        ForeColor = SecondaryTextColor,
        Margin = new Padding(2, 10, 0, 4),
        Text = "None"
      });
    }

    return section;
  }

  private Control CreateMemberRow(
      string primaryText,
      string secondaryText,
      Action? action,
      bool monospace)
  {
    var row = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 2,
      Cursor = action is null ? Cursors.Default : Cursors.Hand,
      Dock = DockStyle.Top,
      Margin = new Padding(0),
      Padding = new Padding(2, 7, 2, 7)
    };
    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

    var primary = new System.Windows.Forms.Label
    {
      AutoEllipsis = true,
      Dock = DockStyle.Fill,
      Font = monospace ? new Font(FontFamily.GenericMonospace, Font.Size) : Font,
      ForeColor = SystemColors.ControlText,
      Margin = new Padding(0),
      Text = primaryText,
      TextAlign = ContentAlignment.MiddleLeft
    };
    var secondary = new System.Windows.Forms.Label
    {
      AutoEllipsis = true,
      Dock = DockStyle.Fill,
      ForeColor = SecondaryTextColor,
      Margin = new Padding(12, 0, 0, 0),
      Text = secondaryText,
      TextAlign = ContentAlignment.MiddleRight
    };

    row.Controls.Add(primary, 0, 0);
    row.Controls.Add(secondary, 1, 0);

    if (action is not null)
    {
      AttachActivation(row, action);
      AttachActivation(primary, action);
      AttachActivation(secondary, action);
    }

    return row;
  }

  private static void AttachActivation(Control control, Action action)
  {
    control.Cursor = Cursors.Hand;
    control.DoubleClick += (_, _) => action();
  }

  private void AddContent(Control control)
  {
    var row = _content.RowCount++;
    _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    _content.Controls.Add(control, 0, row);
  }

  private static string FormatTypeDescription(TypeStructure type)
  {
    var values = new List<string>();
    if (!string.IsNullOrWhiteSpace(type.Accessibility))
    {
      values.Add(type.Accessibility);
    }

    if (!string.IsNullOrWhiteSpace(type.Kind))
    {
      values.Add(type.Kind);
    }

    if (type.FilePaths.Count > 1)
    {
      values.Add("Partial");
    }

    return values.Count == 0 ? "Type" : string.Join("  |  ", values);
  }

  private static string FormatMethodSignature(MethodStructure method)
  {
    var signatureStart = method.SymbolId.IndexOf('(', StringComparison.Ordinal);
    return signatureStart >= 0
        ? $"{method.Name}{method.SymbolId[signatureStart..]}"
        : $"{method.Name}()";
  }

  private static string ShortTypeName(string typeName)
  {
    var index = typeName.LastIndexOf('.');
    return index >= 0 && index < typeName.Length - 1
        ? typeName[(index + 1)..]
        : typeName;
  }

  private static string FormatOptional(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? "(not available)" : value;
  }
}
