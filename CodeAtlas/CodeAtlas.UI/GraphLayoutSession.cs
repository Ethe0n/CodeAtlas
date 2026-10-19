using Microsoft.Msagl.GraphViewerGdi;

namespace CodeAtlas.UI;

internal sealed class GraphLayoutSession
{
  private readonly UserControl _owner;
  private readonly GViewer _viewer;
  private readonly System.Windows.Forms.Label _message;
  private readonly string _emptyText;
  private readonly Action<IReadOnlyDictionary<string, string>>? _setSymbols;
  private readonly Button _cancel = new() { Text = "Cancel layout", Dock = DockStyle.Bottom, Height = 28, Visible = false };
  private CancellationTokenSource? _current;
  public Task Completion { get; private set; } = Task.CompletedTask;

  public GraphLayoutSession(UserControl owner, GViewer viewer, System.Windows.Forms.Label message,
      Action<IReadOnlyDictionary<string, string>>? setSymbols = null)
  {
    _owner = owner;
    _viewer = viewer;
    _message = message;
    _emptyText = message.Text;
    _setSymbols = setSymbols;
    owner.Controls.Add(_cancel);
    _cancel.Click += (_, _) => Cancel("Graph layout canceled.");
    owner.Disposed += (_, _) => Cancel();
  }

  public void Cancel(string? message = null)
  {
    var current = _current;
    _current = null;
    current?.Cancel();
    if (!_owner.IsDisposed)
    {
      _cancel.Visible = false;
      _message.Text = message ?? _emptyText;
    }
  }

  public void Show(GraphLayoutRequest request)
  {
    Cancel();
    var cancellation = new CancellationTokenSource();
    _current = cancellation;
    _viewer.Graph = null;
    _viewer.Visible = false;
    _message.Text = "Calculating graph...";
    _message.Visible = true;
    _cancel.Visible = true;
    _cancel.BringToFront();
    Completion = LoadAsync(request, cancellation);
  }

  private async Task LoadAsync(GraphLayoutRequest request, CancellationTokenSource cancellation)
  {
    try
    {
      var result = await Task.Run(() => GraphLayoutWorker.CalculateAsync(request, cancellation.Token));
      if (!ReferenceEquals(_current, cancellation) || _owner.IsDisposed) return;
      _setSymbols?.Invoke(result.Symbols);
      if (result.Graph is null)
      {
        _message.Text = _emptyText;
        return;
      }

      _viewer.NeedToCalculateLayout = false;
      try { _viewer.Graph = result.Graph; }
      finally { _viewer.NeedToCalculateLayout = true; }
      _viewer.Visible = true;
      _message.Visible = false;
      _owner.BeginInvoke(new Action(() =>
      {
        if (!_owner.IsDisposed && ReferenceEquals(_viewer.Graph, result.Graph))
          _viewer.FitGraphBoundingBox();
      }));
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    catch (Exception exception)
    {
      if (ReferenceEquals(_current, cancellation) && !_owner.IsDisposed)
        _message.Text = exception.Message;
    }
    finally
    {
      if (ReferenceEquals(_current, cancellation) && !_owner.IsDisposed) _cancel.Visible = false;
      cancellation.Dispose();
      if (ReferenceEquals(_current, cancellation)) _current = null;
    }
  }
}
