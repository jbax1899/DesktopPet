using System.Windows;
using DesktopPet.App.Cloud;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow
{
    private void OnManagePronunciationsClicked(object sender, RoutedEventArgs e)
    {
        var apiKey = ToNullIfWhiteSpace(ElevenLabsApiKeyPasswordBox.Password);
        if (apiKey is null)
        {
            StatusTextBlock.Text = "Enter an ElevenLabs API key first.";
            return;
        }

        var window = new PronunciationWindow(
            _elevenLabsSettingsStore,
            _pronunciationService,
            apiKey)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private async Task LoadVisionModelsAsync()
    {
        var openRouterSettings = _openRouterSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(openRouterSettings.ApiKey))
        {
            OpenRouterModelCapabilitiesText.Text = "Enter an API key to load available vision models.";
            return;
        }

        OpenRouterModelCapabilitiesText.Text = "Loading vision models...";

        try
        {
            var models = await _openRouterModelsService.GetVisionModelsAsync(CancellationToken.None);
            _visionModels.Clear();
            foreach (var model in models)
            {
                _visionModels.Add(model);
            }

            if (_visionModels.Count == 0)
            {
                OpenRouterModelCapabilitiesText.Text = "No vision-capable models found. Check your API key.";
                return;
            }

            var selectedModel = _visionModels.FirstOrDefault(m =>
                string.Equals(m.Id, openRouterSettings.VisionModelId, StringComparison.OrdinalIgnoreCase));

            if (selectedModel is not null)
            {
                OpenRouterVisionModelComboBox.SelectedItem = selectedModel;
                UpdateModelCapabilities(selectedModel);
            }
            else if (_visionModels.Count > 0)
            {
                OpenRouterVisionModelComboBox.SelectedIndex = 0;
                UpdateModelCapabilities(_visionModels[0]);
            }
        }
        catch (Exception)
        {
            OpenRouterModelCapabilitiesText.Text = "Failed to load vision models. Check your API key and connection.";
        }
    }

    private async Task LoadAudioModelsAsync()
    {
        var settings = _openRouterSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return;
        }

        var models = await _openRouterModelsService.GetAudioModelsAsync(CancellationToken.None);
        _audioModels.Clear();
        foreach (var model in models)
        {
            _audioModels.Add(model);
        }

        var selected = _audioModels.FirstOrDefault(model =>
            string.Equals(model.Id, settings.AudioAnalysisModelId, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            OpenRouterAudioModelComboBox.SelectedItem = selected;
        }
        else if (_audioModels.Count > 0)
        {
            OpenRouterAudioModelComboBox.SelectedIndex = 0;
        }
    }

    private void UpdateModelCapabilities(OpenRouterModelInfo model)
    {
        var capabilities = new List<string>
        {
            "Image input"
        };
        if (model.SupportsStructuredOutput)
        {
            capabilities.Add("Structured output");
        }

        OpenRouterModelCapabilitiesText.Text = $"Capabilities: {string.Join(", ", capabilities)}";
    }

    private async Task LoadCreditsAsync()
    {
        var elevenLabsTask = _creditInfoService.GetElevenLabsCreditsAsync(CancellationToken.None);
        var openRouterTask = _creditInfoService.GetOpenRouterCreditsAsync(CancellationToken.None);

        await Task.WhenAll(elevenLabsTask, openRouterTask);

        var elevenLabs = elevenLabsTask.Result;
        if (elevenLabs is not null)
        {
            ElevenLabsCreditText.Text = $"{elevenLabs.Tier} — {elevenLabs.CharacterCount:N0} / {elevenLabs.CharacterLimit:N0} credits";
        }

        var openRouter = openRouterTask.Result;
        if (openRouter is not null)
        {
            if (openRouter.LimitRemaining.HasValue)
            {
                OpenRouterCreditText.Text = $"${openRouter.LimitRemaining:F2} remaining";
            }
            else
            {
                OpenRouterCreditText.Text = $"${openRouter.Usage:F2} used";
            }
        }
    }
}
