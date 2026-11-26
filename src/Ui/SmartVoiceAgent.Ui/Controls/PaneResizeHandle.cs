using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.Controls
{
    /// <summary>
    /// A thin strip on a pane's edge that changes the pane's width when dragged. A double click puts the default
    /// width back. <see cref="Committed"/> runs when a drag or reset ends, so the width is saved once, not on every move.
    /// </summary>
    public sealed class PaneResizeHandle : Control
    {
        /// <summary>Defines <see cref="Size"/>.</summary>
        public static readonly StyledProperty<double> SizeProperty =
            AvaloniaProperty.Register<PaneResizeHandle, double>(nameof(Size), defaultBindingMode: BindingMode.TwoWay);

        /// <summary>Defines <see cref="DefaultSize"/>.</summary>
        public static readonly StyledProperty<double> DefaultSizeProperty =
            AvaloniaProperty.Register<PaneResizeHandle, double>(nameof(DefaultSize));

        /// <summary>Defines <see cref="GrowsLeft"/>.</summary>
        public static readonly StyledProperty<bool> GrowsLeftProperty =
            AvaloniaProperty.Register<PaneResizeHandle, bool>(nameof(GrowsLeft));

        /// <summary>Defines <see cref="Committed"/>.</summary>
        public static readonly StyledProperty<ICommand?> CommittedProperty =
            AvaloniaProperty.Register<PaneResizeHandle, ICommand?>(nameof(Committed));

        /// <summary>Defines <see cref="LineBrush"/>.</summary>
        public static readonly StyledProperty<IBrush?> LineBrushProperty =
            AvaloniaProperty.Register<PaneResizeHandle, IBrush?>(nameof(LineBrush));

        private double _startX;
        private double _startSize;
        private bool _dragging;

        static PaneResizeHandle()
        {
            CursorProperty.OverrideDefaultValue<PaneResizeHandle>(new Cursor(StandardCursorType.SizeWestEast));
            FocusableProperty.OverrideDefaultValue<PaneResizeHandle>(false);
            AffectsRender<PaneResizeHandle>(LineBrushProperty);
        }

        /// <summary>Gets or sets the pane width; the view model limits it.</summary>
        public double Size
        {
            get => GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        /// <summary>Gets or sets the width a double click restores.</summary>
        public double DefaultSize
        {
            get => GetValue(DefaultSizeProperty);
            set => SetValue(DefaultSizeProperty, value);
        }

        /// <summary>Gets or sets whether dragging left widens the pane (a pane on the right side of the window).</summary>
        public bool GrowsLeft
        {
            get => GetValue(GrowsLeftProperty);
            set => SetValue(GrowsLeftProperty, value);
        }

        /// <summary>Gets or sets the command run when a drag or a reset ends.</summary>
        public ICommand? Committed
        {
            get => GetValue(CommittedProperty);
            set => SetValue(CommittedProperty, value);
        }

        /// <summary>Gets or sets the line drawn while the pointer is over the handle or dragging it.</summary>
        public IBrush? LineBrush
        {
            get => GetValue(LineBrushProperty);
            set => SetValue(LineBrushProperty, value);
        }

        /// <inheritdoc />
        public override void Render(DrawingContext context)
        {
            // Transparent fill so the whole strip takes the pointer; the line shows only on hover or drag.
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            if ((IsPointerOver || _dragging) && LineBrush is { } brush)
            {
                var x = Math.Round(Bounds.Width / 2) - 1;
                context.FillRectangle(brush, new Rect(x, 0, 2, Bounds.Height));
            }
        }

        /// <inheritdoc />
        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            InvalidateVisual();
        }

        /// <inheritdoc />
        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            InvalidateVisual();
        }

        /// <inheritdoc />
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (e.ClickCount >= 2)
            {
                Size = DefaultSize;
                Commit();
                e.Handled = true;
                return;
            }

            _dragging = true;
            _startX = e.GetPosition(null).X;
            _startSize = Size;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        /// <inheritdoc />
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging)
            {
                return;
            }

            var delta = e.GetPosition(null).X - _startX;
            Size = _startSize + (GrowsLeft ? -delta : delta);
            e.Handled = true;
        }

        /// <inheritdoc />
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            e.Pointer.Capture(null);
            Commit();
            e.Handled = true;
            InvalidateVisual();
        }

        /// <inheritdoc />
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            if (_dragging)
            {
                _dragging = false;
                Commit();
                InvalidateVisual();
            }
        }

        private void Commit()
        {
            if (Committed is { } command && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }
}
