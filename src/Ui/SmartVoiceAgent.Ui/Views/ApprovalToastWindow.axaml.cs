using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// A small always-on-top card in the corner of the screen for a tool call waiting for approval.
    /// </summary>
    public partial class ApprovalToastWindow : Window
    {
        private const int ScreenMargin = 8;

        public ApprovalToastWindow()
        {
            InitializeComponent();
            this.FindControl<Button>("ReviewButton")!.Click += (_, _) => ReviewRequested?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("DismissButton")!.Click += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);
            Opened += (_, _) => MoveToCorner();
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
            this.FindControl<TextBlock>("ToolText")!.Text = string.IsNullOrWhiteSpace(detail)
                ? toolName
                : $"{toolName} · {detail}";
            this.FindControl<TextBlock>("ThreadText")!.Text = othersWaiting > 0
                ? $"In \"{threadTitle}\" · {othersWaiting} more waiting"
                : $"In \"{threadTitle}\"";
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
