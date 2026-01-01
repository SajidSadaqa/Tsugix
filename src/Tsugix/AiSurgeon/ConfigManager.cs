using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tsugix.AiSurgeon;

/// <summary>
/// Manages Tsugix configuration loading from files and environment variables.
/// </summary>
public class ConfigManager
{
    private const string ConfigFileName = ".tsugix.json";
    private const string OpenAiApiKeyEnvVar = "OPENAI_API_KEY";
    private const string AnthropicApiKeyEnvVar = "ANTHROPIC_API_KEY";
    private const string ConfigPathEnvVar = "TSUGIX_CONFIG";
    
    private TsugixConfig? _cachedConfig;
    
    /// <summary>
    /// Loads configuration from file or returns defaults.
    /// </summary>
    public TsugixConfig Load(string? workingDirectory = null)
    {
        if (_cachedConfig != null)
            return _cachedConfig;
        
        var configPath = FindConfigFile(workingDirectory);
        
        if (configPath != null && File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                _cachedConfig = MergeWithDefaults(json);
            }
            catch
            {
                _cachedConfig = TsugixConfig.Default;
            }
        }
        else
        {
            _cachedConfig = TsugixConfig.Default;
        }
        
        return _cachedConfig;
    }
    
    /// <summary>
    /// Merges partial JSON config with defaults.
    /// This ensures missing properties use default values instead of type defaults.
    /// </summary>
    private static TsugixConfig MergeWithDefaults(string json)
    {
        var defaults = TsugixConfig.Default;
        var node = JsonNode.Parse(json);
        
        if (node is not JsonObject obj)
            return defaults;
        
        return new TsugixConfig
        {
            Provider = obj.TryGetPropertyValue("provider", out var provider) && provider != null
                ? Enum.TryParse<LlmProvider>(provider.GetValue<string>(), ignoreCase: true, out var p) ? p : defaults.Provider
                : defaults.Provider,
            ModelName = obj.TryGetPropertyValue("model", out var model) && model != null
                ? model.GetValue<string>()
                : defaults.ModelName,
            MaxTokens = obj.TryGetPropertyValue("maxTokens", out var maxTokens) && maxTokens != null
                ? maxTokens.GetValue<int>()
                : defaults.MaxTokens,
            AutoBackup = obj.TryGetPropertyValue("autoBackup", out var autoBackup) && autoBackup != null
                ? autoBackup.GetValue<bool>()
                : defaults.AutoBackup,
            AutoApply = obj.TryGetPropertyValue("autoApply", out var autoApply) && autoApply != null
                ? autoApply.GetValue<bool>()
                : defaults.AutoApply,
            AutoRerun = obj.TryGetPropertyValue("autoRerun", out var autoRerun) && autoRerun != null
                ? autoRerun.GetValue<bool>()
                : defaults.AutoRerun,
            TimeoutSeconds = obj.TryGetPropertyValue("timeout", out var timeout) && timeout != null
                ? timeout.GetValue<int>()
                : defaults.TimeoutSeconds,
            RetryCount = obj.TryGetPropertyValue("retryCount", out var retryCount) && retryCount != null
                ? retryCount.GetValue<int>()
                : defaults.RetryCount,
            CustomPromptTemplate = obj.TryGetPropertyValue("customPromptTemplate", out var customPrompt) && customPrompt != null
                ? customPrompt.GetValue<string>()
                : defaults.CustomPromptTemplate,
            Temperature = obj.TryGetPropertyValue("temperature", out var temp) && temp != null
                ? temp.GetValue<double>()
                : defaults.Temperature
        };
    }
    
    /// <summary>
    /// Gets the API key for the specified provider.
    /// </summary>
    public string? GetApiKey(LlmProvider provider)
    {
        return provider switch
        {
            LlmProvider.OpenAI => Environment.GetEnvironmentVariable(OpenAiApiKeyEnvVar),
            LlmProvider.Anthropic => Environment.GetEnvironmentVariable(AnthropicApiKeyEnvVar),
            _ => null
        };
    }
    
    /// <summary>
    /// Checks if an API key is configured for the specified provider.
    /// </summary>
    public bool HasApiKey(LlmProvider provider)
    {
        return !string.IsNullOrEmpty(GetApiKey(provider));
    }
    
    /// <summary>
    /// Gets the environment variable name for the specified provider.
    /// </summary>
    public static string GetApiKeyEnvVarName(LlmProvider provider)
    {
        return provider switch
        {
            LlmProvider.OpenAI => OpenAiApiKeyEnvVar,
            LlmProvider.Anthropic => AnthropicApiKeyEnvVar,
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }
    
    /// <summary>
    /// Creates a default configuration file at the specified path.
    /// </summary>
    public static void CreateDefaultConfig(string path)
    {
        var json = JsonSerializer.Serialize(TsugixConfig.Default, TsugixJsonContext.Default.TsugixConfig);
        File.WriteAllText(path, json);
    }
    
    /// <summary>
    /// Clears the cached configuration.
    /// </summary>
    public void ClearCache()
    {
        _cachedConfig = null;
    }
    
    private static string? FindConfigFile(string? workingDirectory)
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable(ConfigPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;
        
        // Check working directory
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            var workingDirConfig = Path.Combine(workingDirectory, ConfigFileName);
            if (File.Exists(workingDirConfig))
                return workingDirConfig;
        }
        
        // Check current directory
        var currentDirConfig = Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        if (File.Exists(currentDirConfig))
            return currentDirConfig;
        
        // Check user home directory
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeDirConfig = Path.Combine(homeDir, ConfigFileName);
        if (File.Exists(homeDirConfig))
            return homeDirConfig;
        
        return null;
    }
}
