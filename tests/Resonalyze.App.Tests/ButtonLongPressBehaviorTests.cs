using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The record-button long-press state machine moved off Form1 into
/// <see cref="ButtonLongPressBehavior"/>; these drive the internal handlers
/// directly (the WinForms timer needs a message pump) and pin the fire-once
/// and click-suppression semantics.
/// </summary>
public sealed class ButtonLongPressBehaviorTests
{
    [Fact]
    public async Task LongPress_FiresOnceAndSuppressesExactlyOneClick()
    {
        using var button = new Button();
        int longPresses = 0;
        using var behavior = new ButtonLongPressBehavior(
            button,
            650,
            canTrigger: () => true,
            onLongPress: () =>
            {
                longPresses++;
                return Task.CompletedTask;
            });

        behavior.HandleMouseDown(MouseButtons.Left);
        await behavior.HandleLongPressElapsedAsync();
        await behavior.HandleLongPressElapsedAsync();

        Assert.Equal(1, longPresses);
        Assert.True(behavior.ConsumeClickSuppression());
        Assert.False(behavior.ConsumeClickSuppression());
    }

    [Fact]
    public void Click_WithoutLongPress_IsNotSuppressed()
    {
        using var button = new Button();
        using var behavior = new ButtonLongPressBehavior(
            button,
            650,
            canTrigger: () => true,
            onLongPress: () => Task.CompletedTask);

        behavior.HandleMouseDown(MouseButtons.Left);

        Assert.False(behavior.ConsumeClickSuppression());
    }

    [Fact]
    public async Task Elapsed_DoesNotFireWhenTheConditionTurnedFalse()
    {
        using var button = new Button();
        bool canTrigger = true;
        int longPresses = 0;
        using var behavior = new ButtonLongPressBehavior(
            button,
            650,
            canTrigger: () => canTrigger,
            onLongPress: () =>
            {
                longPresses++;
                return Task.CompletedTask;
            });

        behavior.HandleMouseDown(MouseButtons.Left);
        canTrigger = false;
        await behavior.HandleLongPressElapsedAsync();

        Assert.Equal(0, longPresses);
        Assert.False(behavior.ConsumeClickSuppression());
    }

    [Fact]
    public async Task NewPress_AfterALongPress_ArmsAFreshCycle()
    {
        using var button = new Button();
        int longPresses = 0;
        using var behavior = new ButtonLongPressBehavior(
            button,
            650,
            canTrigger: () => true,
            onLongPress: () =>
            {
                longPresses++;
                return Task.CompletedTask;
            });

        behavior.HandleMouseDown(MouseButtons.Left);
        await behavior.HandleLongPressElapsedAsync();
        Assert.True(behavior.ConsumeClickSuppression());

        behavior.HandleMouseDown(MouseButtons.Left);
        await behavior.HandleLongPressElapsedAsync();

        Assert.Equal(2, longPresses);
        Assert.True(behavior.ConsumeClickSuppression());
    }
}
