using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopPet.App.Overlay;

public partial class PetOverlayWindow : Window, IPetPerformanceController
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
    private static readonly TimeSpan MouthFrameInterval = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan GazeUpdateInterval = TimeSpan.FromMilliseconds(33);
    private const string MouthA = "Mouth A";
    private const string MouthB = "Mouth B";
    private const string LeftEye = "Left Eye";
    private const string RightEye = "Right Eye";
    private static readonly Vector LeftEyeNeutralOffset = new(-14, 12);
    private static readonly Vector RightEyeNeutralOffset = new(-14, 12);

    private readonly WpfInochiPuppetView _puppetView = new();
    private readonly DispatcherTimer _mouthTimer;
    private readonly DispatcherTimer _gazeTimer;
    private readonly Stopwatch _idleClock = Stopwatch.StartNew();
    private bool _isClickThrough;
    private bool _isSpeaking;
    private bool _showMouthB;
    private Vector _currentLeftEyeOffset = LeftEyeNeutralOffset;
    private Vector _currentRightEyeOffset = RightEyeNeutralOffset;

    public PetOverlayWindow()
    {
        InitializeComponent();

        var puppetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "bug.inp");
        _puppetView.Load(InochiPuppetLoader.Load(puppetPath));
        PuppetHost.Child = _puppetView;
        SetMouthFrame(showMouthB: false);
        ApplyEyeOffset(_currentLeftEyeOffset, _currentRightEyeOffset);

        _mouthTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = MouthFrameInterval
        };
        _mouthTimer.Tick += OnMouthTimerTick;

        _gazeTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = GazeUpdateInterval
        };
        _gazeTimer.Tick += OnGazeTimerTick;

        Loaded += (_, _) => MoveNearBottomRight();
        Loaded += (_, _) => _gazeTimer.Start();
        Closed += (_, _) =>
        {
            _mouthTimer.Stop();
            _mouthTimer.Tick -= OnMouthTimerTick;
            _gazeTimer.Stop();
            _gazeTimer.Tick -= OnGazeTimerTick;
        };
    }

    public IDisposable BeginSpeaking()
    {
        _isSpeaking = true;
        _showMouthB = false;
        SetMouthFrame(showMouthB: false);
        _mouthTimer.Start();

        return new SpeakingScope(this);
    }

    public void SetClickThrough(bool isClickThrough)
    {
        _isClickThrough = isClickThrough;

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
        MoveNearBottomRight();

        Show();
        Activate();
    }

    private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        KeepInsideWorkArea();
    }

    private void MoveNearBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - EdgeMargin;
        Top = workArea.Bottom - Height - EdgeMargin;
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

    private void OnMouthTimerTick(object? sender, EventArgs e)
    {
        _showMouthB = !_showMouthB;
        SetMouthFrame(_showMouthB);
    }

    private void OnGazeTimerTick(object? sender, EventArgs e)
    {
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
        var speakingLift = _isSpeaking ? -1.5 : 0;

        _puppetView.SetRootPose(
            offsetY: speakingLift - liftedBreath * IdleBobPixels,
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

    private void StopSpeaking()
    {
        _isSpeaking = false;
        _mouthTimer.Stop();
        _showMouthB = false;
        SetMouthFrame(showMouthB: false);
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

    private sealed class SpeakingScope : IDisposable
    {
        private PetOverlayWindow? _window;

        public SpeakingScope(PetOverlayWindow window)
        {
            _window = window;
        }

        public void Dispose()
        {
            _window?.StopSpeaking();
            _window = null;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
