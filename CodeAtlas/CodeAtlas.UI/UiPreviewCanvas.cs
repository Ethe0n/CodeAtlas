using System.Drawing.Drawing2D;
using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

internal sealed class UiPreviewCanvas : Control
{
  private static readonly Color AccentColor = Color.FromArgb(41, 98, 148);
  private static readonly Color FormBorderColor = Color.FromArgb(134, 142, 150);
  private static readonly Color ControlBorderColor = Color.FromArgb(150, 157, 164);
  private static readonly Color ControlFillColor = Color.FromArgb(248, 249, 250);
  private static readonly Color TitleBarColor = Color.FromArgb(235, 238, 242);

  private readonly List<(string Name, RectangleF Bounds)> _hitRegions = [];
  private TypeStructure? _type;
  private string? _selectedControlName;

  public UiPreviewCanvas()
  {
    BackColor = Color.FromArgb(242, 244, 247);
    DoubleBuffered = true;
    MinimumSize = new Size(260, 180);
    SetStyle(
        ControlStyles.AllPaintingInWmPaint |
        ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.ResizeRedraw |
        ControlStyles.UserPaint,
        true);
  }

  public event Action<string>? ControlSelected;

  public void ShowType(TypeStructure type)
  {
    _type = type;
    _selectedControlName = null;
    Invalidate();
  }

  public void ClearPreview()
  {
    _type = null;
    _selectedControlName = null;
    _hitRegions.Clear();
    Invalidate();
  }

  public void SelectControl(string? controlName)
  {
    if (string.Equals(_selectedControlName, controlName, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    _selectedControlName = controlName;
    Invalidate();
  }

  protected override void OnMouseDown(MouseEventArgs e)
  {
    base.OnMouseDown(e);

    for (var index = _hitRegions.Count - 1; index >= 0; index--)
    {
      var region = _hitRegions[index];
      if (!region.Bounds.Contains(e.Location))
      {
        continue;
      }

      SelectControl(region.Name);
      ControlSelected?.Invoke(region.Name);
      return;
    }
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    base.OnPaint(e);
    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    e.Graphics.Clear(BackColor);
    _hitRegions.Clear();

    if (_type is null || _type.UiControls.Count == 0)
    {
      DrawCenteredMessage(e.Graphics, "No UI preview available");
      return;
    }

    var absoluteBounds = BuildAbsoluteBounds(_type.UiControls);
    var placedControls = _type.UiControls
        .Where(control => absoluteBounds.ContainsKey(control.Name))
        .OrderBy(control => control.TabIndex ?? int.MaxValue)
        .ThenBy(control => control.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var clientWidth = _type.UiPreview?.ClientWidth
        ?? placedControls.Select(control => absoluteBounds[control.Name].Right).DefaultIfEmpty(480).Max() + 24;
    var clientHeight = _type.UiPreview?.ClientHeight
        ?? placedControls.Select(control => absoluteBounds[control.Name].Bottom).DefaultIfEmpty(320).Max() + 24;
    clientWidth = Math.Max(clientWidth, 160);
    clientHeight = Math.Max(clientHeight, 100);

    const float outerMargin = 22;
    const float titleBarHeight = 26;
    var availableWidth = Math.Max(1, ClientSize.Width - (outerMargin * 2));
    var availableHeight = Math.Max(1, ClientSize.Height - (outerMargin * 2) - titleBarHeight);
    var scale = Math.Min(availableWidth / clientWidth, availableHeight / clientHeight);
    scale = Math.Max(0.1f, Math.Min(scale, 1.5f));

    var renderedWidth = clientWidth * scale;
    var renderedHeight = clientHeight * scale;
    var formLeft = (ClientSize.Width - renderedWidth) / 2;
    var formTop = Math.Max(outerMargin + titleBarHeight, (ClientSize.Height - renderedHeight + titleBarHeight) / 2);
    var titleBounds = new RectangleF(formLeft, formTop - titleBarHeight, renderedWidth, titleBarHeight);
    var clientBounds = new RectangleF(formLeft, formTop, renderedWidth, renderedHeight);

    using var shadowBrush = new SolidBrush(Color.FromArgb(30, Color.Black));
    using var formBrush = new SolidBrush(Color.White);
    using var titleBrush = new SolidBrush(TitleBarColor);
    using var formBorderPen = new Pen(FormBorderColor, 1);
    e.Graphics.FillRectangle(shadowBrush, formLeft + 4, formTop - titleBarHeight + 4, renderedWidth, renderedHeight + titleBarHeight);
    e.Graphics.FillRectangle(titleBrush, titleBounds);
    e.Graphics.FillRectangle(formBrush, clientBounds);
    e.Graphics.DrawRectangle(formBorderPen, titleBounds.X, titleBounds.Y, titleBounds.Width, titleBounds.Height + clientBounds.Height);

    var formTitle = _type.UiPreview?.Title ?? _type.Name;
    TextRenderer.DrawText(
        e.Graphics,
        formTitle,
        Font,
        Rectangle.Round(titleBounds),
        SystemColors.ControlText,
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    foreach (var control in placedControls)
    {
      var sourceBounds = absoluteBounds[control.Name];
      var renderedBounds = new RectangleF(
          formLeft + (sourceBounds.X * scale),
          formTop + (sourceBounds.Y * scale),
          Math.Max(3, sourceBounds.Width * scale),
          Math.Max(3, sourceBounds.Height * scale));
      DrawControl(e.Graphics, control, renderedBounds);
      _hitRegions.Add((control.Name, renderedBounds));
    }

    var unplacedCount = _type.UiControls.Count - placedControls.Length;
    if (unplacedCount > 0)
    {
      using var textBrush = new SolidBrush(SystemColors.GrayText);
      e.Graphics.DrawString(
          $"{unplacedCount} control(s) without layout information",
          Font,
          textBrush,
          new PointF(outerMargin, ClientSize.Height - Font.Height - 5));
    }
  }

  private void DrawControl(Graphics graphics, UiControlInfo control, RectangleF bounds)
  {
    var shortType = ShortTypeName(control.Type);
    var text = string.IsNullOrWhiteSpace(control.DisplayText) ? control.Name : control.DisplayText;
    var selected = string.Equals(control.Name, _selectedControlName, StringComparison.OrdinalIgnoreCase);
    var borderColor = selected ? AccentColor : ControlBorderColor;
    var borderWidth = selected ? 2f : 1f;

    using var borderPen = new Pen(borderColor, borderWidth);
    using var fillBrush = new SolidBrush(ControlFillColor);
    using var subtleBrush = new SolidBrush(Color.FromArgb(238, 242, 246));

    if (shortType.EndsWith("Label", StringComparison.OrdinalIgnoreCase))
    {
      DrawText(graphics, text, bounds, ContentAlignment.MiddleLeft);
    }
    else if (shortType.EndsWith("Button", StringComparison.OrdinalIgnoreCase))
    {
      graphics.FillRectangle(subtleBrush, bounds);
      graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
      DrawText(graphics, text, bounds, ContentAlignment.MiddleCenter);
    }
    else if (shortType.EndsWith("TextBox", StringComparison.OrdinalIgnoreCase) ||
             shortType.EndsWith("ComboBox", StringComparison.OrdinalIgnoreCase))
    {
      graphics.FillRectangle(Brushes.White, bounds);
      graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
      DrawText(graphics, text, RectangleF.Inflate(bounds, -4, 0), ContentAlignment.MiddleLeft);
    }
    else
    {
      graphics.FillRectangle(fillBrush, bounds);
      graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
      DrawText(graphics, text, bounds, ContentAlignment.MiddleCenter);
    }

    if (selected && shortType.EndsWith("Label", StringComparison.OrdinalIgnoreCase))
    {
      graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
  }

  private void DrawText(Graphics graphics, string text, RectangleF bounds, ContentAlignment alignment)
  {
    if (bounds.Width < 8 || bounds.Height < 8)
    {
      return;
    }

    var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
    flags |= alignment == ContentAlignment.MiddleCenter
        ? TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
        : TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
    TextRenderer.DrawText(
        graphics,
        text,
        Font,
        Rectangle.Round(bounds),
        SystemColors.ControlText,
        flags);
  }

  private static Dictionary<string, Rectangle> BuildAbsoluteBounds(IReadOnlyList<UiControlInfo> controls)
  {
    var controlsByName = controls.ToDictionary(control => control.Name, StringComparer.OrdinalIgnoreCase);
    var result = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);

    Rectangle? Resolve(UiControlInfo control, HashSet<string> path)
    {
      if (result.TryGetValue(control.Name, out var existing))
      {
        return existing;
      }

      if (control.Bounds is null || !path.Add(control.Name))
      {
        return null;
      }

      var x = control.Bounds.X;
      var y = control.Bounds.Y;
      if (control.ParentName is not null &&
          controlsByName.TryGetValue(control.ParentName, out var parent) &&
          Resolve(parent, path) is { } parentBounds)
      {
        x += parentBounds.X;
        y += parentBounds.Y;
      }

      path.Remove(control.Name);
      var bounds = new Rectangle(x, y, control.Bounds.Width, control.Bounds.Height);
      result[control.Name] = bounds;
      return bounds;
    }

    foreach (var control in controls)
    {
      Resolve(control, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    return result;
  }

  private void DrawCenteredMessage(Graphics graphics, string text)
  {
    TextRenderer.DrawText(
        graphics,
        text,
        Font,
        ClientRectangle,
        SystemColors.GrayText,
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
  }

  private static string ShortTypeName(string typeName)
  {
    var index = typeName.LastIndexOf('.');
    return index >= 0 && index < typeName.Length - 1
        ? typeName[(index + 1)..]
        : typeName;
  }
}
