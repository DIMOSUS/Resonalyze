using Resonalyze.Screenshots;

// Re-takes the documentation's screenshots from the current build.
//
//   dotnet run --project tools/Resonalyze.Screenshots                 all of them
//   dotnet run --project tools/Resonalyze.Screenshots -- fr gd        just those
//   dotnet run --project tools/Resonalyze.Screenshots -- --list       what there is
//
// STA because the panels register drag-drop targets, which OLE refuses off one, and
// because the whole run drives real windows.
int exitCode = 0;
var thread = new Thread(() => exitCode = Run(args));
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exitCode;

static int Run(string[] args)
{
    var requested = new List<string>();
    string? configPath = null;
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--list":
                foreach (Scene listed in Shots.All)
                {
                    Console.WriteLine(listed.OnRequest
                        ? $"{listed.Name}: (only when asked for by name)"
                        : $"{listed.Name}:");
                    foreach (string shot in listed.Shots)
                    {
                        Console.WriteLine($"  {shot}");
                    }
                }

                return 0;

            case "--config" when i + 1 < args.Length:
                configPath = args[++i];
                break;

            default:
                requested.Add(args[i]);
                break;
        }
    }

    ShotConfig config;
    try
    {
        config = ShotConfig.Load(configPath);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }

    string[] unknown = [.. requested.Where(
        name => !Shots.All.Any(scene => scene.Shots.Contains(name)))];
    if (unknown.Length > 0)
    {
        Console.Error.WriteLine(
            $"Unknown shot(s): {string.Join(", ", unknown)}. Try --list.");
        return 2;
    }

    bool Wanted(string name) => requested.Count == 0 || requested.Contains(name);
    Scene[] scenes = [.. Shots.All.Where(
        scene => scene.Shots.Any(Wanted) && (!scene.OnRequest || requested.Count > 0))];

    Application.EnableVisualStyles();
    Application.SetHighDpiMode(HighDpiMode.SystemAware);
    Console.WriteLine($"Writing to {config.OutputRoot}");

    bool failed = false;
    foreach (Scene scene in scenes)
    {
        Console.WriteLine($"{scene.Name}:");
        try
        {
            ShotSession.Run(config, scene.WindowSize, session => scene.Body(session, Wanted));
        }
        catch (Exception exception)
        {
            // One scene failing must not cost the others: a renamed control in the
            // EQ Wizard should not also block re-shooting the analysis modes. The
            // run still ends non-zero, so nothing reads a partial sweep as a success.
            Console.Error.WriteLine($"  {scene.Name} FAILED: {Unwrap(exception).Message}");
            failed = true;
        }
    }

    return failed ? 1 : 0;
}

static Exception Unwrap(Exception exception) =>
    exception.InnerException is { } inner ? Unwrap(inner) : exception;
