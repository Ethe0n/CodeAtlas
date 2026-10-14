namespace CodeAtlas.UI;

internal sealed class VirtualModelGrid<T> : DataGridView where T : class
{
  private readonly List<Func<T, object?>> _values = new();
  private IReadOnlyList<T> _items = Array.Empty<T>();
  private int[]? _sortedIndices;
  private int _sortColumn = -1;
  private bool _sortAscending;

  public VirtualModelGrid()
  {
    DoubleBuffered = true;
    VirtualMode = true;
    ReadOnly = true;
    AllowUserToAddRows = false;
    AllowUserToDeleteRows = false;
    AllowUserToResizeRows = false;
    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
    BackgroundColor = SystemColors.Window;
    BorderStyle = BorderStyle.None;
    CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
    ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
    ColumnHeadersHeight = 34;
    ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
    Dock = DockStyle.Fill;
    EnableHeadersVisualStyles = false;
    MultiSelect = false;
    RowHeadersVisible = false;
    RowTemplate.Height = 30;
    SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
    ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
    DefaultCellStyle.BackColor = SystemColors.Window;
    DefaultCellStyle.ForeColor = SystemColors.ControlText;
    DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
    GridColor = SystemColors.ControlLight;

    CellValueNeeded += (_, args) =>
    {
      if (GetItem(args.RowIndex) is { } item)
      {
        args.Value = _values[args.ColumnIndex](item);
      }
    };
    CellDoubleClick += (_, args) =>
    {
      if (GetItem(args.RowIndex) is { } item)
      {
        ItemActivated?.Invoke(item);
      }
    };
    CellToolTipTextNeeded += (_, args) =>
    {
      if (args.ColumnIndex >= 0 && GetItem(args.RowIndex) is { } item)
      {
        args.ToolTipText = _values[args.ColumnIndex](item)?.ToString() ?? string.Empty;
      }
    };
    ColumnHeaderMouseClick += (_, args) => SortItems(args.ColumnIndex);
  }

  public event Action<T>? ItemActivated;

  public void AddColumn(string title, Func<T, object?> value, float fillWeight, int minimumWidth)
  {
    _values.Add(value);
    Columns.Add(new DataGridViewTextBoxColumn
    {
      HeaderText = title,
      FillWeight = fillWeight,
      MinimumWidth = minimumWidth,
      SortMode = DataGridViewColumnSortMode.Programmatic
    });
  }

  public void SetItems(IReadOnlyList<T> items)
  {
    SuspendLayout();
    try
    {
      RowCount = 0;
      _items = items;
      _sortedIndices = null;
      _sortColumn = -1;
      foreach (DataGridViewColumn column in Columns)
      {
        column.HeaderCell.SortGlyphDirection = SortOrder.None;
      }

      RowCount = items.Count;
      ClearSelection();
    }
    finally
    {
      ResumeLayout();
    }

    Invalidate();
  }

  public T? GetItem(int rowIndex)
  {
    return rowIndex >= 0 && rowIndex < _items.Count
        ? _items[_sortedIndices is null ? rowIndex : _sortedIndices[rowIndex]]
        : null;
  }

  public void SizeToVisibleRows(int maximumRows = 10)
  {
    Dock = DockStyle.Top;
    Height = ColumnHeadersHeight + (RowTemplate.Height * Math.Clamp(_items.Count, 1, maximumRows))
        + SystemInformation.HorizontalScrollBarHeight + 2;
  }

  public int FindItemIndex(Predicate<T> predicate)
  {
    for (var index = 0; index < _items.Count; index++)
    {
      if (GetItem(index) is { } item && predicate(item))
      {
        return index;
      }
    }

    return -1;
  }

  private void SortItems(int columnIndex)
  {
    if (columnIndex < 0 || columnIndex >= _values.Count)
    {
      return;
    }

    var selectedItem = CurrentCell is null ? null : GetItem(CurrentCell.RowIndex);
    _sortAscending = _sortColumn != columnIndex || !_sortAscending;
    _sortColumn = columnIndex;
    var values = _items.Select(_values[columnIndex]).ToArray();
    _sortedIndices = Enumerable.Range(0, _items.Count).ToArray();
    Array.Sort(_sortedIndices, (left, right) =>
    {
      var comparison = values[left] is string leftText && values[right] is string rightText
          ? StringComparer.OrdinalIgnoreCase.Compare(leftText, rightText)
          : Comparer<object>.Default.Compare(values[left], values[right]);
      return comparison == 0 ? left.CompareTo(right) : _sortAscending ? comparison : -comparison;
    });
    foreach (DataGridViewColumn column in Columns)
    {
      column.HeaderCell.SortGlyphDirection = column.Index == columnIndex
          ? _sortAscending ? SortOrder.Ascending : SortOrder.Descending
          : SortOrder.None;
    }

    if (selectedItem is not null)
    {
      var sourceIndex = -1;
      for (var index = 0; index < _items.Count; index++)
      {
        if (ReferenceEquals(_items[index], selectedItem))
        {
          sourceIndex = index;
          break;
        }
      }

      var rowIndex = Array.IndexOf(_sortedIndices, sourceIndex);
      if (rowIndex >= 0)
      {
        CurrentCell = Rows[rowIndex].Cells[columnIndex];
      }
    }

    Invalidate();
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _items = Array.Empty<T>();
      _sortedIndices = null;
      _values.Clear();
    }

    base.Dispose(disposing);
  }
}
