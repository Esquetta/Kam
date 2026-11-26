using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.ComponentModel;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// The Graphs page: a live graph of a task's main agent and subagents. Its DataContext is a <see cref="GraphsViewModel"/>.
    /// Dragging a node moves it, dragging empty canvas pans, and Ctrl+wheel zooms around the pointer.
    /// </summary>
    public partial class GraphsView : UserControl
    {
        // Below this page width the task list moves above the graph and the detail below it.
        private const double NarrowWidth = 1100;

        // One wheel notch zooms by this factor.
        private const double WheelZoomFactor = 1.1;

        private static Cursor? s_grabCursor;
        private static Cursor? s_moveCursor;
        private GraphsViewModel? _viewModel;
        private GraphPointerGesture? _gesture;
        private GraphNodeViewModel? _dragNode;
        private Point _dragOrigin;
        private Vector _panOrigin;

        /// <summary>
        /// Creates the page.
        /// </summary>
        public GraphsView()
        {
            InitializeComponent();
            Page.SizeChanged += (_, e) => Page.Classes.Set("narrow", e.NewSize.Width < NarrowWidth);

            // Tunnel handlers see the press before a node's button does, so a drag never ends in a click.
            GraphScroll.AddHandler(PointerPressedEvent, OnGraphPointerPressed, RoutingStrategies.Tunnel);
            GraphScroll.AddHandler(PointerWheelChangedEvent, OnGraphPointerWheelChanged, RoutingStrategies.Tunnel);
            GraphScroll.PointerMoved += OnGraphPointerMoved;
            GraphScroll.PointerReleased += OnGraphPointerReleased;
            GraphScroll.PointerCaptureLost += (_, _) => EndGesture();
            GraphScroll.PropertyChanged += (_, e) =>
            {
                if (e.Property == ScrollViewer.ViewportProperty)
                {
                    _viewModel?.SetViewport(GraphScroll.Viewport);
                }
            };
        }

        /// <inheritdoc />
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as GraphsViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _viewModel.SetViewport(GraphScroll.Viewport);
            }

            ApplyZoom();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GraphsViewModel.Zoom))
            {
                ApplyZoom();
            }
        }

        private void ApplyZoom()
        {
            var zoom = _viewModel?.Zoom ?? 1;
            GraphZoom.LayoutTransform = new ScaleTransform(zoom, zoom);
        }

        private void OnGraphPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(GraphScroll);
            if (_viewModel is null || !point.Properties.IsLeftButtonPressed || e.Source is not Visual source
                || source.FindAncestorOfType<ScrollBar>(includeSelf: true) is not null)
            {
                return;
            }

            _dragNode = (source as StyledElement)?.DataContext as GraphNodeViewModel;
            _dragOrigin = _dragNode is null ? default : new Point(_dragNode.X, _dragNode.Y);
            _panOrigin = GraphScroll.Offset;
            _gesture = new GraphPointerGesture(point.Position);
            e.Pointer.Capture(GraphScroll);
            e.Handled = true;
        }

        private void OnGraphPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_gesture is null || _viewModel is null || !_gesture.Move(e.GetPosition(GraphScroll)))
            {
                return;
            }

            GraphSurface.Cursor = s_moveCursor ??= new Cursor(StandardCursorType.SizeAll);
            if (_dragNode is not null)
            {
                _viewModel.DragNode(_dragNode.Id, _dragOrigin, _gesture.Delta);
            }
            else
            {
                GraphScroll.Offset = _panOrigin - _gesture.Delta;
            }

            e.Handled = true;
        }

        private void OnGraphPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_gesture is null)
            {
                return;
            }

            var isClick = _gesture.IsClick;
            var node = _dragNode;
            EndGesture();
            e.Pointer.Capture(null);
            e.Handled = true;

            // A press that did not move clicks the node, as the button would; joins have nothing to show.
            if (isClick && node is { IsJoin: false } && _viewModel is not null)
            {
                _viewModel.SelectNodeCommand.Execute(node).Subscribe();
            }
        }

        private void OnGraphPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (_viewModel is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                return;
            }

            var pointer = e.GetPosition(GraphScroll);
            var oldZoom = _viewModel.Zoom;
            _viewModel.SetZoom(oldZoom * Math.Pow(WheelZoomFactor, e.Delta.Y));
            if (_viewModel.Zoom != oldZoom)
            {
                // Lay out at the new size first, or the scroll viewer clamps the offset to the old extent.
                var offset = GraphsViewModel.ZoomedOffset(GraphScroll.Offset, pointer, oldZoom, _viewModel.Zoom);
                GraphScroll.UpdateLayout();
                GraphScroll.Offset = offset;
            }

            e.Handled = true;
        }

        private void EndGesture()
        {
            _gesture = null;
            _dragNode = null;
            GraphSurface.Cursor = s_grabCursor ??= new Cursor(StandardCursorType.Hand);
        }
    }
}
