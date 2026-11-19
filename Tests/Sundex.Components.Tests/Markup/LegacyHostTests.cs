using Sundex.Components.Abstractions;
using Sundex.Components.Labels;
using Sundex.Components.Panels;
using Sundex.Markup;
using Sundex.Reactive;

namespace Sundex.Components.Tests.Markup;

/// <summary>
///     A version 1.0 document hosting a 2.0 component through an element factory, as an app moving
///     its documents to 2.0 one at a time does: the 2.0 component keeps its own sheet, sits in the
///     host's layout, and ends its bindings when the host's tree is disposed.
/// </summary>
public class LegacyHostTests
{
    // The host's sheet sets what the component's sheet also sets (`count`) and what every label
    // takes (`label`): neither may reach the component, as a 2.0 host's sheet never does.
    private const string Host = """
                                <sundex version="1.0">
                                  <style language="sundex">
                                    component label {
                                      font-size = 11;
                                    }

                                    class count {
                                      font-size = 99;
                                    }
                                  </style>

                                  <layout>
                                    <flex id="host" direction="vertical" width="400" height="300">
                                      <label id="host-label" value="host"/>
                                      <counter id="hosted" width="200"/>
                                    </flex>
                                  </layout>
                                </sundex>
                                """;

    private static (SundexContext<TestAppContext> Sundex, TestAppContext App, SundexComponent Host) Build()
    {
        var app = new TestAppContext();
        var sundex = new SundexContext<TestAppContext>(new TestUIContext(), app);
        sundex.RegisterElementFactory(Counter.MarkupTag, _ => new Counter(sundex).Create());
        return (sundex, app, sundex.NewComponent(Host));
    }

    private static IEnumerable<UIElement> All(UIElement root)
    {
        yield return root;
        if (root is not Panel panel) yield break;
        foreach (var child in panel.Children)
        foreach (var descendant in All(child))
            yield return descendant;
    }

    private static Label Count(UIElement root) => All(root).OfType<Label>().Single(label => label.Classes.Contains("count"));

    private static void Click(UIElement root)
    {
        var button = All(root).OfType<Button>().Single();
        button.OnClick!(button);
        Rx.Flush();
    }

    [Fact]
    public void TheComponentKeepsItsOwnSheet_AndTheHostsStylesItsOwnElements()
    {
        var (_, _, host) = Build();
        var hosted = host.GetID<UIElement>("hosted");

        Assert.Equal(11, host.GetID<Label>("host-label").FontSizePx.Value);
        Assert.Equal(20, Count(hosted).FontSizePx.Value);

        // A class binding restyles from the sheet the element keeps: the component's.
        Click(hosted);
        Assert.Equal(30, Count(hosted).FontSizePx.Value);
        Click(hosted);
        Assert.Equal(20, Count(hosted).FontSizePx.Value);
    }

    [Fact]
    public void ReloadingTheHostsSheetLeavesTheComponentAlone()
    {
        var (_, _, host) = Build();
        var hosted = host.GetID<UIElement>("hosted");

        host.ReloadStyleSheet();

        Assert.Equal(11, host.GetID<Label>("host-label").FontSizePx.Value);
        Assert.Equal(20, Count(hosted).FontSizePx.Value);
        Click(hosted);
        Assert.Equal(30, Count(hosted).FontSizePx.Value);
    }

    [Fact]
    public void TheComponentSitsInTheHostsLayout_WithTheUseSitesAttributes()
    {
        var (_, _, host) = Build();
        var root = (FlexPanel)host.Element;
        var hosted = host.GetID<UIElement>("hosted");

        Assert.Same(root, hosted.Parent);
        Assert.Same(root.Children[1], hosted);
        Assert.Contains("counter", hosted.Classes);

        root.Layout();
        Assert.Equal(200, hosted.Computed.Width);
        Assert.True(hosted.Computed.Y >= host.GetID<Label>("host-label").Computed.Height);
    }

    [Fact]
    public void DisposingTheHostsTreeEndsTheComponentsBindings()
    {
        var (_, app, host) = Build();
        var hosted = host.GetID<UIElement>("hosted");
        var greeting = All(hosted).OfType<Label>().Single(label => label.Classes.Contains("greeting"));
        Assert.NotEmpty(app.Greeting.Observers);

        host.Element.Dispose();
        app.Greeting.Value = "after";
        Rx.Flush();

        Assert.True(hosted.IsDisposed);
        Assert.Equal("hello", greeting.Value.ToString());
        Assert.Empty(app.Greeting.Observers);
    }
}
