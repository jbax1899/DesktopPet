using DesktopPet.App.Cloud;
using DesktopPet.App.Security;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class CredentialStoreTests
{
    private string _directory = null!;
    private string _credentialPath = null!;
    private CredentialStore _credentialStore = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DesktopPet.Tests", Guid.NewGuid().ToString("N"));
        _credentialPath = Path.Combine(_directory, "credentials.dat");
        _credentialStore = new CredentialStore(_credentialPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void SavesAndLoadsBothProviderKeys()
    {
        _credentialStore.SaveElevenLabsApiKey(" eleven-secret ");
        _credentialStore.SaveOpenRouterApiKey("openrouter-secret");

        Assert.AreEqual("eleven-secret", _credentialStore.GetElevenLabsApiKey());
        Assert.AreEqual("openrouter-secret", _credentialStore.GetOpenRouterApiKey());

        var protectedBytes = File.ReadAllBytes(_credentialPath);
        var protectedText = Convert.ToBase64String(protectedBytes);
        Assert.IsFalse(protectedText.Contains("eleven-secret", StringComparison.Ordinal));
        Assert.IsFalse(protectedText.Contains("openrouter-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SavingOneProviderPreservesTheOther()
    {
        _credentialStore.SaveElevenLabsApiKey("eleven-secret");
        _credentialStore.SaveOpenRouterApiKey("openrouter-secret");
        _credentialStore.SaveElevenLabsApiKey("replacement");

        Assert.AreEqual("replacement", _credentialStore.GetElevenLabsApiKey());
        Assert.AreEqual("openrouter-secret", _credentialStore.GetOpenRouterApiKey());
    }

    [TestMethod]
    public void MissingOrCorruptCredentialFileReturnsEmptyKeys()
    {
        Assert.IsNull(_credentialStore.GetElevenLabsApiKey());
        Assert.IsNull(_credentialStore.GetOpenRouterApiKey());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(_credentialPath, "not protected data");

        Assert.IsNull(_credentialStore.GetElevenLabsApiKey());
        Assert.IsNull(_credentialStore.GetOpenRouterApiKey());
    }

    [TestMethod]
    public void ProviderSettingsJsonDoesNotContainApiKeys()
    {
        var elevenPath = Path.Combine(_directory, "cloud-ai-settings.json");
        var openRouterPath = Path.Combine(_directory, "openrouter-settings.json");
        var elevenStore = new ElevenLabsSettingsStore(elevenPath, _credentialStore);
        var openRouterStore = new OpenRouterSettingsStore(openRouterPath, _credentialStore);

        elevenStore.Save(new ElevenLabsSettings(
            "eleven-secret",
            "agent-id",
            "voice-id",
            []));
        openRouterStore.Save(new OpenRouterSettings(
            "openrouter-secret",
            "model-id"));

        Assert.IsFalse(File.ReadAllText(elevenPath).Contains("eleven-secret", StringComparison.Ordinal));
        Assert.IsFalse(File.ReadAllText(openRouterPath).Contains("openrouter-secret", StringComparison.Ordinal));
        Assert.AreEqual("eleven-secret", elevenStore.Load().ElevenLabsApiKey);
        Assert.AreEqual("openrouter-secret", openRouterStore.Load().ApiKey);
    }
}
