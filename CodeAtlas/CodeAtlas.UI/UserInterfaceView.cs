using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class UserInterfaceView : UserControl
{
  private readonly UiPreviewCanvas _preview = new() { Dock = DockStyle.Fill };
  private readonly DataGridView _itemsGrid = new()
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

    AddColumn("Control", 120, 110);
    AddColumn("Type", 125, 110);
    AddColumn("Event", 95, 85);
    AddColumn("Handler", 170, 150);

    _itemsGrid.CellDoubleClick += (_, args) =>
    {
      if (args.RowIndex >= 0 &&
          _itemsGrid.Rows[args.RowIndex].Tag is UiItemRow { Handler: not null } row)
      {
        MethodSelected?.Invoke(row.Handler.HandlerMethodSymbolId);
      }
    };
    _itemsGrid.SelectionChanged += (_, _) =>
    {
      if (_synchronizingSelection || _itemsGrid.CurrentRow?.Tag is not UiItemRow row)
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
    _itemsGrid.Rows.Clear();

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
        AddRow(control.Name, ShortTypeName(control.Type), null);
        continue;
      }

      foreach (var handler in handlers)
      {
        AddRow(control.Name, ShortTypeName(control.Type), handler);
      }
    }

    foreach (var handler in type.UiEventHandlers.Where(handler =>
                 !knownControls.Contains(NormalizeControlName(handler.ControlName))))
    {
      AddRow(NormalizeControlName(handler.ControlName), "(unknown)", handler);
    }

    _itemsGrid.ClearSelection();
  }

  public void ClearView()
  {
    _preview.ClearPreview();
    _itemsGrid.Rows.Clear();
  }

  private void AddRow(string controlName, string typeName, UiEventHandlerInfo? handler)
  {
    var rowIndex = _itemsGrid.Rows.Add(
        controlName,
        typeName,
        handler?.EventName ?? string.Empty,
        handler?.HandlerMethodName ?? string.Empty);
    _itemsGrid.Rows[rowIndex].Tag = new UiItemRow(controlName, handler);
  }

  private void SelectControlRow(string controlName)
  {
    var row = _itemsGrid.Rows
        .Cast<DataGridViewRow>()
        .FirstOrDefault(candidate =>
            candidate.Tag is UiItemRow item &&
            string.Equals(item.ControlName, controlName, StringComparison.OrdinalIgnoreCase));
    if (row is null)
    {
      return;
    }

    _synchronizingSelection = true;
    _itemsGrid.ClearSelection();
    row.Selected = true;
    _itemsGrid.CurrentCell = row.Cells[0];
    _synchronizingSelection = false;
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

  private void AddColumn(string headerText, float fillWeight, int minimumWidth)
  {
    _itemsGrid.Columns.Add(new DataGridViewTextBoxColumn
    {
      HeaderText = headerText,
      FillWeight = fillWeight,
      MinimumWidth = minimumWidth,
      SortMode = DataGridViewColumnSortMode.Automatic
    });
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

  private sealed record UiItemRow(string ControlName, UiEventHandlerInfo? Handler);
}
