using System;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// The widths the user can drag the rail to, with its default and limits.
/// </summary>
public static class PaneWidths
{
    /// <summary>The rail's default width (Kam Studio's 232 px).</summary>
    public const double RailDefault = 232;

    /// <summary>The narrowest rail that still fits its labels and ladder badges.</summary>
    public const double RailMin = 180;

    /// <summary>The widest rail.</summary>
    public const double RailMax = 360;

    /// <summary>Returns a rail width inside the limits; 0 or less, or not a number, gives the default.</summary>
    /// <param name="width">The requested width.</param>
    public static double ClampRail(double width) =>
        double.IsFinite(width) && width > 0 ? Math.Round(Math.Clamp(width, RailMin, RailMax)) : RailDefault;
}
