namespace SmartVoiceAgent.Core.Dtos
{
    public record NormalizedRectangle
    {
        /// <summary>
        /// X coordinate normalized to [0..1] in monitor-local coordinates (0 = left, 1 = right).
        /// </summary>
        public double X { get; init; }

        /// <summary>
        /// Y coordinate normalized to [0..1] in monitor-local coordinates (0 = top, 1 = bottom).
        /// </summary>
        public double Y { get; init; }

        /// <summary>
        /// Width normalized to [0..1] relative to monitor width.
        /// </summary>
        public double Width { get; init; }

        /// <summary>
        /// Height normalized to [0..1] relative to monitor height.
        /// </summary>
        public double Height { get; init; }
    }

}
