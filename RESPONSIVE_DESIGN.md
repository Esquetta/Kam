# Responsive Design Implementation

This document describes the responsive design improvements made to the Smart Voice Agent UI.

## Breakpoints

The application uses three responsive breakpoints:

| Breakpoint | Width Range | Behavior |
|------------|-------------|----------|
| **Compact** | < 1024px | Single column layout, log panel hidden |
| **Medium** | 1024px - 1440px | Two column layout, log panel visible (320px) |
| **Expanded** | > 1440px | Three column layout, log panel visible (450px) |

## Files Added

### 1. WindowStateManager.cs
**Location:** `src/Ui/SmartVoiceAgent.Ui/Services/WindowStateManager.cs`

A singleton service that tracks window dimensions and provides responsive state:
- `WindowWidth` / `WindowHeight` - Current window dimensions
- `IsCompact` / `IsMedium` / `IsExpanded` - Breakpoint flags
- `GridColumns` - Number of columns for grid layouts (1-3)
- `IsLogPanelVisible` - Whether to show the kernel log panel
- `ReducedMotion` - Accessibility preference for disabling animations

### 2. ResponsiveConverters.cs
**Location:** `src/Ui/SmartVoiceAgent.Ui/Converters/ResponsiveConverters.cs`

XAML converters for responsive bindings:
- `ResponsiveColumnConverter` - Returns column count from WindowStateManager
- `LogPanelVisibilityConverter` - Returns visibility boolean
- `LogPanelWidthConverter` - Returns GridLength for log panel
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
- Log panel width now adapts: 0px (compact), 320px (medium), 450px (expanded)
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
   - < 1024px: Single column, no log panel
   - 1024-1440px: Two columns, 320px log panel
   - > 1440px: Three columns, 450px log panel

2. **Toggle Reduced Motion** in Settings to see animations disable

3. **Minimum size**: Window can now resize down to 800x600

## Future Improvements

- Add touch-friendly button sizes for tablet mode
- Implement collapsible navigation sidebar for compact mode
- Add font scaling based on OS accessibility settings
- Consider adding a hamburger menu for navigation on small screens
