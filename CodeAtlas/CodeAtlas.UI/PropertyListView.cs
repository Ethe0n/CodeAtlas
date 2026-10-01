using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class PropertyListView : UserControl
{
  private readonly System.Windows.Forms.Label _heading = new()
  {
    AutoSize = true,
    Dock = DockStyle.Top,
    Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold),
    Margin = new Padding(0),
    Padding = new Padding(0, 0, 0, 12),
    Text = "Properties"
  };

  private readonly DataGridView _propertiesGrid = new()
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

  public PropertyListView()
  {
    Dock = DockStyle.Fill;
    BackColor = SystemColors.Window;
    Padding = new Padding(20);

    _propertiesGrid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
    _propertiesGrid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
    _propertiesGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
    _propertiesGrid.DefaultCellStyle.BackColor = SystemColors.Window;
    _propertiesGrid.DefaultCellStyle.ForeColor = SystemColors.ControlText;
    _propertiesGrid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
    _propertiesGrid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
    _propertiesGrid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
    _propertiesGrid.GridColor = SystemColors.ControlLight;

    AddColumn("Name", 135, 130);
    AddColumn("Type", 165, 150);
    AddColumn("Accessibility", 100, 100);
    AddColumn("Modifiers", 125, 120);

    _propertiesGrid.CellDoubleClick += (_, args) =>
    {
      if (args.RowIndex >= 0 &&
          _propertiesGrid.Rows[args.RowIndex].Tag is PropertyStructure property)
      {
        PropertyActivated?.Invoke(property);
      }
    };

    Controls.Add(_propertiesGrid);
    Controls.Add(_heading);
  }

  public event Action<PropertyStructure>? PropertyActivated;

  public void ShowProperties(IReadOnlyList<PropertyStructure> properties)
  {
    _propertiesGrid.Rows.Clear();
    _heading.Text = $"Properties ({properties.Count})";

    foreach (var property in properties)
    {
      var rowIndex = _propertiesGrid.Rows.Add(
          property.Name,
          property.Type,
          property.Accessibility,
          FormatModifiers(property));
      _propertiesGrid.Rows[rowIndex].Tag = property;
    }

    _propertiesGrid.ClearSelection();
  }

  public void ClearProperties()
  {
    _propertiesGrid.Rows.Clear();
    _heading.Text = "Properties";
  }

  private void AddColumn(string headerText, float fillWeight, int minimumWidth)
  {
    _propertiesGrid.Columns.Add(new DataGridViewTextBoxColumn
    {
      HeaderText = headerText,
      FillWeight = fillWeight,
      MinimumWidth = minimumWidth,
      SortMode = DataGridViewColumnSortMode.Automatic
    });
  }

  private static string FormatModifiers(PropertyStructure property)
  {
    var modifiers = new List<string>();
    if (property.IsShared)
    {
      modifiers.Add("Shared");
    }

    if (property.IsReadOnly)
    {
      modifiers.Add("ReadOnly");
    }
    else if (property.IsWriteOnly)
    {
      modifiers.Add("WriteOnly");
    }
    else
    {
      modifiers.Add("Read/Write");
    }

    return string.Join(" ", modifiers);
  }
}
