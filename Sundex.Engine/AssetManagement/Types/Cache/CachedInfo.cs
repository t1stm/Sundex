using Sundex.Engine.AssetManagement.Abstract.Loading;

namespace Sundex.Engine.AssetManagement.Types.Cache;

public class CachedInfo : ILoaderInfo
{
    public string CacheID { get; set; } = string.Empty;
}