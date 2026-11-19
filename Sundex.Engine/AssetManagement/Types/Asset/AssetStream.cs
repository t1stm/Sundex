using Sundex.Engine.AssetManagement.Abstract.Loading;

namespace Sundex.Engine.AssetManagement.Types.Asset;

public class AssetStream : ILoadableAsset<AssetStream, AssetInfo>
{
    public AssetInfo Info { get; set; } = new();
    public Stream Stream { get; set; } = Stream.Null;
    public static IAssetLoader<AssetStream, AssetInfo> AssetLoader { get; } = new AssetLoader();
}