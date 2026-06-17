using DesktopPet.App.Settings;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopPet.App.Overlay;

public partial class PetOverlayWindow : Window, ICharacterStateController
{
    // WPF has no click-through property, so this uses the Win32 window flags.
    private const int ExtendedWindowStyleIndex = -20; // GWL_EXSTYLE
    private const int TransparentWindowStyle = 0x00000020; // WS_EX_TRANSPARENT
    private const double EdgeMargin = 32;
    private const double MaximumEyeOffsetX = 28;
    private const double MaximumEyeOffsetY = 22;
    private const double FullGazeDistance = 220;
    private const double GazeSmoothing = 0.28;
    private const double AssumedMonitorWidthCentimeters = 60;
    private const double ViewerDistanceFromScreenCentimeters = 90;
    private const double IdleBreathPeriodSeconds = 4.0;
    private const double IdleBobPixels = 2.0;
    private const double IdleSquashAmount = 0.018;
    private const double ActionPadMaximumCursorDistance = 320;
    private const double MouthOpenThreshold = 0.28;
    private static readonly TimeSpan IdleMoodDelay = TimeSpan.FromSeconds(45);
    private const double MouthCloseThreshold = 0.16;
    private const int LeftMouseButtonVirtualKey = 0x01; // VK_LBUTTON
    private const int RightMouseButtonVirtualKey = 0x02; // VK_RBUTTON
    private static readonly TimeSpan GazeUpdateInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan ActionPadUpdateInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ActionPadMouseAwayDelay = TimeSpan.FromSeconds(3);
    private const string MouthA = "Mouth A";
    private const string MouthB = "Mouth B";
    private const string LeftEye = "Left Eye";
    private const string RightEye = "Right Eye";
    private static readonly Vector LeftEyeNeutralOffset = new(-14, 12);
    private static readonly Vector RightEyeNeutralOffset = new(-14, 12);

    private readonly WpfInochiPuppetView _puppetView = new();
    private readonly OverlayCommands _commands;
    private readonly Action<Rect> _positionChanged;
    private readonly DispatcherTimer _gazeTimer;
    private readonly DispatcherTimer _actionPadTimer;
    private readonly Dictionary<int, PetMood> _moodScopes = [];
    private readonly Stopwatch _idleClock = Stopwatch.StartNew();
    private bool _isClickThrough;
    private bool _isSpeaking;
    private bool _showMouthB;
    private bool _wasLeftMouseButtonDown;
    private bool _wasRightMouseButtonDown;
    private bool _closedActionPadOnRightMouseDown;
    private PetMood _currentMood = PetMood.Idle;
    private PetMood? _temporaryMood;
    private DateTime _temporaryMoodExpiresAtUtc;
    private DateTime _lastActivityAtUtc = DateTime.UtcNow;
    private DateTime _lastActionPadMouseOverAt;
    private int _nextMoodScopeId;
    private Vector _currentLeftEyeOffset = LeftEyeNeutralOffset;
    private Vector _currentRightEyeOffset = RightEyeNeutralOffset;
    private OverlayPosition? _initialPosition;

    public PetOverlayWindow(OverlayCommands commands, Action<Rect> positionChanged)
    {
        _commands = commands;
        _positionChanged = positionChanged;

        InitializeComponent();

        var puppetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "bug.inp");
        _puppetView.Load(InochiPuppetLoader.Load(puppetPath));
        PuppetHost.Child = _puppetView;
        SetMouthFrame(showMouthB: false);
        ApplyEyeOffset(_currentLeftEyeOffset, _currentRightEyeOffset);

        _gazeTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = GazeUpdateInterval
        };
        _gazeTimer.Tick += OnGazeTimerTick;

        _actionPadTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = ActionPadUpdateInterval
        };
        _actionPadTimer.Tick += OnActionPadTimerTick;

        Loaded += (_, _) => ApplyInitialPosition();
        Loaded += (_, _) => _gazeTimer.Start();
        Closed += (_, _) =>
        {
            _gazeTimer.Stop();
            _gazeTimer.Tick -= OnGazeTimerTick;
            _actionPadTimer.Stop();
            _actionPadTimer.Tick -= OnActionPadTimerTick;
        };
    }

    public ISpeakingScope BeginSpeaking()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(BeginSpeaking);
        }

        var moodScope = BeginMood(PetMood.Speaking);
        _isSpeaking = true;
        _showMouthB = false;
        SetMouthFrame(showMouthB: false);
        UpdateMood();

        return new SpeakingScope(this, moodScope);
    }

    public IDisposable BeginMood(PetMood mood)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(() => BeginMood(mood));
        }

        MarkActivity();
        var scopeId = ++_nextMoodScopeId;
        _moodScopes[scopeId] = mood;
        UpdateMood();

        return new MoodScope(this, scopeId);
    }

    public void ShowTemporaryMood(PetMood mood, TimeSpan duration)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowTemporaryMood(mood, duration));
            return;
        }

        MarkActivity();
        _temporaryMood = mood;
        _temporaryMoodExpiresAtUtc = DateTime.UtcNow + duration;
        UpdateMood();
    }

    public void MarkActivity()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(MarkActivity);
            return;
        }

        _lastActivityAtUtc = DateTime.UtcNow;
        if (_currentMood == PetMood.Sleepy)
        {
            UpdateMood();
        }
    }

    public void SetClickThrough(bool isClickThrough)
    {
        _isClickThrough = isClickThrough;
        if (isClickThrough)
        {
            HideActionPad();
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var currentStyle = GetWindowLongPtr(handle, ExtendedWindowStyleIndex);
        var updatedStyle = isClickThrough
            ? currentStyle | TransparentWindowStyle
            : currentStyle & ~TransparentWindowStyle;

        SetWindowLongPtr(handle, ExtendedWindowStyleIndex, updatedStyle);
    }

    public void ShowNearBottomRight()
    {
        HideActionPad();
        MoveNearBottomRight();

        Show();
        Activate();
    }

    public Rect GetScreenBounds()
    {
        return new Rect(Left, Top, GetWindowWidth(), GetWindowHeight());
    }

    public void SetInitialPosition(OverlayPosition? position)
    {
        _initialPosition = position;

        if (IsLoaded)
        {
            ApplyInitialPosition();
        }
    }

    private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        MarkActivity();

        if (ActionPad.Visibility == Visibility.Visible)
        {
            if (ActionPad.IsMouseOver)
            {
                return;
            }

            HideActionPad();
        }

        DragMove();
        KeepInsideWorkArea();
        _positionChanged(GetScreenBounds());
    }

    private void OnOverlayPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough)
        {
            return;
        }

        MarkActivity();
        _closedActionPadOnRightMouseDown = ActionPad.Visibility == Visibility.Visible;

        if (_closedActionPadOnRightMouseDown)
        {
            HideActionPad();
            e.Handled = true;
        }
    }

    private void OnOverlayMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough)
        {
            return;
        }

        MarkActivity();

        if (_closedActionPadOnRightMouseDown)
        {
            _closedActionPadOnRightMouseDown = false;
            e.Handled = true;
            return;
        }

        ShowActionPad();
        e.Handled = true;
    }

    private void OnChatActionClicked(object sender, RoutedEventArgs e)
    {
        MarkActivity();
        HideActionPad();
        _commands.ShowChat();
    }

    private void OnSettingsActionClicked(object sender, RoutedEventArgs e)
    {
        MarkActivity();
        HideActionPad();
        _commands.ShowSettings();
    }

    private void OnMemoriesActionClicked(object sender, RoutedEventArgs e)
    {
        MarkActivity();
        HideActionPad();
        _commands.ShowMemories();
    }

    private void ToggleActionPad()
    {
        if (ActionPad.Visibility == Visibility.Visible)
        {
            HideActionPad();
            return;
        }

        ShowActionPad();
    }

    private void ShowActionPad()
    {
        _closedActionPadOnRightMouseDown = false;
        ActionPad.Visibility = Visibility.Visible;
        _lastActionPadMouseOverAt = DateTime.UtcNow;
        _wasLeftMouseButtonDown = IsMouseButtonDown(LeftMouseButtonVirtualKey);
        _wasRightMouseButtonDown = IsMouseButtonDown(RightMouseButtonVirtualKey);
        _actionPadTimer.Start();
    }

    private void HideActionPad()
    {
        ActionPad.Visibility = Visibility.Collapsed;
        _actionPadTimer.Stop();
    }

    private void OnActionPadTimerTick(object? sender, EventArgs e)
    {
        if (ActionPad.Visibility != Visibility.Visible)
        {
            _actionPadTimer.Stop();
            return;
        }

        var cursor = Forms.Control.MousePosition;
        var cursorPoint = new System.Windows.Point(cursor.X, cursor.Y);
        var actionPadBounds = GetActionPadScreenBounds();
        var isMouseOverActionPad = actionPadBounds.Contains(cursorPoint);
        var now = DateTime.UtcNow;

        if (isMouseOverActionPad)
        {
            _lastActionPadMouseOverAt = now;
        }

        var leftMouseButtonDown = IsMouseButtonDown(LeftMouseButtonVirtualKey);
        var rightMouseButtonDown = IsMouseButtonDown(RightMouseButtonVirtualKey);
        var leftMouseClicked = leftMouseButtonDown && !_wasLeftMouseButtonDown;
        var rightMouseClicked = rightMouseButtonDown && !_wasRightMouseButtonDown;

        _wasLeftMouseButtonDown = leftMouseButtonDown;
        _wasRightMouseButtonDown = rightMouseButtonDown;

        if (rightMouseClicked
            || leftMouseClicked && !isMouseOverActionPad
            || GetDistanceFromRect(cursorPoint, actionPadBounds) > ActionPadMaximumCursorDistance
            || !isMouseOverActionPad && now - _lastActionPadMouseOverAt > ActionPadMouseAwayDelay)
        {
            HideActionPad();
        }
    }

    private Rect GetActionPadScreenBounds()
    {
        var topLeft = ActionPad.PointToScreen(new System.Windows.Point(0, 0));
        var bottomRight = ActionPad.PointToScreen(new System.Windows.Point(ActionPad.ActualWidth, ActionPad.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    private static double GetDistanceFromRect(System.Windows.Point point, Rect rect)
    {
        var closestX = Clamp(point.X, rect.Left, rect.Right);
        var closestY = Clamp(point.Y, rect.Top, rect.Bottom);
        var distanceX = point.X - closestX;
        var distanceY = point.Y - closestY;

        return Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
    }

    private static bool IsMouseButtonDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private void MoveNearBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - EdgeMargin;
        Top = workArea.Bottom - Height - EdgeMargin;
    }

    private void ApplyInitialPosition()
    {
        if (_initialPosition is null)
        {
            MoveNearBottomRight();
        }
        else
        {
            Left = _initialPosition.Left;
            Top = _initialPosition.Top;
            KeepInsideWorkArea();
        }
    }

    private void KeepInsideWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var windowWidth = GetWindowWidth();
        var windowHeight = GetWindowHeight();

        Left = Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - windowWidth));
        Top = Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowHeight));
    }

    private double GetWindowWidth()
    {
        return ActualWidth > 0 ? ActualWidth : Width;
    }

    private double GetWindowHeight()
    {
        return ActualHeight > 0 ? ActualHeight : Height;
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }

    private void OnGazeTimerTick(object? sender, EventArgs e)
    {
        UpdateMood();
        UpdateMoodIconAnimation();
        UpdateIdlePose();

        var (leftOffset, rightOffset) = _isSpeaking
            ? GetViewerGazeOffsets()
            : GetMouseGazeOffsets();

        SetEyeOffset(leftOffset, rightOffset);
    }

    private void UpdateIdlePose()
    {
        var phase = _idleClock.Elapsed.TotalSeconds / IdleBreathPeriodSeconds * Math.Tau;
        var breath = Math.Sin(phase);
        var liftedBreath = (breath + 1) / 2;
        var speakingLift = _currentMood == PetMood.Speaking ? -1.5 : 0;
        var sleepySink = _currentMood == PetMood.Sleepy ? 1.5 : 0;

        _puppetView.SetRootPose(
            offsetY: sleepySink + speakingLift - liftedBreath * IdleBobPixels,
            scaleX: 1 + liftedBreath * IdleSquashAmount * 0.6,
            scaleY: 1 + liftedBreath * IdleSquashAmount);
    }

    private (Vector Left, Vector Right) GetMouseGazeOffsets()
    {
        var cursor = Forms.Control.MousePosition;
        var leftOffset = LeftEyeNeutralOffset + GetCursorEyeOffset(
            _puppetView.GetPartCenterScreenPoint(LeftEye, LeftEyeNeutralOffset),
            cursor);
        var rightOffset = RightEyeNeutralOffset + GetCursorEyeOffset(
            _puppetView.GetPartCenterScreenPoint(RightEye, RightEyeNeutralOffset),
            cursor);

        return (leftOffset, rightOffset);
    }

    private (Vector Left, Vector Right) GetViewerGazeOffsets()
    {
        var leftEyeCenter = _puppetView.GetPartCenterScreenPoint(LeftEye, LeftEyeNeutralOffset);
        var rightEyeCenter = _puppetView.GetPartCenterScreenPoint(RightEye, RightEyeNeutralOffset);
        var mouthCenter = _puppetView.GetPartCenterScreenPoint(MouthA, new Vector());
        var faceTarget = new System.Windows.Point(
            (leftEyeCenter.X + rightEyeCenter.X + mouthCenter.X) / 3,
            (leftEyeCenter.Y + rightEyeCenter.Y + mouthCenter.Y) / 3);
        var viewerTarget = GetViewerTarget(faceTarget);

        // Speaking should feel like the pet is looking through its own face toward the viewer.
        // Assume the viewer's head is centered on the active screen and a fixed distance away.
        var leftOffset = LeftEyeNeutralOffset + GetViewerEyeOffset(leftEyeCenter, viewerTarget.ScreenCenter, viewerTarget.Distance);
        var rightOffset = RightEyeNeutralOffset + GetViewerEyeOffset(rightEyeCenter, viewerTarget.ScreenCenter, viewerTarget.Distance);

        return (leftOffset, rightOffset);
    }

    private static (System.Windows.Point ScreenCenter, double Distance) GetViewerTarget(System.Windows.Point faceTarget)
    {
        var facePoint = new System.Drawing.Point(
            (int)Math.Round(faceTarget.X),
            (int)Math.Round(faceTarget.Y));
        var screenBounds = Forms.Screen.FromPoint(facePoint).Bounds;
        var screenCenter = new System.Windows.Point(
            screenBounds.Left + screenBounds.Width / 2.0,
            screenBounds.Top + screenBounds.Height / 2.0);

        // Windows gives reliable screen pixels here, but physical monitor size is less dependable.
        // Keep the real-world assumption visible until this becomes a user setting.
        var pixelsPerCentimeter = screenBounds.Width / AssumedMonitorWidthCentimeters;
        return (screenCenter, ViewerDistanceFromScreenCentimeters * pixelsPerCentimeter);
    }

    private static Vector GetCursorEyeOffset(System.Windows.Point eyeCenter, System.Drawing.Point cursor)
    {
        return GetPointEyeOffset(eyeCenter, new System.Windows.Point(cursor.X, cursor.Y));
    }

    private static Vector GetPointEyeOffset(System.Windows.Point eyeCenter, System.Windows.Point target)
    {
        var deltaX = target.X - eyeCenter.X;
        var deltaY = target.Y - eyeCenter.Y;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance <= 0.001)
        {
            return new Vector();
        }

        var strength = Clamp(distance / FullGazeDistance, 0, 1);
        return new Vector(
            deltaX / distance * MaximumEyeOffsetX * strength,
            deltaY / distance * MaximumEyeOffsetY * strength);
    }

    private static Vector GetViewerEyeOffset(
        System.Windows.Point eyeCenter,
        System.Windows.Point viewerTarget,
        double viewerDistance)
    {
        return new Vector(
            Clamp((viewerTarget.X - eyeCenter.X) / viewerDistance * MaximumEyeOffsetX, -MaximumEyeOffsetX, MaximumEyeOffsetX),
            Clamp((viewerTarget.Y - eyeCenter.Y) / viewerDistance * MaximumEyeOffsetY, -MaximumEyeOffsetY, MaximumEyeOffsetY));
    }

    private void EndMood(int scopeId)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => EndMood(scopeId));
            return;
        }

        if (_moodScopes.Remove(scopeId))
        {
            UpdateMood();
        }
    }

    private void UpdateMood()
    {
        var now = DateTime.UtcNow;
        var mood = ResolveMood(now);
        if (mood == _currentMood)
        {
            return;
        }

        _currentMood = mood;
        ApplyMoodIcon(mood);
    }

    private PetMood ResolveMood(DateTime now)
    {
        if (_temporaryMood is not null)
        {
            if (now < _temporaryMoodExpiresAtUtc)
            {
                return _temporaryMood.Value;
            }

            _temporaryMood = null;
        }

        if (_moodScopes.ContainsValue(PetMood.Speaking))
        {
            return PetMood.Speaking;
        }

        if (_moodScopes.ContainsValue(PetMood.Thinking))
        {
            return PetMood.Thinking;
        }

        return now - _lastActivityAtUtc >= IdleMoodDelay
            ? PetMood.Sleepy
            : PetMood.Idle;
    }

    private void ApplyMoodIcon(PetMood mood)
    {
        MoodBadge.Visibility = mood is PetMood.Thinking or PetMood.Alarmed or PetMood.Sleepy
            ? Visibility.Visible
            : Visibility.Collapsed;
        ThinkingMoodIcon.Visibility = mood == PetMood.Thinking ? Visibility.Visible : Visibility.Collapsed;
        AlarmedMoodIcon.Visibility = mood == PetMood.Alarmed ? Visibility.Visible : Visibility.Collapsed;
        SleepyMoodIcon.Visibility = mood == PetMood.Sleepy ? Visibility.Visible : Visibility.Collapsed;

        if (mood != PetMood.Thinking)
        {
            ThinkingMoodRotate.Angle = 0;
        }
    }

    private void UpdateMoodIconAnimation()
    {
        if (_currentMood == PetMood.Thinking)
        {
            ThinkingMoodRotate.Angle = _idleClock.Elapsed.TotalSeconds * 180 % 360;
        }
    }

    private void StopSpeaking()
    {
        _isSpeaking = false;
        _showMouthB = false;
        SetMouthFrame(showMouthB: false);
    }

    private void SetMouthOpen(double openness)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetMouthOpen(openness));
            return;
        }

        var threshold = _showMouthB ? MouthCloseThreshold : MouthOpenThreshold;
        var shouldShowOpenMouth = _showMouthB
            ? openness > threshold
            : openness >= threshold;

        if (shouldShowOpenMouth == _showMouthB)
        {
            return;
        }

        _showMouthB = shouldShowOpenMouth;
        SetMouthFrame(_showMouthB);
    }

    private void SetMouthFrame(bool showMouthB)
    {
        _puppetView.SetPartVisible(MouthA, !showMouthB);
        _puppetView.SetPartVisible(MouthB, showMouthB);
    }

    private void SetEyeOffset(Vector leftOffset, Vector rightOffset)
    {
        _currentLeftEyeOffset = Lerp(_currentLeftEyeOffset, leftOffset, GazeSmoothing);
        _currentRightEyeOffset = Lerp(_currentRightEyeOffset, rightOffset, GazeSmoothing);
        ApplyEyeOffset(_currentLeftEyeOffset, _currentRightEyeOffset);
    }

    private void ApplyEyeOffset(Vector leftOffset, Vector rightOffset)
    {
        _puppetView.SetPartOffset(LeftEye, leftOffset.X, leftOffset.Y);
        _puppetView.SetPartOffset(RightEye, rightOffset.X, rightOffset.Y);
    }

    private static Vector Lerp(Vector current, Vector target, double amount)
    {
        return current + (target - current) * amount;
    }

    private sealed class SpeakingScope : ISpeakingScope
    {
        private PetOverlayWindow? _window;
        private IDisposable? _moodScope;

        public SpeakingScope(PetOverlayWindow window, IDisposable moodScope)
        {
            _window = window;
            _moodScope = moodScope;
        }

        public void SetMouthOpen(double openness)
        {
            _window?.SetMouthOpen(openness);
        }

        public void Dispose()
        {
            _window?.StopSpeaking();
            _window = null;
            _moodScope?.Dispose();
            _moodScope = null;
        }
    }

    private sealed class MoodScope : IDisposable
    {
        private PetOverlayWindow? _window;
        private readonly int _scopeId;

        public MoodScope(PetOverlayWindow window, int scopeId)
        {
            _window = window;
            _scopeId = scopeId;
        }

        public void Dispose()
        {
            _window?.EndMood(_scopeId);
            _window = null;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
