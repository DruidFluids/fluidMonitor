using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fluid.App;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        BuildContent();
    }

    private void BuildContent()
    {
        var categories = new (string Title, (string Name, string Desc)[] Items)[]
        {
            ("Tiles", new[]
            {
                ("Tile toggles", "Show or hide individual tiles (CPU, GPU, RAM, Network, Storage, Clock). Drag tiles to reorder them on the widget."),
                ("Tile labels", "Choose between Auto (hardware name detected automatically) or Custom (type your own label). Applies to CPU and GPU tiles."),
                ("Tile width / height", "Resize all tiles uniformly. The widget expands or shrinks to fit. Useful for matching your desktop layout or fitting more info."),
                ("CPU temperature", "Requires a one-time sensor driver install (PawnIO). Once installed, the CPU tile shows temperature alongside load. Use the (i) icon below Tiles to set up or the °C/°F rocker to switch units."),
            }),
            ("Layout", new[]
            {
                ("Horizontal / Vertical", "Switch the widget between a tall vertical stack or a wide horizontal bar. The tile order is preserved in both modes."),
                ("UI scale", "Scale the entire widget up or down. Useful for high-DPI displays or if you want a more compact look."),
            }),
            ("Appearance", new[]
            {
                ("Preset Themes", "One-click theme + skin + color combos. Browse the full library with the folder icon, or roll the dice for a random pick. Undo reverts to your previous look."),
                ("Skins", "Visual templates that control the widget's shape, borders, and tile style. Each skin can define its own font, corner radius, and opacity. Browse or randomize."),
                ("Colors", "The five-color palette: Background, Tile, Accent, Text, and Muted. Click any swatch to open the color picker. Save custom palettes with the + button."),
                ("Dark / Light mode", "Toggle between dark and light default palettes. Light mode forces full opacity so the widget is readable over any wallpaper."),
                ("Muted text visibility", "Controls how visible muted/secondary text is. Higher values blend the muted color toward the main text color for better contrast."),
                ("Color swatches", "Click any swatch to open the color picker. The active swatch has a bright border. Hex values are shown below each swatch."),
                ("Saved Themes (1–5)", "Five preset slots to save and recall your favorite theme + skin + color combinations. Click the pen icon to save, click a number to load."),
                ("Dice button", "Randomizes your appearance. Left-click rolls skin + colors. The undo button (↶) appears to step back through changes."),
            }),
            ("Fonts", new[]
            {
                ("Sync fonts", "When on, changing the Primary font automatically applies to Secondary and Indicator fonts too. Turn off to set each independently."),
                ("Primary / Secondary / Indicator", "Primary: main value numbers. Secondary: labels and subtitles. Indicator: unit text (GB, MHz, B/s). Each has its own size slider."),
                ("Font size sliders", "Adjust the relative size of each font type. The thin tick mark on each slider shows the default (0pt offset)."),
                ("Allow random fonts with dice", "When on, the dice button also randomizes fonts. When off, dice only changes skin and colors — your fonts stay locked."),
            }),
            ("Behavior", new[]
            {
                ("Always on top", "Keeps the widget above all other windows. Turn off if you want it to go behind fullscreen apps."),
                ("Click-through", "Makes the widget transparent to mouse clicks — clicks pass through to whatever is behind it. Use the hotkey to toggle back."),
                ("Click-through hotkey", "Set a keyboard shortcut to toggle click-through mode on and off. Click the box, then press your key combo."),
                ("Snap to edges", "When you drag the widget near a screen edge, it snaps flush. Works with all monitor edges."),
                ("Snap to windows", "When snap is on, the widget also docks to other windows' edges. Corners align automatically. Manage exceptions in Utilities > Blocklist."),
                ("Run at Windows startup", "Launches the widget automatically when you sign in. Uses a per-user registry key — no admin rights needed."),
                ("Opacity", "Controls the widget's transparency. 100% is fully opaque, lower values let your wallpaper show through. Light mode forces 100%."),
                ("Update interval", "How often the widget refreshes sensor data. Lower values = smoother updates but slightly more CPU. The service adjusts its polling rate to match."),
            }),
            ("Network", new[]
            {
                ("Traffic indicator", "Visual feedback for network activity. Cycle through Off, Blink, Fade, and Glow styles. The ↓↑ arrows preview the current style."),
                ("Monitor adapter", "Choose which network adapter to track. Defaults to all adapters combined. Pick a specific one if you only care about Ethernet or Wi-Fi."),
                ("Arrow spacing / size", "Fine-tune the ↓↑ arrow positioning and size on the Network tile. Spacing controls the gap between arrows and numbers."),
            }),
            ("Disk", new[]
            {
                ("Tile label", "Cycle between showing the drive letter (C:), the disk model name, or both on the Disk tile header."),
                ("Monitor disk", "Choose which physical disk to track. Shows the disk number and model name. Read/Write speeds update live."),
                ("R: / W: spacing / size", "Fine-tune the R: and W: label positioning and size on the Disk tile."),
            }),
            ("Remote Monitoring", new[]
            {
                ("Enable remote monitoring", "Start a TCP server so other fluidMonitor instances on your network can display this machine's stats as a remote tile."),
                ("Handshake key", "A shared secret used to authenticate remote connections. Both machines must use the same key. Regenerate if compromised."),
                ("Remote devices", "Add other machines running fluidMonitor to see their stats alongside yours. Enter their IP and handshake key."),
            }),
            ("Game Mode", new[]
            {
                ("Hotkey", "Press a key combo to instantly snap the widget to a corner of your primary monitor. Press again to return it. Works system-wide, even in fullscreen games."),
                ("Snap position", "Choose which corner or edge the widget snaps to in Game Mode (9 positions)."),
                ("Appearance when active", "Set a different opacity, orientation, and click-through behavior while Game Mode is active. Useful for making the widget smaller and unobtrusive during gaming."),
                ("Tiles when active", "Choose which tiles are visible during Game Mode. Hide Clock or Storage to save space while keeping CPU/GPU/RAM visible."),
            }),
            ("Warnings", new[]
            {
                ("Temperature threshold", "Set a temperature (in °C) that triggers a visual warning. When crossed, the tile background flashes the configured color."),
                ("Flash", "Enable a flashing background effect when the threshold is exceeded. Choose the flash color (default: red)."),
                ("Gradient mode", "Instead of a hard flash, smoothly shifts the unit text color from cool blue to hot red based on the current temperature."),
            }),
            ("Utilities", new[]
            {
                ("Chris Titus Win Utility", "Launches a popular Windows debloating and optimization tool. Runs with admin rights. Not affiliated with fluidMonitor."),
                ("Microsoft Activation Scripts", "Launches MAS for Windows/Office activation. Runs with admin rights. Not affiliated with fluidMonitor."),
                ("Window snap blocklist", "Windows whose titles match any line here won't be used as snap targets. Use 'Pick window' to select from currently open windows."),
            }),
        };

        foreach (var (catTitle, items) in categories)
        {
            // Category header
            var header = new TextBlock
            {
                Text = catTitle,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 16, 0, 6),
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            HelpContent.Children.Add(header);

            foreach (var (name, desc) in items)
            {
                var expander = new Expander
                {
                    Margin = new Thickness(0, 0, 0, 2),
                    IsExpanded = false,
                    BorderThickness = new Thickness(0),
                };
                expander.SetResourceReference(Expander.ForegroundProperty, "TextBrush");

                // Header: feature name
                expander.Header = new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    FontWeight = FontWeights.Normal,
                };

                // Content: description
                var descBlock = new TextBlock
                {
                    Text = desc,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 17,
                    Margin = new Thickness(16, 2, 0, 8),
                };
                descBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                expander.Content = descBlock;

                HelpContent.Children.Add(expander);
            }
        }

        // Footer
        var footer = new TextBlock
        {
            Text = "fluidMonitor — built with care",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 8),
        };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        HelpContent.Children.Add(footer);
    }
}
