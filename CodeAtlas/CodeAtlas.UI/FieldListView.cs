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

  private readonly VirtualModelGrid<FieldStructure> _fieldsGrid = new()
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

    _fieldsGrid.AddColumn("Name", field => field.Name, 115, 115);
    _fieldsGrid.AddColumn("Type", field => field.Type, 145, 140);
    _fieldsGrid.AddColumn("Accessibility", field => field.Accessibility, 90, 95);
    _fieldsGrid.AddColumn("Modifiers", FormatModifiers, 105, 105);
    _fieldsGrid.AddColumn("Initializer", field => field.Initializer ?? string.Empty, 170, 150);

    _fieldsGrid.ItemActivated += field => FieldActivated?.Invoke(field);

    Controls.Add(_fieldsGrid);
    Controls.Add(_heading);
  }

  public event Action<FieldStructure>? FieldActivated;

  public void ShowFields(IReadOnlyList<FieldStructure> fields)
  {
    _heading.Text = $"Fields ({fields.Count})";
    _fieldsGrid.SetItems(fields);
  }

  public void ClearFields()
  {
    _fieldsGrid.SetItems(Array.Empty<FieldStructure>());
    _heading.Text = "Fields";
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
