using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System.Collections.Generic;

namespace SmartVoiceAgent.Ui.Controls
{
    /// <summary>
    /// Draws the edges of an agent graph as horizontal curves under its nodes. An edge takes the color of the
    /// subagent it leads to or from; a running edge is dashed.
    /// </summary>
    public sealed class AgentGraphEdges : Control
    {
        /// <summary>Defines <see cref="Edges"/>.</summary>
        public static readonly StyledProperty<IReadOnlyList<GraphEdge>?> EdgesProperty =
            AvaloniaProperty.Register<AgentGraphEdges, IReadOnlyList<GraphEdge>?>(nameof(Edges));

        private static readonly DashStyle RunningDash = new([4, 3], 0);

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

                var bend = (edge.To.X - edge.From.X) / 2;
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

        private IBrush Brush(string statusKey)
        {
            var key = statusKey switch
            {
                "running" => "AccentBrush",
                "needs-you" => "AccentOrangeBrush",
                "failed" => "AccentErrorBrush",
                _ => "BorderMutedBrush"
            };

            return this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush
                ? brush
                : Brushes.Gray;
        }
    }
}
