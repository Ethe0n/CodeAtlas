using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class UserInterfaceView : UserControl
{
  private readonly UiPreviewCanvas _preview = new() { Dock = DockStyle.Fill };
  private readonly VirtualModelGrid<UiItemRow> _itemsGrid = new()
  {
    AllowUserToAddRows = false,
    AllowUserToDeleteRows = false,
    AllowUserToResizeRows = false,
    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    BackgroundColor = SystemColors.Window,
    BorderStyle = BorderStyle.None,
    CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
    ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
    ColumnHeadersHeight = 34,
    ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
    Dock = DockStyle.Fill,
    EnableHeadersVisualStyles = false,
    MultiSelect = false,
    ReadOnly = true,
    RowHeadersVisible = false,
    RowTemplate = { Height = 30 },
    SelectionMode = DataGridViewSelectionMode.FullRowSelect
  };
  private bool _synchronizingSelection;

  public UserInterfaceView()
  {
    Dock = DockStyle.Fill;
    BackColor = SystemColors.Window;

    _itemsGrid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
    _itemsGrid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
    _itemsGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
    _itemsGrid.DefaultCellStyle.BackColor = SystemColors.Window;
    _itemsGrid.DefaultCellStyle.ForeColor = SystemColors.ControlText;
    _itemsGrid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
    _itemsGrid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
    _itemsGrid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
    _itemsGrid.GridColor = SystemColors.ControlLight;

    _itemsGrid.AddColumn("Control", row => row.ControlName, 120, 110);
    _itemsGrid.AddColumn("Type", row => row.TypeName, 125, 110);
    _itemsGrid.AddColumn("Event", row => row.Handler?.EventName ?? string.Empty, 95, 85);
    _itemsGrid.AddColumn("Handler", row => row.Handler?.HandlerMethodName ?? string.Empty, 170, 150);

    _itemsGrid.ItemActivated += row =>
    {
      if (row.Handler is not null)
      {
        MethodSelected?.Invoke(row.Handler.HandlerMethodSymbolId);
      }
    };
    _itemsGrid.SelectionChanged += (_, _) =>
    {
      if (_synchronizingSelection ||
          _itemsGrid.GetItem(_itemsGrid.CurrentCell?.RowIndex ?? -1) is not { } row)
      {
        return;
      }

      _preview.SelectControl(row.ControlName);
    };
    _preview.ControlSelected += SelectControlRow;

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      Dock = DockStyle.Fill,
      RowCount = 2,
      Padding = new Padding(20)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
    layout.Controls.Add(CreateSection("UI Preview", _preview), 0, 0);
    layout.Controls.Add(CreateSection("Controls and Events", _itemsGrid), 0, 1);
    Controls.Add(layout);
  }

  public event Action<string>? MethodSelected;

  public void ShowType(TypeStructure type)
  {
    _preview.ShowType(type);
    var rows = new List<UiItemRow>();

    var handlersByControl = type.UiEventHandlers
        .ToLookup(handler => NormalizeControlName(handler.ControlName), StringComparer.OrdinalIgnoreCase);
    var knownControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var control in type.UiControls
                 .OrderBy(control => control.TabIndex ?? int.MaxValue)
                 .ThenBy(control => control.Name, StringComparer.OrdinalIgnoreCase))
    {
      knownControls.Add(control.Name);
      var handlers = handlersByControl[control.Name].ToArray();
      if (handlers.Length == 0)
      {
        rows.Add(new UiItemRow(control.Name, ShortTypeName(control.Type), null));
        continue;
      }

      foreach (var handler in handlers)
      {
        rows.Add(new UiItemRow(control.Name, ShortTypeName(control.Type), handler));
      }
    }

    foreach (var handler in type.UiEventHandlers.Where(handler =>
                 !knownControls.Contains(NormalizeControlName(handler.ControlName))))
    {
      rows.Add(new UiItemRow(NormalizeControlName(handler.ControlName), "(unknown)", handler));
    }

    _itemsGrid.SetItems(rows);
  }

  public void ClearView()
  {
    _preview.ClearPreview();
    _itemsGrid.SetItems(Array.Empty<UiItemRow>());
  }

  private void SelectControlRow(string controlName)
  {
    var rowIndex = _itemsGrid.FindItemIndex(item =>
        string.Equals(item.ControlName, controlName, StringComparison.OrdinalIgnoreCase));
    if (rowIndex < 0)
    {
      return;
    }

    _synchronizingSelection = true;
    try
    {
      _itemsGrid.ClearSelection();
      _itemsGrid.CurrentCell = _itemsGrid.Rows[rowIndex].Cells[0];
      _itemsGrid.Rows[rowIndex].Selected = true;
    }
    finally
    {
      _synchronizingSelection = false;
    }
  }

  private Control CreateSection(string title, Control content)
  {
    var panel = new Panel
    {
      Dock = DockStyle.Fill,
      Margin = new Padding(0, 0, 0, 12)
    };
    var heading = new System.Windows.Forms.Label
    {
      AutoSize = true,
      Dock = DockStyle.Top,
      Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
      ForeColor = Color.FromArgb(36, 91, 138),
      Padding = new Padding(0, 0, 0, 7),
      Text = title
    };
    panel.Controls.Add(content);
    panel.Controls.Add(heading);
    return panel;
  }

  private static string NormalizeControlName(string controlName)
  {
    var separatorIndex = controlName.LastIndexOf('.');
    return separatorIndex >= 0 && separatorIndex < controlName.Length - 1
        ? controlName[(separatorIndex + 1)..]
        : controlName;
  }

  private static string ShortTypeName(string typeName)
  {
    var index = typeName.LastIndexOf('.');
    return index >= 0 && index < typeName.Length - 1
        ? typeName[(index + 1)..]
        : typeName;
  }

  private sealed record UiItemRow(string ControlName, string TypeName, UiEventHandlerInfo? Handler);
}
