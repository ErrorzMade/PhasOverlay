using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PhasOverlay
{
    /// <summary>
    /// The styled possession icons, each rendered once per size into a frozen bitmap so no marker
    /// carries live effects.
    /// </summary>
    public static class PossessionIcons
    {
        /// <summary>Space around the glyph for the glow and shadow, on every side.</summary>
        public const double Pad = 8;

        public const double MapGlyph = 30;
        public const double KeyGlyph = 24;

        // Used only when maps.json carries no possessions table.
        private static readonly PossessionDto[] BuiltIn =
        {
            new() { Item = "Ouija Board", Icon = "Images/Icons/Possessions/ouija-board.png" },
            new() { Item = "Tarot Cards", Icon = "Images/Icons/Possessions/tarot-cards.png" },
            new() { Item = "Voodoo Doll", Icon = "Images/Icons/Possessions/voodoo-doll.png" },
            new() { Item = "Music Box", Icon = "Images/Icons/Possessions/music-box.png" },
            new() { Item = "Haunted Mirror", Icon = "Images/Icons/Possessions/haunted-mirror.png" },
            new() { Item = "Monkey Paw", Icon = "Images/Icons/Possessions/monkey-paw.png" },
            new() { Item = "Summoning Circle", Icon = "Images/Icons/Possessions/summoning-circle.png" }
        };

        private static IReadOnlyList<PossessionDto> Table
        {
            get
            {
                var fromData = MapDataService.GetPossessions();
                return fromData.Count > 0 ? fromData : BuiltIn;
            }
        }

        /// <summary>Every possession in the table, in key order.</summary>
        public static IEnumerable<string> Items
        {
            get
            {
                foreach (var p in Table)
                {
                    if (!string.IsNullOrWhiteSpace(p.Item)) yield return p.Item.Trim();
                }
            }
        }

        // Above 96 DPI so the bitmap stays sharp on a scaled display.
        private const double RenderScale = 2;

        private static readonly Dictionary<(string, double), ImageSource?> Cache = new();

        private static readonly Brush Outline = Frozen(Color.FromRgb(0x0B, 0x0B, 0x0D));
        private static readonly Color Glow = Color.FromRgb(0xB4, 0x55, 0xFF);

        /// <summary>The styled icon at <paramref name="glyph"/> DIP plus <see cref="Pad"/> each side, or null when the item has none.</summary>
        public static ImageSource? For(string item, double glyph)
        {
            string name = (item ?? "").Trim();
            string? relative = null;
            foreach (var p in Table)
            {
                if (string.Equals(p.Item?.Trim(), name, StringComparison.OrdinalIgnoreCase)) { relative = p.Icon; break; }
            }
            if (string.IsNullOrWhiteSpace(relative)) return null;

            // New data is only ever applied on the next launch, so the path alone is a safe key.
            var key = (relative, glyph);
            if (!Cache.TryGetValue(key, out var icon))
            {
                icon = Render(relative, glyph);
                Cache[key] = icon;
            }
            return icon;
        }

        /// <summary>The on-screen width of an icon from <see cref="For"/>, glow included.</summary>
        public static double Extent(double glyph) => glyph + Pad * 2;

        /// <summary>Negative margin so an icon wider than its slot is not cropped from the top left by WPF.</summary>
        public static Thickness Overhang(double glyph, double slot) => new(-(Extent(glyph) - slot) / 2);

        private static ImageSource? Render(string relative, double glyph)
        {
            var mask = MapDataService.LoadImage(MapDataService.Resolve(relative));
            if (mask == null) return null;

            var fill = Application.Current?.TryFindResource("PossessionBrush") as Brush
                       ?? Frozen(Color.FromRgb(0xC2, 0x7B, 0xFF));

            double size = Extent(glyph);

            // The outline must stay dark: it also fills the glyph's gaps, and a light one turns the icon into a blob.
            var shape = new Grid { Width = size, Height = size };
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx != 0 || dy != 0) shape.Children.Add(Glyph(mask, Outline, glyph, dx, dy));
                }
            }
            shape.Children.Add(Glyph(mask, fill, glyph, 0, 0));

            // One Effect per element, so the glow and the drop shadow nest.
            shape.Effect = new DropShadowEffect { Color = Glow, ShadowDepth = 0, Opacity = 0.55, BlurRadius = 8 };
            var root = new Grid { Width = size, Height = size };
            root.Children.Add(shape);
            root.Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 2, Direction = 270, Opacity = 0.8, BlurRadius = 6 };

            root.Measure(new Size(size, size));
            root.Arrange(new Rect(0, 0, size, size));
            root.UpdateLayout();

            int px = (int)Math.Ceiling(size * RenderScale);
            var bitmap = new RenderTargetBitmap(px, px, 96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            bitmap.Freeze();
            return bitmap;
        }

        // Only the file's alpha is used; the colour comes from the brush.
        private static Rectangle Glyph(BitmapSource mask, Brush fill, double glyph, double dx, double dy)
        {
            var brush = new ImageBrush(mask) { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();

            var rect = new Rectangle
            {
                Width = glyph,
                Height = glyph,
                Fill = fill,
                OpacityMask = brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(dx, dy)
            };
            RenderOptions.SetBitmapScalingMode(rect, BitmapScalingMode.HighQuality);
            return rect;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
