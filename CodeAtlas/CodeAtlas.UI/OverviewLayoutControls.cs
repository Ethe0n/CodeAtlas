namespace CodeAtlas.UI;

internal sealed class OverviewScrollPanel : Panel
{
  public OverviewScrollPanel()
  {
    DoubleBuffered = true;
  }
}

internal sealed class OverviewContentPanel : TableLayoutPanel
{
  public OverviewContentPanel()
  {
    DoubleBuffered = true;
  }

  public void ClearContent()
  {
    SuspendLayout();
    try
    {
      while (Controls.Count > 0)
      {
        var control = Controls[0];
        Controls.RemoveAt(0);
        control.Dispose();
      }

      RowStyles.Clear();
      RowCount = 0;
    }
    finally
    {
      ResumeLayout(false);
    }
  }
}
