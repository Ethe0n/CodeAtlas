namespace CodeAtlas.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 1 && args[0] == GraphLayoutWorker.Argument)
        {
            Environment.ExitCode = GraphLayoutWorker.Run();
            return;
        }

        Application.Run(new MainForm());
    }
}
