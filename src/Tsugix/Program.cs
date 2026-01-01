using System.CommandLine;
using System.Reflection;
using Tsugix.Commands;
using Tsugix.Logging;

namespace Tsugix;

/// <summary>
/// Tsugix - The Self-Healing Wrapper for Developers
/// Entry point for the CLI application.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Initialize logging (default to non-verbose, RunCommand will reinitialize if --verbose)
        TsugixLogger.Initialize(verbose: args.Contains("--verbose"));
        
        try
        {
            var rootCommand = new RootCommand("Tsugix - The Self-Healing Wrapper for Developers")
            {
                Name = "tsugi"
            };
            
            // Add version option
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "0.1.0";
            
            var versionOption = new Option<bool>(
                aliases: ["--version", "-v"],
                description: "Show version information");
            
            rootCommand.AddGlobalOption(versionOption);
            
            rootCommand.SetHandler((bool showVersion) =>
            {
                if (showVersion)
                {
                    Console.WriteLine($"tsugi {version}");
                    Console.WriteLine("The Self-Healing Wrapper for Developers");
                }
            }, versionOption);
            
            // Add the run command
            rootCommand.AddCommand(new RunCommand());
            
            return await rootCommand.InvokeAsync(args);
        }
        finally
        {
            TsugixLogger.Shutdown();
        }
    }
}
