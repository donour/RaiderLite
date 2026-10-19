namespace RaiderLite;

/// <summary>A set of table colours that the Settings dialog can apply in one go.</summary>
public sealed record ColorPreset(Color Min, Color Max, Color Selection, Color Increased, Color Decreased, Color Axis, Color Warning)
{
    /// <summary>RomRaider's colours.</summary>
    public static ColorPreset Default { get; } = new(
        Min: Color.FromArgb(153, 153, 255),
        Max: Color.FromArgb(255, 102, 102),
        Selection: Color.FromArgb(27, 161, 226),
        Increased: Color.FromArgb(255, 0, 0),
        Decreased: Color.FromArgb(0, 0, 255),
        Axis: Color.FromArgb(255, 255, 255),
        Warning: Color.FromArgb(255, 0, 0));

    /// <summary>
    /// Colours told apart with protanopia, deuteranopia or tritanopia. The default min-to-max blend
    /// passes through green, so with red-green colour blindness much of a table looks alike; this
    /// one runs from light orange to dark red (ColorBrewer OrRd), changing in lightness throughout,
    /// so it reads even without colour. The others are from the Okabe-Ito palette, chosen to
    /// stand apart from each other, the white axis cells and every shade of the blend.
    /// </summary>
    public static ColorPreset ColorBlind { get; } = new(
        Min: Color.FromArgb(0xFD, 0xBB, 0x84),
        Max: Color.FromArgb(0x7F, 0x00, 0x00),
        Selection: Color.FromArgb(0x00, 0x72, 0xB2),   // blue: white text on it is 5:1
        Increased: Color.FromArgb(0xF0, 0xE4, 0x42),   // yellow
        Decreased: Color.FromArgb(0x00, 0x00, 0x00),   // black
        Axis: Color.FromArgb(255, 255, 255),
        Warning: Color.FromArgb(0x56, 0xB4, 0xE9));    // sky blue
}
