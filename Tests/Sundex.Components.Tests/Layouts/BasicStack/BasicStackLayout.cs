using Sundex.Components.Abstractions;
using Sundex.Engine.AssetManagement.Extensions;
using Sundex.Engine.AssetManagement.Types.Asset;
using Sundex.Markup;

namespace Sundex.Components.Tests.Layouts.BasicStack;

public static class BasicStackLayout
{
    public static SundexComponent Create(UIContext uiContext)
    {
        var markupContext = new SundexContext(uiContext);
        var asset = uiContext.AssetProvider.LoadString("Layouts/BasicStack/BasicStack.xml", StorageLocation.Assembly);
        return markupContext.NewComponent(asset);
    }
}