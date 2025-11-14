using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos
{
    /// <summary>
    /// Normalized rectangle with values between 0 and 1
    /// </summary>
    public record NormalizedRectangle
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }

        /// <summary>
        /// Center point X coordinate (normalized)
        /// </summary>
        public double CenterX => X + (Width / 2);

        /// <summary>
        /// Center point Y coordinate (normalized)
        /// </summary>
        public double CenterY => Y + (Height / 2);

        /// <summary>
        /// Area of the rectangle (0-1 range)
        /// </summary>
        public double Area => Width * Height;

        /// <summary>
        /// Check if point is inside this rectangle
        /// </summary>
        public bool Contains(double x, double y)
        {
            return x >= X && x <= (X + Width) && y >= Y && y <= (Y + Height);
        }

        /// <summary>
        /// Convert back to pixel coordinates given screen dimensions
        /// </summary>
        public Rectangle ToAbsolute(int screenWidth, int screenHeight)
        {
            return new Rectangle(
                (int)(X * screenWidth),
                (int)(Y * screenHeight),
                (int)(Width * screenWidth),
                (int)(Height * screenHeight)
            );
        }

        /// <summary>
        /// Create normalized rectangle from absolute bounds
        /// </summary>
        public static NormalizedRectangle FromAbsolute(Rectangle bounds, int screenWidth, int screenHeight)
        {
            return new NormalizedRectangle
            {
                X = (double)bounds.X / screenWidth,
                Y = (double)bounds.Y / screenHeight,
                Width = (double)bounds.Width / screenWidth,
                Height = (double)bounds.Height / screenHeight
            };
        }
    }

}
