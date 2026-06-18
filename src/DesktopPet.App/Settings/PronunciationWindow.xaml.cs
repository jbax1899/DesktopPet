using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DesktopPet.App.Cloud;

namespace DesktopPet.App.Settings;

public partial class PronunciationWindow : Window
{
    private const int MaximumPronunciations = 50;

    private readonly ElevenLabsSettingsStore _settingsStore;
    private readonly ElevenLabsPronunciationService _pronunciationService;
    private readonly string _apiKey;

    public PronunciationWindow(
        ElevenLabsSettingsStore settingsStore,
        ElevenLabsPronunciationService pronunciationService,
        string apiKey)
    {
        _settingsStore = settingsStore;
        _pronunciationService = pronunciationService;
        _apiKey = apiKey;

        var settings = settingsStore.Load();
        Pronunciations = new ObservableCollection<PronunciationRow>(
            settings.CustomPronunciations?
                .Select(pronunciation => new PronunciationRow
                {
                    Text = pronunciation.Text,
                    Alias = pronunciation.Alias
                })
            ?? []);

        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<PronunciationRow> Pronunciations { get; }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var text = TextTextBox.Text.Trim();
        var alias = AliasTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(alias))
        {
            StatusTextBlock.Text = "Enter both the word and how it should sound.";
            return;
        }

        if (Pronunciations.Count >= MaximumPronunciations)
        {
            StatusTextBlock.Text = $"Keep the prototype list to {MaximumPronunciations} pronunciations.";
            return;
        }

        if (Pronunciations.Any(row =>
                string.Equals(row.Text.Trim(), text, StringComparison.OrdinalIgnoreCase)))
        {
            StatusTextBlock.Text = "That word or phrase is already in the list.";
            return;
        }

        Pronunciations.Add(new PronunciationRow
        {
            Text = text,
            Alias = alias
        });
        TextTextBox.Clear();
        AliasTextBox.Clear();
        TextTextBox.Focus();
        StatusTextBlock.Text = string.Empty;
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: PronunciationRow row })
        {
            Pronunciations.Remove(row);
        }
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        PronunciationsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        PronunciationsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var pronunciations = Pronunciations
            .Select(row => new CustomPronunciation(row.Text.Trim(), row.Alias.Trim()))
            .ToArray();
        if (pronunciations.Any(pronunciation =>
                string.IsNullOrWhiteSpace(pronunciation.Text)
                || string.IsNullOrWhiteSpace(pronunciation.Alias)))
        {
            StatusTextBlock.Text = "Every pronunciation needs both fields.";
            return;
        }

        if (pronunciations
            .GroupBy(pronunciation => pronunciation.Text, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            StatusTextBlock.Text = "Each word or phrase can appear only once.";
            return;
        }

        SaveButton.IsEnabled = false;
        StatusTextBlock.Text = "Saving pronunciations…";
        try
        {
            var settings = _settingsStore.Load();
            var locators = await _pronunciationService.SyncAsync(
                _apiKey,
                pronunciations,
                settings.PronunciationDictionaries,
                CancellationToken.None);
            _settingsStore.Save(settings with
            {
                PronunciationDictionaries = locators,
                CustomPronunciations = pronunciations
            });
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public sealed class PronunciationRow
{
    public string Text { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;
}
