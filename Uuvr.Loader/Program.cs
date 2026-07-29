using System.Runtime.InteropServices;
using Uuvr.Loader.Core;
using Uuvr.Loader.UI;

namespace Uuvr.Loader;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            AttachConsole(AttachParentProcess);
            Console.WriteLine();
            return RunCli(args);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    // Headless mode so the whole flow can be scripted:
    //   UuvrLoader detect|install|uninstall|launch <game.exe> [--generation legacy|modern]
    private static int RunCli(string[] args)
    {
        var command = args[0].ToLowerInvariant().TrimStart('-', '/');

        if (command is "help" or "h" or "?" || args.Length < 2)
        {
            Console.WriteLine("UUVR Loader " + LoaderVersion.Value);
            Console.WriteLine("Usage:");
            Console.WriteLine("  UuvrLoader detect <game.exe>");
            Console.WriteLine("  UuvrLoader install <game.exe> [--generation legacy|modern]");
            Console.WriteLine("  UuvrLoader uninstall <game.exe>");
            Console.WriteLine("  UuvrLoader enable <game.exe>    (VR on)");
            Console.WriteLine("  UuvrLoader disable <game.exe>   (play flat, keep install)");
            Console.WriteLine("  UuvrLoader launch <game.exe>");
            Console.WriteLine("Run with no arguments to open the graphical interface.");
            return command is "help" or "h" or "?" ? 0 : 1;
        }

        var exePath = args[1];
        var game = UnityGameInfo.Detect(exePath);
        if (game == null)
        {
            Console.WriteLine($"'{exePath}' doesn't look like a Unity game (no _Data folder found next to it).");
            return 1;
        }

        var installer = new ModInstaller(Console.WriteLine);

        switch (command)
        {
            case "detect":
            {
                var manifest = ModInstaller.ReadManifest(game);
                Console.WriteLine($"Name:          {game.Name}");
                Console.WriteLine($"Unity version: {(game.UnityVersion.Length > 0 ? game.UnityVersion : "unknown")}");
                Console.WriteLine($"Backend:       {game.Backend}");
                Console.WriteLine($"Generation:    {game.Generation}");
                Console.WriteLine($"Architecture:  {game.Architecture}");
                Console.WriteLine($"Mod flavor:    {ModInstaller.GetModFlavor(game.Backend, game.Generation)}");
                Console.WriteLine($"UUVR:          {(manifest != null ? $"installed ({manifest.UuvrVersion})" : "not installed")}");
                return 0;
            }

            case "install":
            {
                UnityGeneration? generationOverride = null;
                var generationArgIndex = Array.IndexOf(args, "--generation");
                if (generationArgIndex >= 0 && generationArgIndex + 1 < args.Length)
                {
                    generationOverride = args[generationArgIndex + 1].ToLowerInvariant() switch
                    {
                        "legacy" => UnityGeneration.Legacy,
                        "modern" => UnityGeneration.Modern,
                        _ => null,
                    };
                }

                var runtimeName = ModInstaller.GetRuntimeName(game.Backend, game.Architecture);
                if (!ModInstaller.HasForeignBepInEx(game) &&
                    !RuntimeDownloader.EnsureRuntimeAsync(runtimeName, Console.WriteLine).GetAwaiter().GetResult())
                {
                    return 1;
                }

                var result = installer.Install(game, generationOverride);
                return result.Success ? 0 : 1;
            }

            case "uninstall":
            {
                var result = installer.Uninstall(game);
                return result.Success ? 0 : 1;
            }

            case "enable":
            case "disable":
            {
                var result = installer.SetVrEnabled(game, command == "enable");
                return result.Success ? 0 : 1;
            }

            case "launch":
            {
                Console.WriteLine($"Launching {game.Name}...");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = game.ExePath,
                    WorkingDirectory = game.GameDir,
                    UseShellExecute = true,
                });
                return 0;
            }

            default:
                Console.WriteLine($"Unknown command '{command}'. Run 'UuvrLoader help' for usage.");
                return 1;
        }
    }
}
