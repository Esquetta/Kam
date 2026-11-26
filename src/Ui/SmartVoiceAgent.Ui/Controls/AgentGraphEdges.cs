using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.Collections.Generic;

namespace SmartVoiceAgent.Ui.Controls
{
    /// <summary>
    /// Draws a dot grid and the edges of an agent graph as horizontal curves under its nodes. An edge takes the
    /// color of the subagent it leads to or from; a running edge is dashed.
    /// </summary>
    public sealed class AgentGraphEdges : Control
    {
        /// <summary>Defines <see cref="Edges"/>.</summary>
        public static readonly StyledProperty<IReadOnlyList<GraphEdge>?> EdgesProperty =
            AvaloniaProperty.Register<AgentGraphEdges, IReadOnlyList<GraphEdge>?>(nameof(Edges));

        private const double GridSpacing = 20;
        private const double GridDotSize = 1.5;

        private static readonly DashStyle RunningDash = new([4, 3], 0);

        // Dash lengths are in pen widths: a zero dash with a round cap draws a dot every GridSpacing.
        private static readonly DashStyle GridDots = new([0, GridSpacing / GridDotSize], 0);

        static AgentGraphEdges()
        {
            AffectsRender<AgentGraphEdges>(EdgesProperty);
        }

        /// <summary>Gets or sets the edges to draw.</summary>
        public IReadOnlyList<GraphEdge>? Edges
        {
            get => GetValue(EdgesProperty);
            set => SetValue(EdgesProperty, value);
        }

        /// <inheritdoc />
        public override void Render(DrawingContext context)
        {
            RenderGrid(context);
            if (Edges is not { Count: > 0 } edges)
            {
                return;
            }

            foreach (var edge in edges)
            {
                var pen = new Pen(Brush(edge.StatusKey), edge.StatusKey is "running" or "needs-you" ? 1.6 : 1.2)
                {
                    DashStyle = edge.StatusKey == "running" ? RunningDash : null,
                    LineCap = PenLineCap.Round
                };

                // A node dragged left of the one it leads to still gets a curve that leaves and enters sideways.
                var bend = Math.Max(Math.Abs(edge.To.X - edge.From.X) / 2, 24);
                var geometry = new StreamGeometry();
                using (var figure = geometry.Open())
                {
                    figure.BeginFigure(edge.From, isFilled: false);
                    figure.CubicBezierTo(
                        new Point(edge.From.X + bend, edge.From.Y),
                        new Point(edge.To.X - bend, edge.To.Y),
                        edge.To);
                    figure.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, pen, geometry);
            }
        }

        private void RenderGrid(DrawingContext context)
        {
            var pen = new Pen(Resource("BorderSubtleBrush"), GridDotSize) { DashStyle = GridDots, LineCap = PenLineCap.Round };
            for (var y = GridSpacing; y < Bounds.Height; y += GridSpacing)
            {
                context.DrawLine(pen, new Point(GridSpacing, y), new Point(Bounds.Width, y));
            }
        }

        private IBrush Brush(string statusKey)
        {
            var key = statusKey switch
            {
                "running" => "AccentBrush",
                "needs-you" => "AccentOrangeBrush",
                "failed" => "AccentErrorBrush",
                _ => "BorderMutedBrush"
            };

            return Resource(key);
        }

        private IBrush Resource(string key)
        {
            return this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush
                ? brush
                : Brushes.Gray;
        }
    }
}
