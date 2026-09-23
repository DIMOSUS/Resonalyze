using System.Windows.Forms;

namespace Resonalyze.App.Tests;

public sealed class AgentProgressDialogTests
{
    private static Label Label(Form dialog, string name) =>
        (Label)dialog.Controls.Find(name, searchAllChildren: true).Single();

    [Fact]
    public void Report_KeepsTheStepsBefore_AndShowsTheOneRunning()
    {
        StaTest.Run(() =>
        {
            using var dialog = new AgentProgressDialog("Import AI proposal", "Reading the reply…");
            dialog.Show();

            Assert.Equal("Reading the reply…", Label(dialog, "labelStep").Text);
            Assert.Empty(Label(dialog, "labelDone").Text);

            dialog.Report("Probe 1 of 2 (junction left:C-D): reading…");
            dialog.Report("Junction tune C/D: searching the crossover…");

            Assert.Equal("Junction tune C/D: searching the crossover…", Label(dialog, "labelStep").Text);
            Assert.Contains("Reading the reply…", Label(dialog, "labelDone").Text);
            Assert.Contains("Probe 1 of 2", Label(dialog, "labelDone").Text);
            dialog.Close();
        });
    }

    [Fact]
    public void Report_FromTheWorkerThread_ReachesTheWindow()
    {
        StaTest.Run(() =>
        {
            using var dialog = new AgentProgressDialog("Copy diagnostics for AI", "Excess group delay…");
            dialog.Show();

            Task.Run(() => dialog.Report("Excess group delay: B:left…")).GetAwaiter().GetResult();
            for (int pump = 0; pump < 20 && !Label(dialog, "labelStep").Text.Contains("B:left"); pump++)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            Assert.Contains("B:left", Label(dialog, "labelStep").Text);
            dialog.Close();
        });
    }

    [Fact]
    public async Task RunAsync_ClosesTheWindowEvenWhenTheWorkThrows()
    {
        AgentProgressDialog? seen = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentProgressDialog.RunAsync<bool>(null, "t", "first", dialog =>
            {
                seen = dialog;
                dialog.Report("second");
                throw new InvalidOperationException("the engine gave up");
            }));

        Assert.NotNull(seen);
        Assert.True(seen!.IsDisposed || !seen.Visible);
    }
}
