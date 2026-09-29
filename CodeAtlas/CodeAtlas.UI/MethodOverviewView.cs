using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class MethodOverviewView : UserControl
{
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
    Padding = new Padding(24, 20, 24, 24)
  };

  public MethodOverviewView()
  {
    Dock = DockStyle.Fill;
    BackColor = SystemColors.Window;
    _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _scrollPanel.Controls.Add(_content);
    Controls.Add(_scrollPanel);
  }

  public void ShowMethod(
      MethodStructure method,
      int incomingCalls,
      int outgoingCalls,
      int externalCalls,
      int cfgBlocks,
      int conditions,
      string errorHandlingMode,
      bool isEventHandler)
  {
    _content.SuspendLayout();
    _content.Controls.Clear();
    _content.RowStyles.Clear();
    _content.RowCount = 0;

    AddContent(CreateHeader(method));
    AddContent(CreateSection(
        "Declaration",
        [
            new OverviewRow("Full Name / Id", method.SymbolId, IsSelectable: true, IsMonospace: true, IsMultiline: true),
            new OverviewRow("Accessibility", method.Accessibility),
            new OverviewRow("Declaring File", method.FilePath ?? "(unknown)", IsSelectable: true),
            new OverviewRow("Source Line", method.Span.StartLine.ToString())
        ]));
    AddContent(CreateSection(
        "Calls",
        [
            new OverviewRow("Incoming", incomingCalls.ToString()),
            new OverviewRow("Outgoing", outgoingCalls.ToString()),
            new OverviewRow("External", externalCalls.ToString())
        ]));
    AddContent(CreateSection(
        "Control Flow",
        [
            new OverviewRow("CFG Blocks", cfgBlocks.ToString()),
            new OverviewRow("Conditions", conditions.ToString()),
            new OverviewRow("Error Handling", errorHandlingMode)
        ]));
    AddContent(CreateSection(
        "Flags",
        [
            new OverviewRow("Generated", FormatBoolean(method.IsGenerated)),
            new OverviewRow("Event Handler", FormatBoolean(isEventHandler))
        ]));

    _content.ResumeLayout(performLayout: true);
    _scrollPanel.AutoScrollPosition = Point.Empty;
  }

  public void ClearOverview()
  {
    _content.Controls.Clear();
    _content.RowStyles.Clear();
    _content.RowCount = 0;
  }

  private Control CreateHeader(MethodStructure method)
  {
    var header = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 1,
      Dock = DockStyle.Top,
      Margin = new Padding(0, 0, 0, 4)
    };
    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
      ForeColor = SystemColors.ControlText,
      Margin = new Padding(0),
      Text = method.Name
    });
    header.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      ForeColor = SystemColors.GrayText,
      Margin = new Padding(1, 4, 0, 0),
      Text = $"{method.Accessibility} {method.Kind}"
    });
    return header;
  }

  private Control CreateSection(string title, IReadOnlyList<OverviewRow> rows)
  {
    var section = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      ColumnCount = 2,
      Dock = DockStyle.Top,
      Margin = new Padding(0, 16, 0, 0)
    };
    section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
    section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

    var heading = new System.Windows.Forms.Label
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      Font = new Font(Font, FontStyle.Bold),
      ForeColor = Color.FromArgb(45, 85, 120),
      Margin = new Padding(0, 0, 0, 6),
      Text = title
    };
    section.Controls.Add(heading, 0, 0);
    section.SetColumnSpan(heading, 2);

    for (var index = 0; index < rows.Count; index++)
    {
      AddRow(section, index + 1, rows[index]);
    }

    return section;
  }

  private void AddContent(Control control)
  {
    var row = _content.RowCount++;
    _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    _content.Controls.Add(control, 0, row);
  }

  private void AddRow(TableLayoutPanel section, int rowIndex, OverviewRow row)
  {
    section.RowStyles.Add(new RowStyle(row.IsMultiline ? SizeType.Absolute : SizeType.AutoSize, row.IsMultiline ? 48 : 0));
    section.Controls.Add(new System.Windows.Forms.Label
    {
      AutoSize = true,
      ForeColor = SystemColors.GrayText,
      Margin = new Padding(0, 4, 16, 5),
      Text = row.Label
    }, 0, rowIndex);

    var valueControl = row.IsSelectable
        ? CreateSelectableValue(row)
        : CreateValueLabel(row.Value);
    section.Controls.Add(valueControl, 1, rowIndex);
  }

  private Control CreateSelectableValue(OverviewRow row)
  {
    return new TextBox
    {
      BackColor = SystemColors.Window,
      BorderStyle = BorderStyle.None,
      Dock = DockStyle.Fill,
      Font = row.IsMonospace
          ? new Font(FontFamily.GenericMonospace, Font.Size)
          : Font,
      Margin = new Padding(0, 2, 0, 4),
      Multiline = row.IsMultiline,
      ReadOnly = true,
      ScrollBars = ScrollBars.None,
      TabStop = false,
      Text = row.Value,
      WordWrap = row.IsMultiline
    };
  }

  private static Control CreateValueLabel(string value)
  {
    return new System.Windows.Forms.Label
    {
      AutoSize = true,
      ForeColor = SystemColors.ControlText,
      Margin = new Padding(0, 4, 0, 5),
      Text = value
    };
  }

  private static string FormatBoolean(bool value)
  {
    return value ? "Yes" : "No";
  }

  private sealed record OverviewRow(
      string Label,
      string Value,
      bool IsSelectable = false,
      bool IsMonospace = false,
      bool IsMultiline = false);
}
