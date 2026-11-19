using Sundex.Engine.AssetManagement.Abstract.Loading;

namespace Sundex.Engine.AssetManagement.Types.String;

public class StringAsset : ILoadableAsset<StringAsset, StringInfo>
{
    public string Value { get; set; } = string.Empty;

    public static IAssetLoader<StringAsset, StringInfo> AssetLoader { get; } = new StringAssetLoader();
}