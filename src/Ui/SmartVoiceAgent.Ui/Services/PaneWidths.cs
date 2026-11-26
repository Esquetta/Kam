using System;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// The widths the user can drag the rail and the Activity panel to, with their defaults and limits.
/// </summary>
public static class PaneWidths
{
    /// <summary>The rail's default width (Kam Studio's 232 px).</summary>
    public const double RailDefault = 232;

    /// <summary>The narrowest rail that still fits its labels and ladder badges.</summary>
    public const double RailMin = 180;

    /// <summary>The widest rail.</summary>
    public const double RailMax = 360;

    /// <summary>The Activity panel's default width (Kam Studio's 290 px context pane).</summary>
    public const double ActivityDefault = 290;

    /// <summary>The narrowest Activity panel that still fits its tabs.</summary>
    public const double ActivityMin = 240;

    /// <summary>The widest Activity panel.</summary>
    public const double ActivityMax = 560;

    /// <summary>Returns a rail width inside the limits; 0 or less, or not a number, gives the default.</summary>
    /// <param name="width">The requested width.</param>
    public static double ClampRail(double width) => Clamp(width, RailDefault, RailMin, RailMax);

    /// <summary>Returns an Activity panel width inside the limits; 0 or less, or not a number, gives the default.</summary>
    /// <param name="width">The requested width.</param>
    public static double ClampActivity(double width) => Clamp(width, ActivityDefault, ActivityMin, ActivityMax);

    private static double Clamp(double width, double fallback, double min, double max) =>
        double.IsFinite(width) && width > 0 ? Math.Round(Math.Clamp(width, min, max)) : fallback;
}
