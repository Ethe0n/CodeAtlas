using CodeAtlas.Roslyn;
using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

public sealed class MainForm : Form
{
  private readonly TreeView _projectExplorer = new();
  private readonly TabControl _detailsTabs = new();
  private readonly TabPage _overviewTab = new("Overview");
  private readonly TabPage _relationsTab = new("Relations");
  private readonly TabPage _flowTab = new("Flow");
  private readonly Panel _relationsHost = new() { Dock = DockStyle.Fill };
  private readonly Panel _flowHost = new() { Dock = DockStyle.Fill };
  private readonly Panel _overviewHost = new() { Dock = DockStyle.Fill };
  private readonly System.Windows.Forms.Label _relationsUnavailableLabel = CreateUnavailableLabel("Relations are not available for this selection.");
  private readonly System.Windows.Forms.Label _flowUnavailableLabel = CreateUnavailableLabel("Control flow is available for methods only.");
  private readonly TextBox _overviewText = CreateReadOnlyTextBox();
  private readonly ClassOverviewView _classOverviewView = new();
  private readonly MethodOverviewView _methodOverviewView = new();
  private readonly FieldListView _fieldListView = new();
  private readonly PropertyListView _propertyListView = new();
  private readonly UserInterfaceView _userInterfaceView = new();
  private readonly CallGraphView _callGraphView = new();
  private readonly ControlFlowGraphView _controlFlowGraphView = new();
  private readonly ClassDependencyView _classDependencyView = new();
  private readonly ProjectDependencyView _projectDependencyView = new();
  private readonly ToolStripStatusLabel _statusLabel = new("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
  private readonly ToolStripProgressBar _analysisProgressBar = new()
  {
    AutoSize = false,
    Width = 180,
    Visible = false
  };
  private readonly ToolStripButton _cancelAnalysisButton = new("Cancel") { Visible = false };
  private readonly ToolStripMenuItem _openSolutionMenuItem = new("&Open Solution...");
  private readonly ToolStripMenuItem _exportFlowImageMenuItem = new("Export Flow Image...") { Enabled = false };
  private readonly ToolStripMenuItem _exportFlowDiagramMenuItem = new("Export Flow Diagram...") { Enabled = false };
  private readonly SplitContainer _mainSplitContainer = new();

  private SolutionStructure? _solution;
  private CancellationTokenSource? _analysisCancellation;
  private FieldsNodeContext? _activeFieldsContext;
  private PropertiesNodeContext? _activePropertiesContext;
  private bool _initialSplitterDistanceApplied;

  public MainForm()
  {
    Text = "CodeAtlas";
    Width = 1200;
    Height = 800;
    MinimumSize = new Size(900, 600);

    BuildLayout();
    _openSolutionMenuItem.Click += async (_, _) => await OpenSolutionAsync();
    _exportFlowImageMenuItem.Click += (_, _) => _controlFlowGraphView.ExportImage(this);
    _exportFlowDiagramMenuItem.Click += (_, _) => _controlFlowGraphView.ExportDrawIo(this);
    _cancelAnalysisButton.Click += (_, _) => CancelAnalysis();
    _callGraphView.MethodSelected += SelectMethodNodeBySymbolId;
    _classDependencyView.TypeSelected += SelectTypeNodeBySymbolId;
    _projectDependencyView.TypeSelected += SelectTypeNodeBySymbolId;
    _classOverviewView.MethodSelected += SelectMethodNodeBySymbolId;
    _classOverviewView.FieldSelected += ShowFieldBySymbolId;
    _fieldListView.FieldActivated += ShowFieldFromList;
    _propertyListView.PropertyActivated += ShowPropertyFromList;
    _userInterfaceView.MethodSelected += SelectMethodNodeBySymbolId;
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
    fileMenu.DropDownItems.Add(_openSolutionMenuItem);
    fileMenu.DropDownItems.Add(new ToolStripSeparator());
    fileMenu.DropDownItems.Add(_exportFlowImageMenuItem);
    fileMenu.DropDownItems.Add(_exportFlowDiagramMenuItem);
    menuStrip.Items.Add(fileMenu);
    MainMenuStrip = menuStrip;

    _mainSplitContainer.Dock = DockStyle.Fill;
    _mainSplitContainer.Orientation = Orientation.Vertical;

    _projectExplorer.Dock = DockStyle.Fill;
    _projectExplorer.HideSelection = false;
    _projectExplorer.ShowNodeToolTips = true;
    _projectExplorer.AfterSelect += (_, args) => ShowNodeDetails(args.Node);
    _projectExplorer.NodeMouseClick += (_, args) =>
    {
      if (!ReferenceEquals(_projectExplorer.SelectedNode, args.Node))
      {
        return;
      }

      if (args.Node.Tag is FieldsNodeContext &&
          !_overviewHost.Controls.Contains(_fieldListView))
      {
        ShowNodeDetails(args.Node);
      }
      else if (args.Node.Tag is PropertiesNodeContext &&
               !_overviewHost.Controls.Contains(_propertyListView))
      {
        ShowNodeDetails(args.Node);
      }
    };
    _mainSplitContainer.Panel1.Controls.Add(_projectExplorer);

    _detailsTabs.Dock = DockStyle.Fill;
    _overviewText.Dock = DockStyle.Fill;
    _overviewHost.Controls.Add(_overviewText);
    _overviewTab.Controls.Add(_overviewHost);
    _relationsTab.Controls.Add(_relationsHost);
    _flowTab.Controls.Add(_flowHost);
    _detailsTabs.TabPages.Add(_overviewTab);
    _detailsTabs.TabPages.Add(_relationsTab);
    _detailsTabs.TabPages.Add(_flowTab);
    _mainSplitContainer.Panel2.Controls.Add(_detailsTabs);

    var statusStrip = new StatusStrip();
    statusStrip.Items.Add(_statusLabel);
    statusStrip.Items.Add(_analysisProgressBar);
    statusStrip.Items.Add(_cancelAnalysisButton);

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

    using var cancellation = new CancellationTokenSource();
    _analysisCancellation = cancellation;
    BeginAnalysisUi();
    ClearDetails();

    try
    {
      var analyzer = new VbSolutionAnalyzer();
      var progress = new Progress<SolutionAnalysisProgress>(UpdateAnalysisProgress);
      _solution = await analyzer.AnalyzeAsync(dialog.FileName, cancellation.Token, progress);
      PopulateProjectExplorer(_solution);
      var partialProjects = _solution.Projects.Count(project => project.AnalysisStatus == ProjectAnalysisStatus.Partial);
      var failedProjects = _solution.Projects.Count(project => project.AnalysisStatus == ProjectAnalysisStatus.Failed);
      _statusLabel.Text = partialProjects == 0 && failedProjects == 0
          ? $"Loaded {_solution.Projects.Count} project(s)"
          : $"Loaded {_solution.Projects.Count} project(s): {partialProjects} partial, {failedProjects} failed";
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
      _statusLabel.Text = "Analysis canceled";
    }
    catch (Exception exception)
    {
      _statusLabel.Text = "Failed to analyze solution";
      MessageBox.Show(this, exception.Message, "CodeAtlas", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      if (ReferenceEquals(_analysisCancellation, cancellation))
      {
        _analysisCancellation = null;
      }

      EndAnalysisUi();
    }
  }

  private void BeginAnalysisUi()
  {
    UseWaitCursor = true;
    _projectExplorer.Enabled = false;
    _openSolutionMenuItem.Enabled = false;
    _statusLabel.Text = "Reading solution...";
    _analysisProgressBar.Style = ProgressBarStyle.Marquee;
    _analysisProgressBar.MarqueeAnimationSpeed = 30;
    _analysisProgressBar.Visible = true;
    _cancelAnalysisButton.Enabled = true;
    _cancelAnalysisButton.Visible = true;
  }

  private void EndAnalysisUi()
  {
    _projectExplorer.Enabled = true;
    _openSolutionMenuItem.Enabled = true;
    _analysisProgressBar.Visible = false;
    _cancelAnalysisButton.Visible = false;
    UseWaitCursor = false;
  }

  private void CancelAnalysis()
  {
    if (_analysisCancellation is null || _analysisCancellation.IsCancellationRequested)
    {
      return;
    }

    _cancelAnalysisButton.Enabled = false;
    _statusLabel.Text = "Canceling analysis...";
    _analysisCancellation.Cancel();
  }

  private void UpdateAnalysisProgress(SolutionAnalysisProgress progress)
  {
    if (_analysisCancellation is null || _analysisCancellation.IsCancellationRequested)
    {
      return;
    }

    if (progress.Stage == SolutionAnalysisStage.DiscoveringProjects)
    {
      _analysisProgressBar.Style = ProgressBarStyle.Marquee;
      _statusLabel.Text = "Reading solution...";
      return;
    }

    _analysisProgressBar.Style = ProgressBarStyle.Blocks;
    _analysisProgressBar.MarqueeAnimationSpeed = 0;
    _analysisProgressBar.Minimum = 0;
    _analysisProgressBar.Maximum = Math.Max(1, progress.TotalProjects);
    _analysisProgressBar.Value = Math.Clamp(
        progress.CompletedProjects,
        _analysisProgressBar.Minimum,
        _analysisProgressBar.Maximum);

    var projectPosition = Math.Min(progress.CompletedProjects + 1, progress.TotalProjects);
    var projectPrefix = $"Project {projectPosition}/{progress.TotalProjects}: {progress.CurrentProjectName}";
    _statusLabel.Text = progress.Stage switch
    {
      SolutionAnalysisStage.LoadingProject => $"{projectPrefix} - loading with MSBuild",
      SolutionAnalysisStage.CompilingProject => $"{projectPrefix} - creating compilation",
      SolutionAnalysisStage.LoadingFallback => $"{projectPrefix} - loading with fallback",
      SolutionAnalysisStage.AnalyzingDocuments => FormatDocumentProgress(projectPrefix, progress),
      SolutionAnalysisStage.ProjectCompleted =>
          $"Analyzed {progress.CompletedProjects}/{progress.TotalProjects} project(s) ({progress.ProjectStatus})",
      _ => "Analyzing solution..."
    };
  }

  private static string FormatDocumentProgress(
      string projectPrefix,
      SolutionAnalysisProgress progress)
  {
    if (progress.TotalDocuments == 0)
    {
      return $"{projectPrefix} - no VB documents";
    }

    var documentName = string.IsNullOrWhiteSpace(progress.CurrentDocumentName)
        ? string.Empty
        : $": {progress.CurrentDocumentName}";
    return $"{projectPrefix} - analyzing document {progress.CurrentDocument}/{progress.TotalDocuments}{documentName}";
  }

  private void PopulateProjectExplorer(SolutionStructure solution)
  {
    _projectExplorer.BeginUpdate();
    _projectExplorer.Nodes.Clear();

    foreach (var project in solution.Projects)
    {
      var projectNode = CreateProjectNode(project);
      _projectExplorer.Nodes.Add(projectNode);
      AddTypeNodes(projectNode, project, project.Types);
      projectNode.Expand();
    }

    _projectExplorer.EndUpdate();
  }

  private static TreeNode CreateProjectNode(ProjectStructure project)
  {
    var node = new TreeNode(project.AnalysisStatus switch
    {
      ProjectAnalysisStatus.Partial => $"{project.Name} [Partial]",
      ProjectAnalysisStatus.Failed => $"{project.Name} [Load Failed]",
      _ => project.Name
    })
    {
      Tag = project
    };

    if (project.AnalysisStatus == ProjectAnalysisStatus.Partial)
    {
      node.ForeColor = Color.DarkOrange;
    }
    else if (project.AnalysisStatus == ProjectAnalysisStatus.Failed)
    {
      node.ForeColor = Color.Firebrick;
    }

    var primaryDiagnostic = project.Diagnostics
        .FirstOrDefault(diagnostic => diagnostic.Severity == ProjectAnalysisDiagnosticSeverity.Error)
        ?? project.Diagnostics.FirstOrDefault();
    if (project.AnalysisStatus != ProjectAnalysisStatus.Full || primaryDiagnostic is not null)
    {
      var toolTipLines = new List<string>
      {
        project.AnalysisStatus switch
        {
          ProjectAnalysisStatus.Failed => "Project load failed",
          ProjectAnalysisStatus.Partial => "Project loaded with partial analysis",
          _ => "Project analysis diagnostic"
        }
      };
      if (!string.IsNullOrWhiteSpace(project.FilePath))
      {
        toolTipLines.Add(project.FilePath);
      }

      if (primaryDiagnostic is not null)
      {
        var diagnosticMessage = primaryDiagnostic.Message.ReplaceLineEndings(" ").Trim();
        const int maxToolTipMessageLength = 240;
        if (diagnosticMessage.Length > maxToolTipMessageLength)
        {
          diagnosticMessage = $"{diagnosticMessage[..maxToolTipMessageLength]}...";
        }

        toolTipLines.Add($"{primaryDiagnostic.Stage}: {diagnosticMessage}");
      }

      node.ToolTipText = string.Join(Environment.NewLine, toolTipLines);
    }

    return node;
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
    AddPropertyNodes(typeNode, project, type);
    AddUserInterfaceNode(typeNode, project, type);
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
    if (type.Fields.Count == 0)
    {
      return;
    }

    var groupNode = new TreeNode($"Fields ({type.Fields.Count})")
    {
      Tag = new FieldsNodeContext(project, type)
    };
    typeNode.Nodes.Add(groupNode);
  }

  private static void AddPropertyNodes(TreeNode typeNode, ProjectStructure project, TypeStructure type)
  {
    if (type.Properties.Count == 0)
    {
      return;
    }

    var groupNode = new TreeNode($"Properties ({type.Properties.Count})")
    {
      Tag = new PropertiesNodeContext(project, type)
    };
    typeNode.Nodes.Add(groupNode);
  }

  private static void AddUserInterfaceNode(
      TreeNode typeNode,
      ProjectStructure project,
      TypeStructure type)
  {
    if (type.UiControls.Count == 0 && type.UiEventHandlers.Count == 0)
    {
      return;
    }

    var groupNode = new TreeNode(
        $"User Interface ({type.UiControls.Count} controls, {type.UiEventHandlers.Count} events)")
    {
      Tag = new UserInterfaceNodeContext(project, type)
    };
    typeNode.Nodes.Add(groupNode);
  }

  private static void AddMethodNodes(TreeNode typeNode, ProjectStructure project, TypeStructure type)
  {
    var methods = type.Methods.Where(method => !method.IsGenerated).ToArray();
    if (methods.Length == 0)
    {
      return;
    }

    var groupNode = new TreeNode($"Methods ({methods.Length})") { Tag = type.Methods };
    typeNode.Nodes.Add(groupNode);

    foreach (var method in methods)
    {
      groupNode.Nodes.Add(new TreeNode(method.Name) { Tag = new MethodNodeContext(project, type, method) });
    }
  }

  private void ShowNodeDetails(TreeNode? node)
  {
    var previousTab = _detailsTabs.SelectedTab;
    ClearDetails();

    var relationsAvailable = false;
    var flowAvailable = false;

    switch (node?.Tag)
    {
      case ProjectStructure project:
        ShowProjectOverview(project);
        ShowProjectDependency(project);
        relationsAvailable = true;
        break;
      case TypeNodeContext typeContext:
        ShowClassOverview(typeContext);
        ShowClassDependency(typeContext);
        relationsAvailable = true;
        break;
      case FieldsNodeContext fieldsContext:
        ShowFieldsOverview(fieldsContext);
        break;
      case PropertiesNodeContext propertiesContext:
        ShowPropertiesOverview(propertiesContext);
        break;
      case FieldNodeContext fieldContext:
        ShowFieldOverview(fieldContext);
        break;
      case PropertyNodeContext propertyContext:
        ShowPropertyOverview(propertyContext);
        break;
      case UserInterfaceNodeContext userInterfaceContext:
        ShowUserInterface(userInterfaceContext);
        break;
      case MethodNodeContext methodContext:
        ShowMethodOverview(methodContext);
        ShowMethodCalls(methodContext);
        ShowControlFlow(methodContext);
        relationsAvailable = true;
        flowAvailable = true;
        break;
    }

    RestoreSelectedTab(previousTab, relationsAvailable, flowAvailable);
  }

  private void ShowClassOverview(TypeNodeContext context)
  {
    ShowOverviewView(_classOverviewView);
    _classOverviewView.ShowType(context.Type);
  }

  private void ShowProjectOverview(ProjectStructure project)
  {
    var types = project.Types;
    var topLevelTypes = types.Count(type => type.ContainingTypeSymbolId is null);
    var nestedTypes = types.Count - topLevelTypes;
    var methods = types.SelectMany(type => type.Methods).Where(method => !method.IsGenerated).ToArray();
    var generatedMethods = types
        .SelectMany(type => type.GeneratedMethods)
        .Concat(types.SelectMany(type => type.Methods).Where(method => method.IsGenerated))
        .GroupBy(method => method.SymbolId, StringComparer.Ordinal)
        .Count();
    var fields = types.SelectMany(type => type.Fields).Where(field => !field.IsGenerated).ToArray();
    var properties = types.SelectMany(type => type.Properties).Where(property => !property.IsGenerated).ToArray();
    var uiControls = types.Sum(type => type.UiControls.Count);
    var uiEventHandlers = types.Sum(type => type.UiEventHandlers.Count);
    var internalCalls = project.Calls.Count(call => call.IsProjectInternal);
    var externalCalls = project.Calls.Count(call => !call.IsProjectInternal);
    var fieldReads = project.FieldUsages.Count(usage => usage.UsageKind is FieldUsageKind.Read or FieldUsageKind.ReadWrite);
    var fieldWrites = project.FieldUsages.Count(usage => usage.UsageKind is FieldUsageKind.Write or FieldUsageKind.ReadWrite);
    var dependencyRelations = project.TypeDependencies.Count;
    var dependencyPairs = project.TypeDependencies
        .Select(dependency => $"{dependency.SourceTypeSymbolId}|{dependency.TargetTypeSymbolId}")
        .Distinct(StringComparer.Ordinal)
        .Count();

    var lines = new List<string>
        {
            "Project",
            $"Name                   {project.Name}",
            $"Project File           {FormatOptional(project.FilePath)}",
            $"Analysis Status        {project.AnalysisStatus}",
            string.Empty,
            "Structure",
            $"Types                  {types.Count}",
            $"Top-Level Types        {topLevelTypes}",
            $"Nested Types           {nestedTypes}",
            $"Methods                {methods.Length}",
            $"Fields                 {fields.Length}",
            $"Properties             {properties.Length}",
            $"UI Controls            {uiControls}",
            $"UI Event Handlers      {uiEventHandlers}",
            string.Empty,
            "Analysis",
            $"Internal Call Sites    {internalCalls}",
            $"External Call Sites    {externalCalls}",
            $"Field Reads            {fieldReads}",
            $"Field Writes           {fieldWrites}",
            $"Dependency Relations   {dependencyRelations}",
            $"Dependency Pairs       {dependencyPairs}",
            string.Empty,
            "Generated",
            "Generated Types         (not available)",
            $"Generated Methods       {generatedMethods}",
            string.Empty,
            "Types"
        };

    if (project.Diagnostics.Count > 0)
    {
      var diagnosticsIndex = lines.IndexOf(string.Empty);
      lines.InsertRange(
          diagnosticsIndex,
          new[]
          {
              string.Empty,
              "Diagnostics"
          }.Concat(project.Diagnostics.Select(diagnostic =>
              $"[{diagnostic.Severity}] {diagnostic.Stage}: {diagnostic.Message}")));
    }

    foreach (var type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
    {
      var typeMethods = type.Methods.Count(method => !method.IsGenerated);
      var typeFields = type.Fields.Count(field => !field.IsGenerated);
      var typeProperties = type.Properties.Count(property => !property.IsGenerated);

      lines.Add(type.FullName);
      lines.Add($"  Methods      {typeMethods}");
      lines.Add($"  Fields       {typeFields}");
      lines.Add($"  Properties   {typeProperties}");
      if (type.UiControls.Count > 0)
      {
        lines.Add($"  UI Controls  {type.UiControls.Count}");
      }

      lines.Add(string.Empty);
    }

    _overviewText.Text = string.Join(Environment.NewLine, lines);
  }
  private void ShowFieldOverview(FieldNodeContext context)
  {
    ShowOverviewView(_overviewText);
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

  private void ShowFieldsOverview(FieldsNodeContext context)
  {
    _activeFieldsContext = context;
    _fieldListView.ShowFields(context.Type.Fields);
    ShowOverviewView(_fieldListView);
  }

  private void ShowFieldFromList(FieldStructure field)
  {
    if (_activeFieldsContext is null ||
        !string.Equals(
            field.DeclaringTypeSymbolId,
            _activeFieldsContext.Type.SymbolId,
            StringComparison.Ordinal))
    {
      return;
    }

    ShowFieldOverview(new FieldNodeContext(
        _activeFieldsContext.Project,
        _activeFieldsContext.Type,
        field));
    _detailsTabs.SelectedTab = _overviewTab;
  }

  private void ShowFieldBySymbolId(string fieldSymbolId)
  {
    if (_solution is null)
    {
      return;
    }

    foreach (var project in _solution.Projects)
    {
      foreach (var type in project.Types)
      {
        var field = type.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.SymbolId, fieldSymbolId, StringComparison.Ordinal));
        if (field is null)
        {
          continue;
        }

        ShowFieldOverview(new FieldNodeContext(project, type, field));
        _detailsTabs.SelectedTab = _overviewTab;
        return;
      }
    }
  }

  private void ShowPropertiesOverview(PropertiesNodeContext context)
  {
    _activePropertiesContext = context;
    _propertyListView.ShowProperties(context.Type.Properties);
    ShowOverviewView(_propertyListView);
  }

  private void ShowPropertyFromList(PropertyStructure property)
  {
    if (_activePropertiesContext is null ||
        !_activePropertiesContext.Type.Properties.Contains(property))
    {
      return;
    }

    ShowPropertyOverview(new PropertyNodeContext(
        _activePropertiesContext.Project,
        _activePropertiesContext.Type,
        property));
    _detailsTabs.SelectedTab = _overviewTab;
  }

  private void ShowPropertyOverview(PropertyNodeContext context)
  {
    ShowOverviewView(_overviewText);
    var property = context.Property;
    var lines = new List<string>
    {
      "Property",
      $"Name            {property.Name}",
      $"Type            {property.Type}",
      $"Accessibility   {property.Accessibility}",
      string.Empty,
      "Owner",
      $"Declaring Type  {context.Type.FullName}",
      string.Empty,
      "Declaration",
      $"File            {property.FilePath ?? "(unknown)"}",
      $"Line            {property.Span.StartLine}",
      string.Empty,
      "Modifiers",
      $"Shared          {FormatBoolean(property.IsShared)}",
      $"Readable        {FormatBoolean(!property.IsWriteOnly)}",
      $"Writable        {FormatBoolean(!property.IsReadOnly)}"
    };

    _overviewText.Text = string.Join(Environment.NewLine, lines);
  }

  private void ShowUserInterface(UserInterfaceNodeContext context)
  {
    _userInterfaceView.ShowType(context.Type);
    ShowOverviewView(_userInterfaceView);
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
    var errorHandlingMode = controlFlow?.Nodes
        .SelectMany(node => node.Operations)
        .Where(operation => operation.Role == ControlFlowOperationRole.ErrorHandlingDirective)
        .Select(operation => operation.Text)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];
    var isEventHandler = context.Type.UiEventHandlers.Any(handler =>
        string.Equals(handler.HandlerMethodSymbolId, method.SymbolId, StringComparison.Ordinal) ||
        string.Equals(handler.HandlerMethodName, method.Name, StringComparison.Ordinal));

    ShowOverviewView(_methodOverviewView);
    _methodOverviewView.ShowMethod(
        method,
        incomingCalls.Length,
        outgoingCalls.Length,
        externalCalls.Length,
        cfgBlocks,
        conditions,
        errorHandlingMode.Length == 0 ? "None" : string.Join(", ", errorHandlingMode),
        isEventHandler);
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

    ShowRelationsView(_callGraphView);
    _callGraphView.ShowGraph(context.Method, incomingCalls, outgoingCalls);
  }

  private void ShowClassDependency(TypeNodeContext context)
  {
    ShowRelationsView(_classDependencyView);
    _classDependencyView.ShowGraph(
        context.Type,
        context.Project.Types,
        context.Project.TypeDependencies);
  }

  private void ShowProjectDependency(ProjectStructure project)
  {
    ShowRelationsView(_projectDependencyView);
    _projectDependencyView.ShowGraph(project);
  }
  private void ShowControlFlow(MethodNodeContext context)
  {
    var controlFlow = context.Project.ControlFlows.FirstOrDefault(flow => flow.MethodId == context.Method.SymbolId);
    ShowFlowView(_controlFlowGraphView);
    _controlFlowGraphView.ShowGraph(controlFlow);
    _exportFlowImageMenuItem.Enabled = _controlFlowGraphView.CanExport;
    _exportFlowDiagramMenuItem.Enabled = _controlFlowGraphView.CanExport;
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

  private void SelectTypeNodeBySymbolId(string typeSymbolId)
  {
    var node = FindTypeNode(_projectExplorer.Nodes, typeSymbolId);
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

  private static TreeNode? FindTypeNode(TreeNodeCollection nodes, string typeSymbolId)
  {
    foreach (TreeNode node in nodes)
    {
      if (node.Tag is TypeNodeContext context &&
          string.Equals(context.Type.SymbolId, typeSymbolId, StringComparison.Ordinal))
      {
        return node;
      }

      var match = FindTypeNode(node.Nodes, typeSymbolId);
      if (match is not null)
      {
        return match;
      }
    }

    return null;
  }

  private void ClearDetails()
  {
    _exportFlowImageMenuItem.Enabled = false;
    _exportFlowDiagramMenuItem.Enabled = false;
    _activeFieldsContext = null;
    _activePropertiesContext = null;
    _overviewText.Clear();
    _classOverviewView.ClearOverview();
    _methodOverviewView.ClearOverview();
    _fieldListView.ClearFields();
    _propertyListView.ClearProperties();
    _userInterfaceView.ClearView();
    ShowOverviewView(_overviewText);
    _classDependencyView.ClearGraph();
    _projectDependencyView.ClearGraph();
    _callGraphView.ClearGraph();
    _controlFlowGraphView.ClearGraph();
    SetRelationsUnavailable();
    SetFlowUnavailable();
  }

  private void ShowOverviewView(Control view)
  {
    _overviewHost.Controls.Clear();
    view.Dock = DockStyle.Fill;
    _overviewHost.Controls.Add(view);
  }

  private void ShowRelationsView(Control view)
  {
    _relationsHost.Controls.Clear();
    view.Dock = DockStyle.Fill;
    _relationsHost.Controls.Add(view);
  }

  private void SetRelationsUnavailable()
  {
    _relationsHost.Controls.Clear();
    _relationsHost.Controls.Add(_relationsUnavailableLabel);
  }

  private void ShowFlowView(Control view)
  {
    _flowHost.Controls.Clear();
    view.Dock = DockStyle.Fill;
    _flowHost.Controls.Add(view);
  }

  private void SetFlowUnavailable()
  {
    _flowHost.Controls.Clear();
    _flowHost.Controls.Add(_flowUnavailableLabel);
  }

  private void RestoreSelectedTab(
      TabPage? previousTab,
      bool relationsAvailable,
      bool flowAvailable)
  {
    if (previousTab == _relationsTab && relationsAvailable)
    {
      _detailsTabs.SelectedTab = _relationsTab;
      return;
    }

    if (previousTab == _flowTab && flowAvailable)
    {
      _detailsTabs.SelectedTab = _flowTab;
      return;
    }

    _detailsTabs.SelectedTab = _overviewTab;
  }

  private static TabPage CreateTabPage(string title, Control content)
  {
    var page = new TabPage(title);
    content.Dock = DockStyle.Fill;
    page.Controls.Add(content);
    return page;
  }

  private static System.Windows.Forms.Label CreateUnavailableLabel(string text)
  {
    return new System.Windows.Forms.Label
    {
      Dock = DockStyle.Fill,
      Text = text,
      TextAlign = ContentAlignment.MiddleCenter
    };
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

  private sealed record FieldsNodeContext(ProjectStructure Project, TypeStructure Type);

  private sealed record PropertiesNodeContext(ProjectStructure Project, TypeStructure Type);

  private sealed record FieldNodeContext(ProjectStructure Project, TypeStructure Type, FieldStructure Field);

  private sealed record PropertyNodeContext(ProjectStructure Project, TypeStructure Type, PropertyStructure Property);

  private sealed record UserInterfaceNodeContext(ProjectStructure Project, TypeStructure Type);

  private sealed record MethodNodeContext(ProjectStructure Project, TypeStructure Type, MethodStructure Method);
}





