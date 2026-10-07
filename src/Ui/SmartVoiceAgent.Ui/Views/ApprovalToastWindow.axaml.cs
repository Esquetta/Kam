using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SmartVoiceAgent.Ui.Services;
using System;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// A small always-on-top card in the corner of the screen for a tool call waiting for approval.
    /// </summary>
    public partial class ApprovalToastWindow : Window
    {
        private const int ScreenMargin = 8;

        private string _toolName = string.Empty;
        private string _detail = string.Empty;
        private string _threadTitle = string.Empty;
        private int _othersWaiting;

        public ApprovalToastWindow()
        {
            InitializeComponent();
            this.FindControl<Button>("ReviewButton")!.Click += (_, _) => ReviewRequested?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("DismissButton")!.Click += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);
            Opened += (_, _) =>
            {
                LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
                MoveToCorner();
            };
            Closed += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            SizeChanged += (_, _) => MoveToCorner();
        }

        /// <summary>Raised when the user asks to see the approval card.</summary>
        public event EventHandler? ReviewRequested;

        /// <summary>Raised when the user closes the card without answering.</summary>
        public event EventHandler? DismissRequested;

        /// <summary>
        /// Shows which tool waits, in which chat, and how many others wait too.
        /// </summary>
        public void SetNotice(string toolName, string detail, string threadTitle, int othersWaiting)
        {
            _toolName = toolName;
            _detail = detail;
            _threadTitle = threadTitle;
            _othersWaiting = othersWaiting;
            ApplyNotice();
        }

        private void ApplyNotice()
        {
            this.FindControl<TextBlock>("ToolText")!.Text = string.IsNullOrWhiteSpace(_detail)
                ? _toolName
                : $"{_toolName} · {_detail}";
            this.FindControl<TextBlock>("ThreadText")!.Text = _othersWaiting > 0
                ? Loc.Format("Shell.Toast.ThreadWithOthers", _threadTitle, _othersWaiting)
                : Loc.Format("Shell.Toast.Thread", _threadTitle);
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(ApplyNotice);
        }

        private void MoveToCorner()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
            {
                return;
            }

            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            var size = FrameSize ?? Bounds.Size;
            Position = new PixelPoint(
                area.Right - (int)Math.Ceiling(size.Width * scale) - ScreenMargin,
                area.Bottom - (int)Math.Ceiling(size.Height * scale) - ScreenMargin);
        }
    }
}
