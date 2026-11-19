using Sundex.Components.Abstractions;
using Sundex.Engine.AssetManagement.Extensions;
using Sundex.Engine.AssetManagement.Types.Asset;
using Sundex.Markup;

namespace Sundex.Components.Tests.Layouts.NestedFlex;

public static class NestedFlexLayout
{
    public static SundexComponent Create(UIContext uiContext)
    {
        var markupContext = new SundexContext(uiContext);
        var asset = uiContext.AssetProvider.LoadString("Layouts/NestedFlex/NestedFlex.xml", StorageLocation.Assembly);
        return markupContext.NewComponent(asset);
    }
}