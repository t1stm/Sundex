using Sundex.Engine.Renderer.Shaders;
using Sundex.Engine.AssetManagement.Abstract.Loading;
using Sundex.Engine.AssetManagement.Types.Asset;

namespace Sundex.Engine.AssetManagement.Types.Shader;

public class ShaderInfo : ILoaderInfo
{
    public AssetInfo AssetInfo { get; set; } = new();
    public ShaderStage Type { get; set; }

    public static ShaderInfo CreateFromUnknownStorage(ShaderStage type, string location)
    {
        return new ShaderInfo
            { Type = type, AssetInfo = new AssetInfo { Location = location, Storage = StorageLocation.Unknown } };
    }
}