# Responsive Design Implementation

This document describes the responsive design improvements made to the Smart Voice Agent UI.

## Breakpoints

The shell follows the Kam Studio breakpoints:

| Width | Behavior |
|-------|----------|
| 1280px and up | Tasks shows the task list (270px) and the thread |
| < 1280px | Task list narrows to 240px (`IsLogPanelVisible` is false) |
| < 980px | Top bar drops the wake phrase pill and the search text (`IsNarrow`) |
| < 760px | Rail shows icons only and the task list sits above the thread (`IsRailCompact`) |

`IsCompact` (< 1024px), `IsMedium` and `IsExpanded` still drive `GridColumns` and the responsive converters.

## Files Added

### 1. WindowStateManager.cs
**Location:** `src/Ui/SmartVoiceAgent.Ui/Services/WindowStateManager.cs`

A singleton service that tracks window dimensions and provides responsive state:
- `WindowWidth` / `WindowHeight` - Current window dimensions
- `IsCompact` / `IsMedium` / `IsExpanded` - Breakpoint flags
- `GridColumns` - Number of columns for grid layouts (1-3)
- `IsLogPanelVisible` - Whether the window is 1280px or wider (the Tasks list keeps 270px)
- `ReducedMotion` - Accessibility preference for disabling animations

### 2. ResponsiveConverters.cs
**Location:** `src/Ui/SmartVoiceAgent.Ui/Converters/ResponsiveConverters.cs`

XAML converters for responsive bindings:
- `ResponsiveColumnConverter` - Returns column count from WindowStateManager
- `LogPanelVisibilityConverter` - Returns visibility boolean
- `ResponsiveScaleConverter` - Scales values based on breakpoint
- `ResponsiveFontSizeConverter` - Returns scaled font sizes
- `CompactModeConverter` / `MediumModeConverter` / `ExpandedModeConverter` - Breakpoint checks
- `ReducedMotionConverter` - Returns reduced motion preference
- `BooleanNegationConverter` - Inverts boolean values

## Files Modified

### MainWindow.axaml
**Changes:**
- Added WindowStateManager resource
- Added responsive converters
- Log panel width now adapts: 0px (compact), 420px (medium), 600px (expanded)
- Log panel font size increased: 11px → 13px for better readability
- Log panel visibility bound to `IsLogPanelVisible`
- Minimum window size reduced from 1024x700 to 800x600

### MainWindow.axaml.cs
**Changes:**
- Attaches WindowStateManager to window on `OnOpened()`

### CoordinatorView.axaml
**Changes:**
- Cards now use `ItemsControl` with `UniformGrid` bound to `GridColumns`
- Title font size scales responsively
- Neural Orb size scales responsively

### PluginsView.axaml
**Changes:**
- Cards now use `ItemsControl` with `UniformGrid` bound to `GridColumns`
- Title font size scales responsively
- Card height changed from fixed `220` to `MinHeight="180"`

### NeuralOrb.axaml
**Changes:**
- Animations respect `ReducedMotion` preference
- Static alternatives shown when reduced motion is enabled
- Three animated elements toggle visibility based on accessibility setting

### SettingsView.axaml
**Changes:**
- Reduced Motion toggle now bound to `WindowStateManager.ReducedMotion`

## Usage Examples

### Responsive Grid Columns
```xml
<UniformGrid Columns="{Binding Source={StaticResource WindowStateManager}, Path=GridColumns}">
```

### Responsive Font Size
```xml
<TextBlock FontSize="{Binding Source={StaticResource WindowStateManager}, Path=WindowWidth, Converter={StaticResource ResponsiveFontSizeConverter}, ConverterParameter=48}" />
```

### Conditional Visibility (Reduced Motion)
```xml
<Ellipse Classes="RotatingRing"
         IsVisible="{Binding Source={StaticResource WindowStateManager}, Path=ReducedMotion, Converter={StaticResource BooleanNegationConverter}}" />
```

## Accessibility

### Reduced Motion Support
All animations now respect the user's reduced motion preference:
- Neural Orb: Rotating rings and pulsing glow disabled
- PluginsView: Pulsing dot animation disabled when preference is set
- Settings: Toggle switch controls the preference

To check reduced motion in code:
```csharp
if (WindowStateManager.Instance.ReducedMotion)
{
    // Disable animations
}
```

## Testing Responsive Design

1. **Resize the window** to see layout adapt:
   - 1280px and up: 270px task list and the thread
   - < 1280px: 240px task list
   - < 980px: top bar trims to the search icon and the mic
   - < 760px: icon rail, task list above the thread

2. **Toggle Reduced Motion** in Settings to see animations disable

3. **Minimum size**: Window can resize down to 640x560

## Future Improvements

- Add touch-friendly button sizes for tablet mode
- Implement collapsible navigation sidebar for compact mode
- Add font scaling based on OS accessibility settings
- Consider adding a hamburger menu for navigation on small screens
