using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class FieldListView : UserControl
{
  private readonly System.Windows.Forms.Label _heading = new()
  {
    AutoSize = true,
    Dock = DockStyle.Top,
    Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold),
    Margin = new Padding(0),
    Padding = new Padding(0, 0, 0, 12),
    Text = "Fields"
  };

  private readonly DataGridView _fieldsGrid = new()
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

  public FieldListView()
  {
    Dock = DockStyle.Fill;
    BackColor = SystemColors.Window;
    Padding = new Padding(20);

    _fieldsGrid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
    _fieldsGrid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
    _fieldsGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
    _fieldsGrid.DefaultCellStyle.BackColor = SystemColors.Window;
    _fieldsGrid.DefaultCellStyle.ForeColor = SystemColors.ControlText;
    _fieldsGrid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
    _fieldsGrid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
    _fieldsGrid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
    _fieldsGrid.GridColor = SystemColors.ControlLight;

    AddColumn("Name", 115, 115);
    AddColumn("Type", 145, 140);
    AddColumn("Accessibility", 90, 95);
    AddColumn("Modifiers", 105, 105);
    AddColumn("Initializer", 170, 150);

    _fieldsGrid.CellDoubleClick += (_, args) =>
    {
      if (args.RowIndex >= 0 && _fieldsGrid.Rows[args.RowIndex].Tag is FieldStructure field)
      {
        FieldActivated?.Invoke(field);
      }
    };

    Controls.Add(_fieldsGrid);
    Controls.Add(_heading);
  }

  public event Action<FieldStructure>? FieldActivated;

  public void ShowFields(IReadOnlyList<FieldStructure> fields)
  {
    _fieldsGrid.Rows.Clear();
    _heading.Text = $"Fields ({fields.Count})";

    foreach (var field in fields)
    {
      var rowIndex = _fieldsGrid.Rows.Add(
          field.Name,
          field.Type,
          field.Accessibility,
          FormatModifiers(field),
          field.Initializer ?? string.Empty);
      _fieldsGrid.Rows[rowIndex].Tag = field;
    }

    _fieldsGrid.ClearSelection();
  }

  public void ClearFields()
  {
    _fieldsGrid.Rows.Clear();
    _heading.Text = "Fields";
  }

  private void AddColumn(string headerText, float fillWeight, int minimumWidth)
  {
    _fieldsGrid.Columns.Add(new DataGridViewTextBoxColumn
    {
      HeaderText = headerText,
      FillWeight = fillWeight,
      MinimumWidth = minimumWidth,
      SortMode = DataGridViewColumnSortMode.Automatic
    });
  }

  private static string FormatModifiers(FieldStructure field)
  {
    if (field.IsConst)
    {
      return "Const";
    }

    var modifiers = new List<string>();
    if (field.IsShared)
    {
      modifiers.Add("Shared");
    }

    if (field.IsReadOnly)
    {
      modifiers.Add("ReadOnly");
    }

    return string.Join(" ", modifiers);
  }
}
