namespace Resonalyze;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppProfiler.SetThreadName("UI");
        Application.Run(new Form1());
    }
}
