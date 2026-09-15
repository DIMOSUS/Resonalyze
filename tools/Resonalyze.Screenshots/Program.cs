using Resonalyze.Screenshots;

// Re-takes the documentation's screenshots: no args = all, names = those, --list = catalogue.
// STA: drag-drop registration (OLE) and real windows require it.
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
    // As in the app's ApplicationConfiguration: GDI+ text measures differently, and a tight AutoEllipsis label then draws nothing.
    Application.SetCompatibleTextRenderingDefault(false);
    Console.WriteLine($"Writing to {config.OutputRoot}");

    bool failed = false;
    foreach (Scene scene in scenes)
    {
        Console.WriteLine($"{scene.Name}:");
        // Missing material skips a scene in a sweep but fails a shot asked for by name, so a named re-shoot never exits 0 empty-handed.
        if (scene.Unavailable?.Invoke(config) is { } reason)
        {
            if (requested.Count > 0)
            {
                Console.Error.WriteLine($"  {scene.Name} FAILED: {reason}");
                failed = true;
                continue;
            }

            Console.WriteLine($"  skipped: {reason}");
            continue;
        }

        try
        {
            ShotSession.Run(config, scene.WindowSize, session => scene.Body(session, Wanted));
        }
        catch (Exception exception)
        {
            // One failing scene must not block the others; the run still exits non-zero.
            Console.Error.WriteLine($"  {scene.Name} FAILED: {Unwrap(exception).Message}");
            failed = true;
        }
    }

    return failed ? 1 : 0;
}

static Exception Unwrap(Exception exception) =>
    exception.InnerException is { } inner ? Unwrap(inner) : exception;
