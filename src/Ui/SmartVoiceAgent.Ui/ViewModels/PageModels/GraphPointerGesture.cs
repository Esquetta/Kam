using Avalonia;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// A press on the graph canvas that becomes a drag once the pointer moves past <see cref="DragThreshold"/>;
    /// released before that, it is a click.
    /// </summary>
    public sealed class GraphPointerGesture
    {
        /// <summary>How far, in screen units, the pointer moves before a press becomes a drag.</summary>
        public const double DragThreshold = 4;

        private readonly Point _start;

        /// <summary>Starts a gesture.</summary>
        /// <param name="start">Where the pointer was pressed, in screen units.</param>
        public GraphPointerGesture(Point start)
        {
            _start = start;
        }

        /// <summary>Gets whether the press became a drag.</summary>
        public bool IsDragging { get; private set; }

        /// <summary>Gets whether releasing now counts as a click.</summary>
        public bool IsClick => !IsDragging;

        /// <summary>Gets how far the pointer moved since the press.</summary>
        public Vector Delta { get; private set; }

        /// <summary>Follows the pointer.</summary>
        /// <param name="pointer">Where the pointer is now, in screen units.</param>
        /// <returns>Whether the gesture is a drag.</returns>
        public bool Move(Point pointer)
        {
            Delta = pointer - _start;
            if (!IsDragging && Delta.Length >= DragThreshold)
            {
                IsDragging = true;
            }

            return IsDragging;
        }
    }
}
