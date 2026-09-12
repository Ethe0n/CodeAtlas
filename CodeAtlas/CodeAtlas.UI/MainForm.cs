using CodeAtlas.Roslyn;
using CodeAtlas.Roslyn.Models;

namespace CodeAtlas.UI;

public sealed class MainForm : Form
{
    private readonly TreeView _projectExplorer = new();
    private readonly TabControl _detailsTabs = new();
    private readonly TextBox _overviewText = CreateReadOnlyTextBox();
    private readonly TextBox _callGraphText = CreateReadOnlyTextBox();
    private readonly ControlFlowGraphView _controlFlowGraphView = new();
    private readonly ToolStripStatusLabel _statusLabel = new("Ready");

    private SolutionStructure? _solution;

    public MainForm()
    {
        Text = "CodeAtlas";
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(900, 600);

        BuildLayout();
    }

    private void BuildLayout()
    {
        var menuStrip = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        var openSolutionMenuItem = new ToolStripMenuItem("&Open Solution...", null, async (_, _) => await OpenSolutionAsync());
        fileMenu.DropDownItems.Add(openSolutionMenuItem);
        menuStrip.Items.Add(fileMenu);
        MainMenuStrip = menuStrip;

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 360
        };

        _projectExplorer.Dock = DockStyle.Fill;
        _projectExplorer.HideSelection = false;
        _projectExplorer.AfterSelect += (_, args) => ShowNodeDetails(args.Node);
        splitContainer.Panel1.Controls.Add(_projectExplorer);

        _detailsTabs.Dock = DockStyle.Fill;
        _detailsTabs.TabPages.Add(CreateTabPage("Overview", _overviewText));
        _detailsTabs.TabPages.Add(CreateTabPage("Call Graph", _callGraphText));
        _detailsTabs.TabPages.Add(CreateTabPage("Control Flow", _controlFlowGraphView));
        splitContainer.Panel2.Controls.Add(_detailsTabs);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        Controls.Add(splitContainer);
        Controls.Add(statusStrip);
        Controls.Add(menuStrip);

        menuStrip.Dock = DockStyle.Top;
        statusStrip.Dock = DockStyle.Bottom;
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

            foreach (var type in project.Types)
            {
                var typeNode = new TreeNode(type.Name) { Tag = new TypeNodeContext(project, type) };
                projectNode.Nodes.Add(typeNode);

                AddFieldNodes(typeNode, type);
                AddUiControlNodes(typeNode, type);
                AddUiEventHandlerNodes(typeNode, type);
                AddMethodNodes(typeNode, project, type);
            }
        }

        _projectExplorer.ExpandAll();
        _projectExplorer.EndUpdate();
    }

    private static void AddFieldNodes(TreeNode typeNode, TypeStructure type)
    {
        var groupNode = new TreeNode("Fields") { Tag = type.Fields };
        typeNode.Nodes.Add(groupNode);

        foreach (var field in type.Fields)
        {
            groupNode.Nodes.Add(new TreeNode($"{field.Name} : {field.Type}") { Tag = field });
        }
    }

    private static void AddUiControlNodes(TreeNode typeNode, TypeStructure type)
    {
        var groupNode = new TreeNode("UI Controls") { Tag = type.UiControls };
        typeNode.Nodes.Add(groupNode);

        foreach (var control in type.UiControls)
        {
            groupNode.Nodes.Add(new TreeNode($"{control.Name} : {ShortTypeName(control.Type)}") { Tag = control });
        }
    }

    private static void AddUiEventHandlerNodes(TreeNode typeNode, TypeStructure type)
    {
        var groupNode = new TreeNode("UI Event Handlers") { Tag = type.UiEventHandlers };
        typeNode.Nodes.Add(groupNode);

        foreach (var handler in type.UiEventHandlers)
        {
            groupNode.Nodes.Add(new TreeNode($"{handler.ControlName}.{handler.EventName} -> {handler.HandlerMethodName}") { Tag = handler });
        }
    }

    private static void AddMethodNodes(TreeNode typeNode, ProjectStructure project, TypeStructure type)
    {
        var groupNode = new TreeNode("Methods") { Tag = type.Methods };
        typeNode.Nodes.Add(groupNode);

        foreach (var method in type.Methods.Where(method => !method.IsGenerated))
        {
            groupNode.Nodes.Add(new TreeNode(method.Name) { Tag = new MethodNodeContext(project, type, method) });
        }
    }

    private void ShowNodeDetails(TreeNode? node)
    {
        ClearDetails();

        if (node?.Tag is not MethodNodeContext context)
        {
            return;
        }

        ShowMethodOverview(context);
        ShowMethodCalls(context);
        ShowControlFlow(context);
    }

    private void ShowMethodOverview(MethodNodeContext context)
    {
        var method = context.Method;
        _overviewText.Text = string.Join(
            Environment.NewLine,
            $"Method Name: {method.Name}",
            $"Full Name / Id: {method.SymbolId}",
            $"Accessibility: {method.Accessibility}",
            $"Declaring File: {method.FilePath ?? "(unknown)"}",
            $"Source Line: {method.Span.StartLine}");
    }

    private void ShowMethodCalls(MethodNodeContext context)
    {
        var calls = context.Project.Calls
            .Where(call => call.CallerMethodSymbolId == context.Method.SymbolId)
            .ToArray();

        if (calls.Length == 0)
        {
            _callGraphText.Text = "(no outgoing calls)";
            return;
        }

        var internalCalls = calls.Where(call => call.IsProjectInternal).ToArray();
        var externalCalls = calls.Where(call => !call.IsProjectInternal).ToArray();
        var lines = new List<string>();

        AppendCallGroup(lines, "Internal", internalCalls);
        if (lines.Count > 0 && externalCalls.Length > 0)
        {
            lines.Add(string.Empty);
        }

        AppendCallGroup(lines, "External", externalCalls);

        _callGraphText.Text = string.Join(Environment.NewLine, lines);
    }

    private void ShowControlFlow(MethodNodeContext context)
    {
        var controlFlow = context.Project.ControlFlows.FirstOrDefault(flow => flow.MethodId == context.Method.SymbolId);
        _controlFlowGraphView.ShowGraph(controlFlow);
    }

    private static void AppendCallGroup(List<string> lines, string title, IReadOnlyList<CallRelation> calls)
    {
        if (calls.Count == 0)
        {
            return;
        }

        lines.Add(title);
        foreach (var call in calls)
        {
            lines.Add($"  -> {call.CalleeDisplayName}");
        }
    }

    private void ClearDetails()
    {
        _overviewText.Clear();
        _callGraphText.Clear();
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

    private sealed record TypeNodeContext(ProjectStructure Project, TypeStructure Type);

    private sealed record MethodNodeContext(ProjectStructure Project, TypeStructure Type, MethodStructure Method);
}
