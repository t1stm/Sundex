using Sundex.Engine.AssetManagement.Abstract.Loading;
using Sundex.Engine.AssetManagement.Types.Asset;

namespace Sundex.Engine.AssetManagement.Types.Cache;

public class CachedAsset : ILoadableAsset<CachedAsset, CachedInfo>
{
    public AssetStream AssetStream { get; set; } = new();
    public static IAssetLoader<CachedAsset, CachedInfo> AssetLoader { get; } = new CachedAssetLoader();
}