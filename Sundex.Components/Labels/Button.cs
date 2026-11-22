using Sundex.Components.Abstractions;
using Sundex.Components.Abstractions.Values;
using Sundex.Components.Attributes;
using Sundex.Components.Panels;

namespace Sundex.Components.Labels;

[MarkupTag("button")]
public class Button : FlexPanel
{
    /// <param name="context">The UI context the button belongs to.</param>
    /// <param name="label">The button's text.</param>
    /// <param name="background">The plane drawn behind it; null for none.</param>
    /// <param name="font">The label's font; null for the interface font. See <see cref="Label" />.</param>
    public Button(UIContext context, string label, Renderable? background = null, string? font = null) : this(
        context, new Label(context, label, font), background)
    {
    }

    /// <summary>
    ///     A button with no label of its own, for markup: its children are whatever the markup puts
    ///     in it. <see cref="Label" /> is a detached placeholder until the first label the markup puts
    ///     in it, which takes its place (see <see cref="AddChild" />).
    /// </summary>
    public Button(UIContext context) : base(context)
    {
        SetInitialLayout(LayoutDirection.Vertical);
        PaddingTop = PaddingRight = PaddingBottom = PaddingLeft = 5;
        HorizontalAlign = Align.Center;
        VerticalAlign = Align.Center;
        Label = new Label(context);
        _adoptsLabel = true;
        UpdateCursorOnHover = true;
        Focusable = true;
    }

    public Button(UIContext context, Label label, Renderable? background = null) : base(context)
    {
        SetInitialLayout(LayoutDirection.Vertical);
        PaddingTop = PaddingRight = PaddingBottom = PaddingLeft = 5;
        HorizontalAlign = Align.Center;
        VerticalAlign = Align.Center;
        Background = background;

        Children = [Label = label];
        UpdateCursorOnHover = true;
        Focusable = true;
    }


    public override string Tag => "button";

    private bool _adoptsLabel;

    /// <summary>
    ///     The first label added to a button built for markup becomes its <see cref="Label" />, as
    ///     if the button had been built around it: <see cref="Value" /> and <see cref="FontSizePx" />
    ///     reach it. Markup styles the button before its label exists, so a font size the button's
    ///     rules set moves over to the label, unless the label's own rules set one.
    /// </summary>
    public override void AddChild(UIElement child)
    {
        if (_adoptsLabel && child is Label label)
        {
            _adoptsLabel = false;
            if (IsStyled(nameof(FontSizePx)) && !label.IsStyled(nameof(Label.FontSizePx))) label.FontSizePx = FontSizePx;
            Label = label;
        }

        base.AddChild(child);
    }

    /// <summary>Enter and Space press a focused button, as in a browser.</summary>
    public override bool HandleKeyDown(OpenTK.Windowing.Common.KeyboardKeyEventArgs args)
    {
        if (Disabled || args.IsRepeat ||
            args.Key is not (OpenTK.Windowing.GraphicsLibraryFramework.Keys.Enter
                or OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadEnter
                or OpenTK.Windowing.GraphicsLibraryFramework.Keys.Space))
            return false;
        OnClick?.Invoke(this);
        return true;
    }
    public Label Label { get; set; }

    [NamedSetting("text-value")]
    public ReadOnlySpan<char> Value
    {
        get => Label.Value;
        set => Label.Value = value;
    }

    [NamedSetting("font-size")]
    public LiteralOrComputable FontSizePx
    {
        get => Label.FontSizePx;
        set => Label.FontSizePx = value;
    }

}