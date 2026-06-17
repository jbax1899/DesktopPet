using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopPet.App.Conversation;

public partial class ConversationOverlayWindow : Window
{
    private const int WindowNoClientHitTestMessage = 0x0084; // WM_NCHITTEST
    private const int TransparentHitTest = -1; // HTTRANSPARENT
    private const int ClientHitTest = 1; // HTCLIENT
    private const double InputMinimumWidth = 260;
    private const double InputMaximumWidthFallback = 1120;
    private const double TranscriptMinimumWidth = 220;
    private const double MaximumWidthScreenRatio = 0.75;
    private const double InputHorizontalPadding = 96;
    private const double TranscriptHorizontalPadding = 56;
    private const double HorizontalScreenMargin = 96;
    private const double BottomMargin = 88;
    private const double TranscriptGap = 12;
    private const double TranscriptMinimumTop = 24;
    private const double ErrorGap = 10;
    private const int TranscriptCharactersPerSecond = 42;
    private static readonly TimeSpan TranscriptFrameInterval = TimeSpan.FromMilliseconds(33);

    private readonly Func<Rect> _petBoundsProvider;
    private readonly DispatcherTimer _transcriptTimer;
    private HwndSource? _source;
    private int _pendingCount;
    private Rect _lastPetBounds;
    private string _fullTranscript = string.Empty;
    private int _visibleTranscriptCharacters;
    private double _transcriptCharacterBudget;
    private bool _isShowingSubmittedMessage;
    private bool _isSubmitting;

    public ConversationOverlayWindow(Func<Rect> petBoundsProvider)
    {
        _petBoundsProvider = petBoundsProvider;
        InitializeComponent();
        _lastPetBounds = petBoundsProvider();

        _transcriptTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TranscriptFrameInterval
        };
        _transcriptTimer.Tick += OnTranscriptTimerTick;
        InputTextBox.LostFocus += OnInputLostFocus;
    }

    public event EventHandler<string>? MessageSubmitted;

    public void ShowAndFocusInput()
    {
        _lastPetBounds = _petBoundsProvider();
        MoveToRelevantScreen(_lastPetBounds);
        InputShell.Visibility = Visibility.Visible;
        ArrangeOverlay();

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        InputTextBox.Focus();
        Keyboard.Focus(InputTextBox);
        InputTextBox.CaretIndex = InputTextBox.Text.Length;
    }

    public void ToggleInput()
    {
        if (IsInputVisible)
        {
            HideInput();
            return;
        }

        ShowAndFocusInput();
    }

    public void HideInput()
    {
        InputShell.Visibility = Visibility.Collapsed;
        ErrorShell.Visibility = Visibility.Collapsed;
        ArrangeOverlay();
    }

    public bool IsInputVisible => IsVisible && InputShell.Visibility == Visibility.Visible;

    public void ShowTranscript(string text)
    {
        _lastPetBounds = _petBoundsProvider();
        MoveToRelevantScreen(_lastPetBounds);
        if (!IsVisible)
        {
            Show();
        }

        ClearSubmittedMessage();
        HideInput();
        _fullTranscript = text;
        _visibleTranscriptCharacters = 0;
        _transcriptCharacterBudget = 0;
        TranscriptTextBlock.Text = string.Empty;
        TranscriptShell.Visibility = Visibility.Visible;
        ErrorShell.Visibility = Visibility.Collapsed;
        ArrangeOverlay();
        _transcriptTimer.Start();
    }

    public void HideTranscript()
    {
        _transcriptTimer.Stop();
        TranscriptShell.Visibility = Visibility.Collapsed;
    }

    public void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorShell.Visibility = Visibility.Visible;
        ArrangeOverlay();
    }

    public void SetRequestPending(bool isPending)
    {
        _pendingCount += isPending ? 1 : -1;
        _pendingCount = Math.Max(0, _pendingCount);
        PendingSpinner.Visibility = _pendingCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(OnWindowMessage);
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WindowNoClientHitTestMessage)
        {
            return nint.Zero;
        }

        handled = true;
        return IsScreenPointOverInput(GetScreenPoint(lParam))
            ? ClientHitTest
            : TransparentHitTest;
    }

    private void OnInputPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideInput();
            return;
        }

        if (_isShowingSubmittedMessage && ShouldClearSubmittedMessageForKey(e.Key))
        {
            ClearSubmittedMessage();
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (_isShowingSubmittedMessage)
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        SubmitCurrentText();
    }

    private void OnInputPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_isShowingSubmittedMessage)
        {
            ClearSubmittedMessage();
        }
    }

    private void OnInputTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ResizeInputForText();
        ArrangeOverlay();
    }

    private void OnInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isSubmitting)
        {
            return;
        }

        ClearSubmittedMessage();
        HideInput();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ArrangeOverlay();
    }

    private void OnTranscriptTimerTick(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_fullTranscript))
        {
            _transcriptTimer.Stop();
            return;
        }

        _transcriptCharacterBudget += TranscriptCharactersPerSecond * TranscriptFrameInterval.TotalSeconds;
        var nextVisibleCount = Math.Min(_fullTranscript.Length, (int)Math.Floor(_transcriptCharacterBudget));
        if (nextVisibleCount <= _visibleTranscriptCharacters)
        {
            return;
        }

        _visibleTranscriptCharacters = nextVisibleCount;
        TranscriptTextBlock.Text = _fullTranscript[.._visibleTranscriptCharacters];
        ArrangeOverlay();

        if (_visibleTranscriptCharacters >= _fullTranscript.Length)
        {
            _transcriptTimer.Stop();
        }
    }

    private void SubmitCurrentText()
    {
        var message = InputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        InputTextBox.Clear();
        ShowSubmittedMessage(message);
        ErrorShell.Visibility = Visibility.Collapsed;
        MessageSubmitted?.Invoke(this, message);
        _isSubmitting = true;
        Keyboard.ClearFocus();
        _isSubmitting = false;
    }

    private void ResizeInputForText()
    {
        var availableMaximumWidth = GetAvailableInputWidth();
        var textWidth = MeasureLongestLineWidth(
            InputTextBox.Text,
            InputTextBox.FontFamily,
            InputTextBox.FontStyle,
            InputTextBox.FontWeight,
            InputTextBox.FontStretch,
            InputTextBox.FontSize,
            InputTextBox.Foreground);

        InputShell.Width = Clamp(textWidth + InputHorizontalPadding, InputMinimumWidth, availableMaximumWidth);
    }

    private void ArrangeOverlay()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var inputMaximumWidth = GetAvailableInputWidth();
        InputShell.MaxWidth = inputMaximumWidth;
        InputShell.Measure(new System.Windows.Size(inputMaximumWidth, InputShell.MaxHeight));
        var inputWidth = InputShell.Width > 0 ? InputShell.Width : InputMinimumWidth;
        var inputHeight = Math.Min(InputShell.DesiredSize.Height, InputShell.MaxHeight);
        System.Windows.Controls.Canvas.SetLeft(InputShell, (ActualWidth - inputWidth) / 2);
        System.Windows.Controls.Canvas.SetTop(InputShell, Math.Max(0, ActualHeight - inputHeight - BottomMargin));

        if (ErrorShell.Visibility == Visibility.Visible)
        {
            ErrorShell.Measure(new System.Windows.Size(520, double.PositiveInfinity));
            System.Windows.Controls.Canvas.SetLeft(ErrorShell, (ActualWidth - ErrorShell.DesiredSize.Width) / 2);
            System.Windows.Controls.Canvas.SetTop(ErrorShell, Math.Max(0, System.Windows.Controls.Canvas.GetTop(InputShell) - ErrorShell.DesiredSize.Height - ErrorGap));
        }

        if (TranscriptShell.Visibility != Visibility.Visible)
        {
            return;
        }

        var transcriptMaximumWidth = GetAvailableInputWidth();
        TranscriptShell.MaxWidth = transcriptMaximumWidth;
        TranscriptShell.Width = GetTranscriptWidth(transcriptMaximumWidth);
        TranscriptShell.Measure(new System.Windows.Size(TranscriptShell.Width, TranscriptShell.MaxHeight));
        var inputTop = InputShell.Visibility == Visibility.Visible
            ? System.Windows.Controls.Canvas.GetTop(InputShell)
            : Math.Max(0, ActualHeight - InputShell.DesiredSize.Height - BottomMargin);

        var transcriptLeft = Clamp(
            (ActualWidth - TranscriptShell.Width) / 2,
            16,
            Math.Max(16, ActualWidth - TranscriptShell.Width - 16));
        var transcriptTop = Clamp(
            inputTop - TranscriptShell.DesiredSize.Height - TranscriptGap,
            TranscriptMinimumTop,
            Math.Max(TranscriptMinimumTop, inputTop - TranscriptShell.DesiredSize.Height));

        System.Windows.Controls.Canvas.SetLeft(TranscriptShell, transcriptLeft);
        System.Windows.Controls.Canvas.SetTop(TranscriptShell, transcriptTop);
    }

    private void MoveToRelevantScreen(Rect petBounds)
    {
        var screen = petBounds.Width > 0 && petBounds.Height > 0
            ? Forms.Screen.FromPoint(new System.Drawing.Point(
                (int)Math.Round(petBounds.Left + petBounds.Width / 2),
                (int)Math.Round(petBounds.Top + petBounds.Height / 2)))
            : Forms.Screen.FromPoint(Forms.Control.MousePosition);

        var bounds = screen.Bounds;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private bool IsScreenPointOverInput(System.Windows.Point screenPoint)
    {
        if (InputShell.Visibility != Visibility.Visible)
        {
            return false;
        }

        var topLeft = InputShell.PointToScreen(new System.Windows.Point(0, 0));
        var bottomRight = InputShell.PointToScreen(new System.Windows.Point(InputShell.ActualWidth, InputShell.ActualHeight));
        return new Rect(topLeft, bottomRight).Contains(screenPoint);
    }

    private static System.Windows.Point GetScreenPoint(nint lParam)
    {
        var value = lParam.ToInt64();
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        return new System.Windows.Point(x, y);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }

    private double GetAvailableInputWidth()
    {
        if (ActualWidth <= 0)
        {
            return InputMaximumWidthFallback;
        }

        var ratioWidth = ActualWidth * MaximumWidthScreenRatio;
        var marginWidth = ActualWidth - HorizontalScreenMargin * 2;
        return Math.Max(InputMinimumWidth, Math.Min(ratioWidth, marginWidth));
    }

    private double GetTranscriptWidth(double availableMaximumWidth)
    {
        var textWidth = MeasureLongestLineWidth(
            TranscriptTextBlock.Text,
            TranscriptTextBlock.FontFamily,
            TranscriptTextBlock.FontStyle,
            TranscriptTextBlock.FontWeight,
            TranscriptTextBlock.FontStretch,
            TranscriptTextBlock.FontSize,
            TranscriptTextBlock.Foreground);

        return Clamp(textWidth + TranscriptHorizontalPadding, TranscriptMinimumWidth, availableMaximumWidth);
    }

    private double MeasureLongestLineWidth(
        string text,
        System.Windows.Media.FontFamily fontFamily,
        System.Windows.FontStyle fontStyle,
        FontWeight fontWeight,
        FontStretch fontStretch,
        double fontSize,
        System.Windows.Media.Brush foreground)
    {
        var longestLine = (string.IsNullOrEmpty(text) ? " " : text)
            .ReplaceLineEndings("\n")
            .Split('\n')
            .DefaultIfEmpty(" ")
            .MaxBy(line => line.Length) ?? " ";

        var formattedText = new FormattedText(
            longestLine,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(fontFamily, fontStyle, fontWeight, fontStretch),
            fontSize,
            foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private void ShowSubmittedMessage(string message)
    {
        _isShowingSubmittedMessage = true;
        InputTextBox.Text = message;
        InputTextBox.FontStyle = FontStyles.Italic;
        InputTextBox.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xDD, 0x37, 0x41, 0x51));
        InputTextBox.CaretIndex = InputTextBox.Text.Length;
    }

    private void ClearSubmittedMessage()
    {
        if (!_isShowingSubmittedMessage)
        {
            return;
        }

        _isShowingSubmittedMessage = false;
        InputTextBox.Clear();
        InputTextBox.FontStyle = FontStyles.Normal;
        InputTextBox.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x0F, 0x17, 0x2A));
    }

    private static bool ShouldClearSubmittedMessageForKey(Key key)
    {
        return key is Key.Back
            or Key.Delete
            or Key.Space
            or >= Key.A and <= Key.Z
            or >= Key.D0 and <= Key.D9
            or >= Key.NumPad0 and <= Key.NumPad9
            or >= Key.Oem1 and <= Key.Oem102;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_source is not null)
        {
            _source.RemoveHook(OnWindowMessage);
            _source = null;
        }

        _transcriptTimer.Stop();
        _transcriptTimer.Tick -= OnTranscriptTimerTick;

        base.OnClosed(e);
    }
}
