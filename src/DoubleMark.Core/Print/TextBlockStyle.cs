namespace DoubleMark.Core.Print;

/// <summary>Block orientation on the label.</summary>
public enum TextBlockLayout
{
    Horizontal = 0,
    Vertical = 1
}

/// <summary>Text direction inside the block: which way it reads / faces.</summary>
public enum TextFlowDirection
{
    Right = 0,
    Left = 1,
    Up = 2,
    Down = 3
}

/// <summary>Legacy combined direction stored in old templates.</summary>
public enum TextBlockDirection
{
    LeftToRight = 0,
    TopToBottom = 1,
    RightToLeft = 2,
    BottomToTop = 3
}

public static class TextBlockStyleHelper
{
    public static (TextBlockLayout Layout, TextFlowDirection Flow) GetStyle(
        TextBlockLayout? layout,
        TextFlowDirection? flow,
        TextBlockDirection? legacyOrientation = null)
    {
        if (layout is { } l && flow is { } f)
            return (l, f);

        if (legacyOrientation is { } legacy)
            return FromLegacy(legacy);

        return (TextBlockLayout.Horizontal, TextFlowDirection.Right);
    }

    public static (TextBlockLayout Layout, TextFlowDirection Flow) FromLegacy(TextBlockDirection legacy) =>
        legacy switch
        {
            TextBlockDirection.TopToBottom => (TextBlockLayout.Vertical, TextFlowDirection.Down),
            TextBlockDirection.RightToLeft => (TextBlockLayout.Horizontal, TextFlowDirection.Left),
            TextBlockDirection.BottomToTop => (TextBlockLayout.Vertical, TextFlowDirection.Up),
            _ => (TextBlockLayout.Horizontal, TextFlowDirection.Right)
        };

    public static TextBlockDirection ToLegacy(TextBlockLayout layout, TextFlowDirection flow)
    {
        if (layout == TextBlockLayout.Horizontal)
        {
            return flow switch
            {
                TextFlowDirection.Left => TextBlockDirection.RightToLeft,
                TextFlowDirection.Up => TextBlockDirection.BottomToTop,
                TextFlowDirection.Down => TextBlockDirection.TopToBottom,
                _ => TextBlockDirection.LeftToRight
            };
        }

        return flow switch
        {
            TextFlowDirection.Up => TextBlockDirection.BottomToTop,
            TextFlowDirection.Right => TextBlockDirection.BottomToTop,
            TextFlowDirection.Left => TextBlockDirection.TopToBottom,
            _ => TextBlockDirection.TopToBottom
        };
    }

    public static TextBlockLayout ToggleLayout(TextBlockLayout layout, TextFlowDirection flow) =>
        layout == TextBlockLayout.Horizontal ? TextBlockLayout.Vertical : TextBlockLayout.Horizontal;

    public static TextFlowDirection DefaultFlowForLayout(TextBlockLayout layout) =>
        layout == TextBlockLayout.Vertical ? TextFlowDirection.Down : TextFlowDirection.Right;

    public static bool UsesVerticalGlyphRun(TextBlockLayout layout, TextFlowDirection flow) =>
        layout == TextBlockLayout.Vertical || flow is TextFlowDirection.Up or TextFlowDirection.Down;
}
