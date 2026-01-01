using Tsugix.AiSurgeon;
using Xunit;

namespace Tsugix.Tests.AiSurgeon;

/// <summary>
/// Tests for ConfigManager.
/// Validates: Requirements 1.5, 1.6, 9.7
/// </summary>
public class ConfigManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly ConfigManager _configManager;
    private readonly Dictionary<string, string?> _originalEnvVars = new();
    
    public ConfigManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tsugix_config_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _configManager = new ConfigManager();
        
        // Save original env vars
        _originalEnvVars["OPENAI_API_KEY"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _originalEnvVars["ANTHROPIC_API_KEY"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _originalEnvVars["TSUGIX_CONFIG"] = Environment.GetEnvironmentVariable("TSUGIX_CONFIG");
    }
    
    public void Dispose()
    {
        // Restore original env vars
        foreach (var (key, value) in _originalEnvVars)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }
    
    [Fact]
    public void Load_NoConfigFile_ReturnsDefaults()
    {
        _configManager.ClearCache();
        var config = _configManager.Load(_testDir);
        
        Assert.Equal(LlmProvider.OpenAI, config.Provider);
        Assert.Equal("gpt-4o", config.ModelName);
        Assert.Equal(8000, config.MaxTokens);
        Assert.True(config.AutoBackup);
        Assert.False(config.AutoApply);
        Assert.False(config.AutoRerun);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.Equal(1, config.RetryCount);
        Assert.Null(config.CustomPromptTemplate);
    }
    
    [Fact]
    public void Load_ValidConfigFile_LoadsSettings()
    {
        var configPath = Path.Combine(_testDir, ".tsugix.json");
        File.WriteAllText(configPath, @"{
            ""provider"": ""anthropic"",
            ""model"": ""claude-3-5-sonnet"",
            ""maxTokens"": 4000,
            ""autoBackup"": false,
            ""autoApply"": true,
            ""timeout"": 60
        }");
        
        _configManager.ClearCache();
        var config = _configManager.Load(_testDir);
        
        Assert.Equal(LlmProvider.Anthropic, config.Provider);
        Assert.Equal("claude-3-5-sonnet", config.ModelName);
        Assert.Equal(4000, config.MaxTokens);
        Assert.False(config.AutoBackup);
        Assert.True(config.AutoApply);
        Assert.Equal(60, config.TimeoutSeconds);
    }
    
    [Fact]
    public void Load_InvalidJson_ReturnsDefaults()
    {
        var configPath = Path.Combine(_testDir, ".tsugix.json");
        File.WriteAllText(configPath, "{ invalid json }");
        
        _configManager.ClearCache();
        var config = _configManager.Load(_testDir);
        
        Assert.Equal(TsugixConfig.Default.Provider, config.Provider);
        Assert.Equal(TsugixConfig.Default.ModelName, config.ModelName);
    }
    
    [Fact]
    public void Load_PartialConfig_MergesWithDefaults()
    {
        var configPath = Path.Combine(_testDir, ".tsugix.json");
        File.WriteAllText(configPath, @"{ ""model"": ""gpt-4-turbo"" }");
        
        _configManager.ClearCache();
        var config = _configManager.Load(_testDir);
        
        Assert.Equal("gpt-4-turbo", config.ModelName);
        Assert.Equal(LlmProvider.OpenAI, config.Provider); // Default
        Assert.Equal(8000, config.MaxTokens); // Default
    }
    
    [Fact]
    public void Load_EnvVarConfigPath_LoadsFromEnvPath()
    {
        var configPath = Path.Combine(_testDir, "custom-config.json");
        File.WriteAllText(configPath, @"{ ""model"": ""custom-model"" }");
        
        Environment.SetEnvironmentVariable("TSUGIX_CONFIG", configPath);
        
        _configManager.ClearCache();
        var config = _configManager.Load();
        
        Assert.Equal("custom-model", config.ModelName);
    }
    
    [Fact]
    public void GetApiKey_OpenAI_ReturnsEnvVar()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-openai-key");
        
        var apiKey = _configManager.GetApiKey(LlmProvider.OpenAI);
        
        Assert.Equal("test-openai-key", apiKey);
    }
    
    [Fact]
    public void GetApiKey_Anthropic_ReturnsEnvVar()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-anthropic-key");
        
        var apiKey = _configManager.GetApiKey(LlmProvider.Anthropic);
        
        Assert.Equal("test-anthropic-key", apiKey);
    }
    
    [Fact]
    public void GetApiKey_NotSet_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        
        var apiKey = _configManager.GetApiKey(LlmProvider.OpenAI);
        
        Assert.Null(apiKey);
    }
    
    [Fact]
    public void HasApiKey_WhenSet_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        
        Assert.True(_configManager.HasApiKey(LlmProvider.OpenAI));
    }
    
    [Fact]
    public void HasApiKey_WhenNotSet_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        
        Assert.False(_configManager.HasApiKey(LlmProvider.OpenAI));
    }
    
    [Fact]
    public void GetApiKeyEnvVarName_ReturnsCorrectNames()
    {
        Assert.Equal("OPENAI_API_KEY", ConfigManager.GetApiKeyEnvVarName(LlmProvider.OpenAI));
        Assert.Equal("ANTHROPIC_API_KEY", ConfigManager.GetApiKeyEnvVarName(LlmProvider.Anthropic));
    }
    
    [Fact]
    public void CreateDefaultConfig_CreatesValidFile()
    {
        var configPath = Path.Combine(_testDir, "new-config.json");
        
        ConfigManager.CreateDefaultConfig(configPath);
        
        Assert.True(File.Exists(configPath));
        
        var content = File.ReadAllText(configPath);
        Assert.Contains("openAI", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gpt-4o", content);
    }
    
    [Fact]
    public void Load_CachesConfig()
    {
        var configPath = Path.Combine(_testDir, ".tsugix.json");
        File.WriteAllText(configPath, @"{ ""model"": ""first-model"" }");
        
        _configManager.ClearCache();
        var config1 = _configManager.Load(_testDir);
        
        // Modify file
        File.WriteAllText(configPath, @"{ ""model"": ""second-model"" }");
        
        // Should return cached value
        var config2 = _configManager.Load(_testDir);
        
        Assert.Equal("first-model", config1.ModelName);
        Assert.Equal("first-model", config2.ModelName);
    }
    
    [Fact]
    public void ClearCache_AllowsReload()
    {
        var configPath = Path.Combine(_testDir, ".tsugix.json");
        File.WriteAllText(configPath, @"{ ""model"": ""first-model"" }");
        
        _configManager.ClearCache();
        var config1 = _configManager.Load(_testDir);
        
        // Modify file and clear cache
        File.WriteAllText(configPath, @"{ ""model"": ""second-model"" }");
        _configManager.ClearCache();
        
        var config2 = _configManager.Load(_testDir);
        
        Assert.Equal("first-model", config1.ModelName);
        Assert.Equal("second-model", config2.ModelName);
    }
}
