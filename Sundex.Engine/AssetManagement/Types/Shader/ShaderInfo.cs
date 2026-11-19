using OpenTK.Graphics.OpenGL;
using Sundex.Engine.AssetManagement.Abstract.Loading;
using Sundex.Engine.AssetManagement.Types.Asset;

namespace Sundex.Engine.AssetManagement.Types.Shader;

public class ShaderInfo : ILoaderInfo
{
    public AssetInfo AssetInfo { get; set; } = new();
    public ShaderType Type { get; set; }

    public static ShaderInfo CreateFromUnknownStorage(ShaderType type, string location)
    {
        return new ShaderInfo
            { Type = type, AssetInfo = new AssetInfo { Location = location, Storage = StorageLocation.Unknown } };
    }
}