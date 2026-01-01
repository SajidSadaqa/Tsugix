using Microsoft.Extensions.Logging;
using Tsugix.ContextEngine;
using Tsugix.Logging;
using Tsugix.Telemetry;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Orchestrates AI-powered fix generation and application.
/// Wires together all AI Surgeon components.
/// </summary>
public sealed class AiSurgeon : IAiSurgeon
{
    private readonly ILlmClient? _llmClient;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IFixParser _fixParser;
    private readonly IDiffView _diffView;
    private readonly IFilePatcher _filePatcher;
    private readonly UserInteraction _userInteraction;
    private readonly TsugixConfig _config;
    private readonly ILogger<AiSurgeon> _logger;
    
    /// <summary>
    /// Creates a new AI Surgeon with all dependencies.
    /// </summary>
    public AiSurgeon(
        ILlmClient? llmClient,
        IPromptBuilder promptBuilder,
        IFixParser fixParser,
        IDiffView diffView,
        IFilePatcher filePatcher,
        UserInteraction userInteraction,
        TsugixConfig config)
    {
        _llmClient = llmClient;
        _promptBuilder = promptBuilder;
        _fixParser = fixParser;
        _diffView = diffView;
        _filePatcher = filePatcher;
        _userInteraction = userInteraction;
        _config = config;
        _logger = TsugixLogger.CreateLogger<AiSurgeon>();
    }
    
    /// <summary>
    /// Creates a new AI Surgeon with default components.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="configManager">The configuration manager for API keys.</param>
    /// <param name="options">Options for the AI Surgeon.</param>
    public static AiSurgeon Create(TsugixConfig config, ConfigManager configManager, AiSurgeonOptions options)
    {
        ILlmClient? llmClient = null;
        
        if (!options.SkipAi && configManager.HasApiKey(config.Provider))
        {
            llmClient = LlmClientFactory.Create(config, configManager);
        }
        
        var rootDirectory = config.RootDirectory ?? Environment.CurrentDirectory;
        var backupService = new FileBackupService(rootDirectory);
        var filePatcher = new FilePatcher(backupService, rootDirectory, options.AllowOutsideRoot);
        
        return new AiSurgeon(
            llmClient,
            new PromptBuilder(),
            new JsonFixParser(),
            new SpectreConsoleDiffView(),
            filePatcher,
            new UserInteraction(options.AutoApply, options.AutoRerun),
            config);
    }
    
    /// <inheritdoc />
    public async Task<FixResult> AnalyzeAndFixAsync(
        ErrorContext errorContext,
        AiSurgeonOptions options,
        CancellationToken cancellationToken = default)
    {
        // Skip AI if requested
        if (options.SkipAi)
        {
            _userInteraction.ShowAiSkipped();
            return FixResult.Skipped();
        }
        
        // Check if LLM client is available
        if (_llmClient == null)
        {
            var envVarName = ConfigManager.GetApiKeyEnvVarName(_config.Provider);
            _logger.LogWarning(LogEvents.ApiKeyMissing, "API key not configured for {Provider}", _config.Provider);
            _userInteraction.ShowMissingApiKey(envVarName);
            return FixResult.Skipped();
        }
        
        try
        {
            _logger.LogDebug(LogEvents.LlmRequestStarted, 
                "Starting LLM request for {Language} error: {ExceptionType}", 
                errorContext.Language, errorContext.Exception.Type);
            
            // Build prompts
            var systemPrompt = _promptBuilder.BuildSystemPrompt();
            var userPrompt = _promptBuilder.BuildUserPrompt(errorContext, new PromptOptions
            {
                MaxTokens = _config.MaxTokens,
                CustomPromptTemplate = _config.CustomPromptTemplate
            });
            
            // Call LLM
            var llmOptions = LlmClientFactory.CreateOptions(_config);
            var response = await _llmClient.CompleteAsync(
                systemPrompt, 
                userPrompt, 
                llmOptions, 
                cancellationToken);
            
            _logger.LogDebug(LogEvents.LlmRequestCompleted, "LLM request completed successfully");
            
            // Parse response
            var suggestion = _fixParser.Parse(response);
            
            if (suggestion == null)
            {
                _logger.LogWarning(LogEvents.LlmRequestFailed, "Could not parse AI response");
                _userInteraction.ShowAiError("Could not parse AI response");
                return FixResult.NoFix();
            }
            
            _logger.LogDebug(LogEvents.FixSuggestionParsed, 
                "Parsed fix suggestion for {FilePath}", suggestion.FilePath);
            
            // Display diff
            _diffView.Display(suggestion);
            
            // Get user confirmation
            var confirmation = _userInteraction.PromptFixConfirmation();
            
            switch (confirmation)
            {
                case FixConfirmation.Apply:
                    return ApplyFix(suggestion, errorContext.Language);
                    
                case FixConfirmation.Skip:
                    _logger.LogInformation(LogEvents.FixRejected, "User skipped fix");
                    TsugixMetrics.Instance.RecordFixResult(errorContext.Language, applied: false);
                    _userInteraction.ShowFixSkipped();
                    return FixResult.Rejected(suggestion);
                    
                case FixConfirmation.Edit:
                    // Edit not implemented yet
                    _logger.LogInformation(LogEvents.FixRejected, "User requested edit (not implemented)");
                    TsugixMetrics.Instance.RecordFixResult(errorContext.Language, applied: false);
                    _userInteraction.ShowFixSkipped();
                    return FixResult.Rejected(suggestion);
                    
                default:
                    TsugixMetrics.Instance.RecordFixResult(errorContext.Language, applied: false);
                    return FixResult.Rejected(suggestion);
            }
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(LogEvents.LlmRequestFailed, ex, "LLM request timed out");
            _userInteraction.ShowAiError($"Request timed out: {ex.Message}");
            return FixResult.AiError(ex.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(LogEvents.LlmRequestFailed, "LLM request cancelled");
            return FixResult.Skipped();
        }
        catch (Exception ex)
        {
            _logger.LogError(LogEvents.LlmRequestFailed, ex, "LLM request failed: {Message}", ex.Message);
            _userInteraction.ShowAiError(ex.Message);
            return FixResult.AiError(ex.Message);
        }
    }
    
    private FixResult ApplyFix(FixSuggestion suggestion, string language = "Unknown")
    {
        var patchOptions = new PatchOptions
        {
            CreateBackup = _config.AutoBackup,
            VerifyContent = true
        };
        
        var patchResult = _filePatcher.Apply(suggestion, patchOptions);
        
        if (patchResult.Success)
        {
            _logger.LogInformation(LogEvents.FixApplied, 
                "Fix applied to {FilePath}, backup at {BackupPath}", 
                suggestion.FilePath, patchResult.BackupPath);
            TsugixMetrics.Instance.RecordFixResult(language, applied: true);
            _userInteraction.ShowFixApplied(patchResult.BackupPath);
            return FixResult.Applied(suggestion, patchResult.BackupPath);
        }
        else
        {
            _logger.LogError(LogEvents.LlmRequestFailed, 
                "Failed to apply fix: {Error}", patchResult.ErrorMessage);
            TsugixMetrics.Instance.RecordFixResult(language, applied: false);
            _userInteraction.ShowFixFailed(patchResult.ErrorMessage ?? "Unknown error");
            return FixResult.Failed(patchResult.ErrorMessage ?? "Unknown error", suggestion);
        }
    }
}
