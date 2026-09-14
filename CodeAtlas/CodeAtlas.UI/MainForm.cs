using CodeAtlas.Roslyn;
using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

public sealed class MainForm : Form
{
  private readonly TreeView _projectExplorer = new();
  private readonly TabControl _detailsTabs = new();
  private readonly TextBox _overviewText = CreateReadOnlyTextBox();
  private readonly CallGraphView _callGraphView = new();
  private readonly ControlFlowGraphView _controlFlowGraphView = new();
  private readonly ToolStripStatusLabel _statusLabel = new("Ready");
  private readonly SplitContainer _mainSplitContainer = new();

  private SolutionStructure? _solution;
  private bool _initialSplitterDistanceApplied;

  public MainForm()
  {
    Text = "CodeAtlas";
    Width = 1200;
    Height = 800;
    MinimumSize = new Size(900, 600);

    BuildLayout();
    _callGraphView.MethodSelected += SelectMethodNodeBySymbolId;
  }

  protected override void OnShown(EventArgs e)
  {
    base.OnShown(e);
    PerformLayout();
    BeginInvoke(new Action(ApplyInitialSplitterDistance));
  }

  private void BuildLayout()
  {
    var menuStrip = new MenuStrip();
    var fileMenu = new ToolStripMenuItem("&File");
    var openSolutionMenuItem = new ToolStripMenuItem("&Open Solution...", null, async (_, _) => await OpenSolutionAsync());
    fileMenu.DropDownItems.Add(openSolutionMenuItem);
    menuStrip.Items.Add(fileMenu);
    MainMenuStrip = menuStrip;

    _mainSplitContainer.Dock = DockStyle.Fill;
    _mainSplitContainer.Orientation = Orientation.Vertical;

    _projectExplorer.Dock = DockStyle.Fill;
    _projectExplorer.HideSelection = false;
    _projectExplorer.AfterSelect += (_, args) => ShowNodeDetails(args.Node);
    _mainSplitContainer.Panel1.Controls.Add(_projectExplorer);

    _detailsTabs.Dock = DockStyle.Fill;
    _detailsTabs.TabPages.Add(CreateTabPage("Overview", _overviewText));
    _detailsTabs.TabPages.Add(CreateTabPage("Call Graph", _callGraphView));
    _detailsTabs.TabPages.Add(CreateTabPage("Control Flow", _controlFlowGraphView));
    _mainSplitContainer.Panel2.Controls.Add(_detailsTabs);

    var statusStrip = new StatusStrip();
    statusStrip.Items.Add(_statusLabel);

    Controls.Add(_mainSplitContainer);
    Controls.Add(statusStrip);
    Controls.Add(menuStrip);

    menuStrip.Dock = DockStyle.Top;
    statusStrip.Dock = DockStyle.Bottom;
  }

  private void ApplyInitialSplitterDistance()
  {
    if (_initialSplitterDistanceApplied)
    {
      return;
    }

    var width = _mainSplitContainer.ClientSize.Width;
    if (width <= 0)
    {
      return;
    }

    const int panel1MinSize = 180;
    const int panel2MinSize = 300;

    var availableWidth = width - _mainSplitContainer.SplitterWidth;
    if (availableWidth <= 0)
    {
      return;
    }

    if (availableWidth < panel1MinSize + panel2MinSize)
    {
      return;
    }

    _mainSplitContainer.Panel1MinSize = panel1MinSize;
    _mainSplitContainer.Panel2MinSize = panel2MinSize;

    var minDistance = panel1MinSize;
    var maxDistance = availableWidth - panel2MinSize;
    if (maxDistance < minDistance)
    {
      return;
    }

    var desiredDistance = (int)Math.Round(availableWidth * 0.33);
    _mainSplitContainer.SplitterDistance = Math.Clamp(
        desiredDistance,
        minDistance,
        maxDistance);
    _initialSplitterDistanceApplied = true;
  }

  private async Task OpenSolutionAsync()
  {
    using var dialog = new OpenFileDialog
    {
      Filter = "Visual Studio Solution (*.sln)|*.sln|All Files (*.*)|*.*",
      Title = "Open Solution"
    };

    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    UseWaitCursor = true;
    _projectExplorer.Enabled = false;
    _statusLabel.Text = "Analyzing solution...";
    ClearDetails();

    try
    {
      var analyzer = new VbSolutionAnalyzer();
      _solution = await analyzer.AnalyzeAsync(dialog.FileName);
      PopulateProjectExplorer(_solution);
      _statusLabel.Text = $"Loaded {_solution.Projects.Count} project(s)";
    }
    catch (Exception exception)
    {
      _statusLabel.Text = "Failed to analyze solution";
      MessageBox.Show(this, exception.Message, "CodeAtlas", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      _projectExplorer.Enabled = true;
      UseWaitCursor = false;
    }
  }

  private void PopulateProjectExplorer(SolutionStructure solution)
  {
    _projectExplorer.BeginUpdate();
    _projectExplorer.Nodes.Clear();

    foreach (var project in solution.Projects)
    {
      var projectNode = new TreeNode(project.Name) { Tag = project };
      _projectExplorer.Nodes.Add(projectNode);
      AddTypeNodes(projectNode, project, project.Types);
      projectNode.Expand();
    }

    _projectExplorer.EndUpdate();
  }

  private static void AddTypeNodes(TreeNode parentNode, ProjectStructure project, IReadOnlyList<TypeStructure> types)
  {
    var nestedTypesByParent = types
        .Where(type => type.ContainingTypeSymbolId is not null)
        .GroupBy(type => type.ContainingTypeSymbolId!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    foreach (var type in types.Where(type => type.ContainingTypeSymbolId is null))
    {
      AddTypeNode(parentNode, project, type, nestedTypesByParent);
    }
  }

  private static void AddTypeNode(
      TreeNode parentNode,
      ProjectStructure project,
      TypeStructure type,
      IReadOnlyDictionary<string, TypeStructure[]> nestedTypesByParent)
  {
    var typeNode = new TreeNode(type.Name) { Tag = new TypeNodeContext(project, type) };
    parentNode.Nodes.Add(typeNode);

    AddFieldNodes(typeNode, project, type);
    AddPropertyNodes(typeNode, type);
    AddUiControlNodes(typeNode, type);
    AddUiEventHandlerNodes(typeNode, type);
    AddMethodNodes(typeNode, project, type);

    if (!nestedTypesByParent.TryGetValue(type.SymbolId, out var nestedTypes))
    {
      return;
    }

    var nestedGroupNode = new TreeNode($"Nested Types ({nestedTypes.Length})") { Tag = nestedTypes };
    typeNode.Nodes.Add(nestedGroupNode);

    foreach (var nestedType in nestedTypes.OrderBy(nestedType => nestedType.FullName, StringComparer.Ordinal))
    {
      AddTypeNode(nestedGroupNode, project, nestedType, nestedTypesByParent);
    }
  }

  private static void AddFieldNodes(TreeNode typeNode, ProjectStructure project, TypeStructure type)
  {
    var groupNode = new TreeNode($"Fields ({type.Fields.Count})") { Tag = type.Fields };
    typeNode.Nodes.Add(groupNode);

    foreach (var field in type.Fields)
    {
      groupNode.Nodes.Add(new TreeNode($"{field.Name} : {field.Type}") { Tag = new FieldNodeContext(project, type, field) });
    }
  }

  private static void AddPropertyNodes(TreeNode typeNode, TypeStructure type)
  {
    var groupNode = new TreeNode($"Properties ({type.Properties.Count})") { Tag = type.Properties };
    typeNode.Nodes.Add(groupNode);

    foreach (var property in type.Properties)
    {
      groupNode.Nodes.Add(new TreeNode($"{property.Name} : {property.Type}") { Tag = property });
    }
  }

  private static void AddUiControlNodes(TreeNode typeNode, TypeStructure type)
  {
    var groupNode = new TreeNode($"UI Controls ({type.UiControls.Count})") { Tag = type.UiControls };
    typeNode.Nodes.Add(groupNode);

    foreach (var control in type.UiControls)
    {
      groupNode.Nodes.Add(new TreeNode($"{control.Name} : {ShortTypeName(control.Type)}") { Tag = control });
    }
  }

  private static void AddUiEventHandlerNodes(TreeNode typeNode, TypeStructure type)
  {
    var groupNode = new TreeNode($"UI Event Handlers ({type.UiEventHandlers.Count})") { Tag = type.UiEventHandlers };
    typeNode.Nodes.Add(groupNode);

    foreach (var handler in type.UiEventHandlers)
    {
      groupNode.Nodes.Add(new TreeNode($"{handler.ControlName}.{handler.EventName} -> {handler.HandlerMethodName}") { Tag = handler });
    }
  }

  private static void AddMethodNodes(TreeNode typeNode, ProjectStructure project, TypeStructure type)
  {
    var methods = type.Methods.Where(method => !method.IsGenerated).ToArray();
    var groupNode = new TreeNode($"Methods ({methods.Length})") { Tag = type.Methods };
    typeNode.Nodes.Add(groupNode);

    foreach (var method in methods)
    {
      groupNode.Nodes.Add(new TreeNode(method.Name) { Tag = new MethodNodeContext(project, type, method) });
    }
  }

  private void ShowNodeDetails(TreeNode? node)
  {
    ClearDetails();

    switch (node?.Tag)
    {
      case TypeNodeContext typeContext:
        ShowClassOverview(typeContext);
        break;
      case FieldNodeContext fieldContext:
        ShowFieldOverview(fieldContext);
        break;
      case MethodNodeContext methodContext:
        ShowMethodOverview(methodContext);
        ShowMethodCalls(methodContext);
        ShowControlFlow(methodContext);
        break;
    }
  }

  private void ShowClassOverview(TypeNodeContext context)
  {
    var type = context.Type;
    var methods = type.Methods
        .Where(method => !method.IsGenerated)
        .ToArray();
    var fields = type.Fields
        .Where(field => !field.IsGenerated)
        .ToArray();
    var properties = type.Properties
        .Where(property => !property.IsGenerated)
        .ToArray();
    var lines = new List<string>
        {
            type.Name,
            new string('-', Math.Max(24, type.Name.Length)),
            string.Empty,
            "Type",
            $"Name            {type.FullName}",
            $"Kind            {FormatOptional(type.Kind)}",
            $"Accessibility   {FormatOptional(type.Accessibility)}",
            $"Partial         {FormatBoolean(type.FilePaths.Count > 1)}",
            $"Base Type       {FormatOptional(type.BaseType)}",
            string.Empty,
            "Source"
        };

    if (type.FilePaths.Count == 0)
    {
      lines.Add("(unknown)");
    }
    else
    {
      lines.AddRange(type.FilePaths.Select(path => Path.GetFileName(path)));
    }

    lines.AddRange(
    [
        string.Empty,
            "Structure",
            $"Fields            {fields.Length}",
            $"Properties        {properties.Length}",
            $"Methods           {methods.Length}",
            $"UI Controls       {type.UiControls.Count}",
            $"UI Event Handlers {type.UiEventHandlers.Count}",
            string.Empty,
            "Methods"
    ]);

    AppendIndentedList(lines, methods.Select(FormatMethodSignature));

    lines.AddRange(
    [
        string.Empty,
            "Fields"
    ]);
    AppendIndentedList(lines, fields.Select(field => $"{field.Name} : {field.Type}"));

    lines.AddRange(
    [
        string.Empty,
            "UI Controls"
    ]);
    AppendIndentedList(lines, type.UiControls.Select(control => $"{control.Name} : {ShortTypeName(control.Type)}"));

    lines.AddRange(
    [
        string.Empty,
            "UI Event Handlers"
    ]);
    AppendIndentedList(lines, type.UiEventHandlers.Select(handler =>
        $"{handler.ControlName}.{handler.EventName} -> {handler.HandlerMethodName}"));

    _overviewText.Text = string.Join(Environment.NewLine, lines);
  }

  private void ShowFieldOverview(FieldNodeContext context)
  {
    var field = context.Field;
    var usages = context.Project.FieldUsages
        .Where(usage => string.Equals(usage.FieldSymbolId, field.SymbolId, StringComparison.Ordinal))
        .ToArray();
    var readUsages = usages
        .Where(usage => usage.UsageKind is FieldUsageKind.Read or FieldUsageKind.ReadWrite)
        .ToArray();
    var writeUsages = usages
        .Where(usage => usage.UsageKind is FieldUsageKind.Write or FieldUsageKind.ReadWrite)
        .ToArray();
    var methodsById = context.Project.Types
        .SelectMany(type => type.Methods.Concat(type.GeneratedMethods))
        .GroupBy(method => method.SymbolId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var usedByMethods = usages
        .Select(usage => usage.MethodSymbolId)
        .Distinct(StringComparer.Ordinal)
        .Count();
    var readByMethods = readUsages
        .Select(usage => usage.MethodSymbolId)
        .Distinct(StringComparer.Ordinal)
        .Select(methodId => FormatMethodReference(methodId, methodsById));
    var writtenByMethods = writeUsages
        .Select(usage => usage.MethodSymbolId)
        .Distinct(StringComparer.Ordinal)
        .Select(methodId => FormatMethodReference(methodId, methodsById));

    var lines = new List<string>
        {
            "Field",
            $"Name            {field.Name}",
            $"Type            {field.Type}",
            $"Accessibility   {field.Accessibility}",
            $"Shared          {FormatBoolean(field.IsShared)}",
            $"ReadOnly        {FormatBoolean(field.IsReadOnly)}",
            $"Const           {FormatBoolean(field.IsConst)}",
            string.Empty,
            "Owner",
            $"Declaring Type  {context.Type.FullName}",
            string.Empty,
            "Declaration",
            $"File            {field.FilePath ?? "(unknown)"}",
            $"Line            {field.Span.StartLine}",
            $"Initializer     {FormatOptional(field.Initializer)}",
            string.Empty,
            "Usage",
            $"Reads           {readUsages.Length}",
            $"Writes          {writeUsages.Length}",
            $"Used By Methods {usedByMethods}",
            $"Status          {GetFieldUsageStatus(readUsages.Length, writeUsages.Length)}",
            string.Empty,
            "Read By"
        };

    AppendIndentedList(lines, readByMethods);

    lines.AddRange(
    [
        string.Empty,
        "Written By"
    ]);
    AppendIndentedList(lines, writtenByMethods);

    _overviewText.Text = string.Join(Environment.NewLine, lines);
  }

  private void ShowMethodOverview(MethodNodeContext context)
  {
    var method = context.Method;
    var outgoingCalls = context.Project.Calls
        .Where(call => call.CallerMethodSymbolId == method.SymbolId)
        .ToArray();
    var incomingCalls = context.Project.Calls
        .Where(call =>
            call.IsProjectInternal &&
            call.CalleeMethodSymbolId == method.SymbolId &&
            call.CallerMethodSymbolId != method.SymbolId)
        .ToArray();
    var externalCalls = outgoingCalls
        .Where(call => !call.IsProjectInternal)
        .ToArray();
    var controlFlow = context.Project.ControlFlows
        .FirstOrDefault(flow => flow.MethodId == method.SymbolId);
    var cfgBlocks = controlFlow?.Nodes
        .Count(node => node.Kind is not "Entry" and not "Exit") ?? 0;
    var conditions = controlFlow?.Edges
        .Where(edge => edge.Kind is "ConditionalTrue" or "ConditionalFalse")
        .Select(edge => edge.From)
        .Distinct()
        .Count() ?? 0;
    var isEventHandler = context.Type.UiEventHandlers.Any(handler =>
        string.Equals(handler.HandlerMethodSymbolId, method.SymbolId, StringComparison.Ordinal) ||
        string.Equals(handler.HandlerMethodName, method.Name, StringComparison.Ordinal));

    var lines = new[]
    {
            "Method",
            $"Method Name: {method.Name}",
            $"Full Name / Id: {method.SymbolId}",
            $"Accessibility: {method.Accessibility}",
            $"Declaring File: {method.FilePath ?? "(unknown)"}",
            $"Source Line: {method.Span.StartLine}",
            string.Empty,
            "Calls",
            $"Incoming Calls: {incomingCalls.Length}",
            $"Outgoing Calls: {outgoingCalls.Length}",
            $"External Calls: {externalCalls.Length}",
            string.Empty,
            "Control Flow",
            $"CFG Blocks: {cfgBlocks}",
            $"Conditions: {conditions}",
            string.Empty,
            "Flags",
            $"Generated: {FormatBoolean(method.IsGenerated)}",
            $"Event Handler: {FormatBoolean(isEventHandler)}"
        };

    _overviewText.Text = string.Join(Environment.NewLine, lines);
  }

  private void ShowMethodCalls(MethodNodeContext context)
  {
    var outgoingCalls = context.Project.Calls
        .Where(call => call.CallerMethodSymbolId == context.Method.SymbolId)
        .ToArray();
    var incomingCalls = context.Project.Calls
        .Where(call =>
            call.IsProjectInternal &&
            call.CalleeMethodSymbolId == context.Method.SymbolId &&
            call.CallerMethodSymbolId != context.Method.SymbolId)
        .ToArray();

    _callGraphView.ShowGraph(context.Method, incomingCalls, outgoingCalls);
  }

  private void ShowControlFlow(MethodNodeContext context)
  {
    var controlFlow = context.Project.ControlFlows.FirstOrDefault(flow => flow.MethodId == context.Method.SymbolId);
    _controlFlowGraphView.ShowGraph(controlFlow);
  }

  private void SelectMethodNodeBySymbolId(string methodSymbolId)
  {
    var node = FindMethodNode(_projectExplorer.Nodes, methodSymbolId);
    if (node is null)
    {
      return;
    }

    node.EnsureVisible();
    _projectExplorer.SelectedNode = node;
  }

  private static TreeNode? FindMethodNode(TreeNodeCollection nodes, string methodSymbolId)
  {
    foreach (TreeNode node in nodes)
    {
      if (node.Tag is MethodNodeContext context &&
          string.Equals(context.Method.SymbolId, methodSymbolId, StringComparison.Ordinal))
      {
        return node;
      }

      var match = FindMethodNode(node.Nodes, methodSymbolId);
      if (match is not null)
      {
        return match;
      }
    }

    return null;
  }

  private void ClearDetails()
  {
    _overviewText.Clear();
    _callGraphView.ClearGraph();
    _controlFlowGraphView.ClearGraph();
  }

  private static TabPage CreateTabPage(string title, Control content)
  {
    var page = new TabPage(title);
    content.Dock = DockStyle.Fill;
    page.Controls.Add(content);
    return page;
  }

  private static TextBox CreateReadOnlyTextBox()
  {
    return new TextBox
    {
      BorderStyle = BorderStyle.None,
      Dock = DockStyle.Fill,
      Font = new Font(FontFamily.GenericMonospace, 10),
      Multiline = true,
      ReadOnly = true,
      ScrollBars = ScrollBars.Both,
      WordWrap = false
    };
  }

  private static string ShortTypeName(string typeName)
  {
    var index = typeName.LastIndexOf('.');
    return index >= 0 && index < typeName.Length - 1
        ? typeName[(index + 1)..]
        : typeName;
  }

  private static string FormatMethodSignature(MethodStructure method)
  {
    var signatureStart = method.SymbolId.IndexOf('(', StringComparison.Ordinal);
    return signatureStart >= 0
        ? $"{method.Name}{method.SymbolId[signatureStart..]}"
        : $"{method.Name}()";
  }

  private static string FormatMethodReference(
      string methodSymbolId,
      IReadOnlyDictionary<string, MethodStructure> methodsById)
  {
    return methodsById.TryGetValue(methodSymbolId, out var method)
        ? FormatMethodSignature(method)
        : methodSymbolId;
  }

  private static string GetFieldUsageStatus(int reads, int writes)
  {
    if (reads == 0 && writes == 0)
    {
      return "Unused Candidate";
    }

    if (reads == 0)
    {
      return "Write Only Candidate";
    }

    return "Used";
  }

  private static void AppendIndentedList(List<string> lines, IEnumerable<string> values)
  {
    var added = false;
    foreach (var value in values)
    {
      lines.Add($"  {value}");
      added = true;
    }

    if (!added)
    {
      lines.Add("  (none)");
    }
  }

  private static string FormatBoolean(bool value)
  {
    return value ? "Yes" : "No";
  }

  private static string FormatOptional(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? "(not available)" : value;
  }

  private sealed record TypeNodeContext(ProjectStructure Project, TypeStructure Type);

  private sealed record FieldNodeContext(ProjectStructure Project, TypeStructure Type, FieldStructure Field);

  private sealed record MethodNodeContext(ProjectStructure Project, TypeStructure Type, MethodStructure Method);
}
