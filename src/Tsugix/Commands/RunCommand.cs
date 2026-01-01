using System.CommandLine;
using System.CommandLine.Invocation;
using Tsugix.AiSurgeon;
using Tsugix.ContextEngine;
using Tsugix.Core;
using Tsugix.Process;

namespace Tsugix.Commands;

/// <summary>
/// The 'run' command that executes wrapped processes.
/// </summary>
public class RunCommand : Command
{
    public RunCommand() : base("run", "Execute a command and monitor for crashes")
    {
        // The command arguments after '--'
        var commandArgument = new Argument<string[]>(
            name: "command",
            description: "The command and arguments to execute")
        {
            Arity = ArgumentArity.OneOrMore
        };
        
        // AI Surgeon options
        var skipAiOption = new Option<bool>(
            name: "--skip-ai",
            description: "Skip AI analysis and just show the error");
        
        var autoApplyOption = new Option<bool>(
            name: "--auto-apply",
            description: "Automatically apply suggested fixes without confirmation");
        
        var autoRerunOption = new Option<bool>(
            name: "--auto-rerun",
            description: "Automatically re-run the command after applying fixes");
        
        var noRerunOption = new Option<bool>(
            name: "--no-rerun",
            description: "Skip the re-run prompt after applying fixes");
        
        var allowOutsideRootOption = new Option<bool>(
            name: "--allow-outside-root",
            description: "Allow patching files outside the working directory (security risk)");
        
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose logging output");
        
        AddArgument(commandArgument);
        AddOption(skipAiOption);
        AddOption(autoApplyOption);
        AddOption(autoRerunOption);
        AddOption(noRerunOption);
        AddOption(allowOutsideRootOption);
        AddOption(verboseOption);
        
        this.SetHandler(ExecuteAsync, commandArgument, skipAiOption, autoApplyOption, autoRerunOption, noRerunOption, allowOutsideRootOption, verboseOption);
    }
    
    private async Task<int> ExecuteAsync(
        string[] commandArgs, 
        bool skipAi, 
        bool autoApply, 
        bool autoRerun, 
        bool noRerun,
        bool allowOutsideRoot,
        bool verbose)
    {
        if (commandArgs.Length == 0)
        {
            Console.Error.WriteLine("tsugi: error: No command specified");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Try 'tsugi --help' for more information.");
            return 1;
        }
        
        var parsed = CommandParser.Parse(commandArgs);
        if (parsed == null)
        {
            Console.Error.WriteLine("tsugi: error: Invalid command");
            return 1;
        }
        
        var (executable, arguments) = parsed.Value;
        
        // Set up cancellation for Ctrl+C (SIGINT)
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        
        // Handle SIGTERM (process exit)
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            cts.Cancel();
        };
        
        var processManager = new ProcessManager();
        var result = await processManager.ExecuteAsync(executable, arguments, cts.Token);
        
        // If process succeeded, just return
        if (result.IsSuccess)
        {
            return result.ExitCode;
        }
        
        // Process crashed - invoke Phase 2 (Context Engine) and Phase 3 (AI Surgeon)
        var crashReport = CrashReport.FromExecutionResult(result, Environment.CurrentDirectory);
        
        // Phase 2: Parse error and extract context
        var contextEngine = ContextEngine.ContextEngine.CreateDefault();
        var errorContext = contextEngine.Process(crashReport);
        
        if (errorContext == null)
        {
            // No parseable error, just return the exit code
            return result.ExitCode;
        }
        
        // Phase 3: AI Surgeon analysis and fix
        var configManager = new ConfigManager();
        var config = configManager.Load(Environment.CurrentDirectory);
        
        var aiOptions = new AiSurgeonOptions
        {
            SkipAi = skipAi,
            AutoApply = autoApply,
            AutoRerun = autoRerun,
            NoRerun = noRerun,
            AllowOutsideRoot = allowOutsideRoot,
            Verbose = verbose
        };
        
        var aiSurgeon = AiSurgeon.AiSurgeon.Create(config, configManager, aiOptions);
        var fixResult = await aiSurgeon.AnalyzeAndFixAsync(errorContext, aiOptions, cts.Token);
        
        // Handle re-run if fix was applied
        if (fixResult.Outcome == FixOutcome.Applied && !noRerun)
        {
            var userInteraction = new UserInteraction(autoApply, autoRerun);
            
            if (autoRerun || userInteraction.PromptRerun())
            {
                var rerunResult = await processManager.ExecuteAsync(executable, arguments, cts.Token);
                
                if (rerunResult.IsSuccess)
                {
                    userInteraction.ShowRerunSuccess();
                }
                else
                {
                    userInteraction.ShowRerunFailed(rerunResult.ExitCode);
                }
                
                return rerunResult.ExitCode;
            }
        }
        
        return result.ExitCode;
    }
}
