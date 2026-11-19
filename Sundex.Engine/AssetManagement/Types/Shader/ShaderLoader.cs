using Sundex.Engine.AssetManagement.Abstract.Loading;
using Sundex.Engine.AssetManagement.Types.Asset;

namespace Sundex.Engine.AssetManagement.Types.Shader;

public class ShaderLoader : IAssetLoader<ShaderSource, ShaderInfo>
{
    public bool Query(ShaderInfo createInfo, AssetProvider assetProvider)
    {
        return assetProvider.Query<AssetStream, AssetInfo>(createInfo.AssetInfo);
    }

    public ShaderSource Load(ShaderInfo info, AssetProvider assetProvider)
    {
        return Load(info, assetProvider, Create);
    }

    public ShaderSource Load(ShaderInfo info, AssetProvider assetProvider,
        Func<ShaderInfo, AssetProvider, ShaderSource> create)
    {
        return create(info, assetProvider);
    }

    public static ShaderSource Create(ShaderInfo info, AssetProvider assetProvider)
    {
        var asset = assetProvider.Load<AssetStream, AssetInfo>(info.AssetInfo);

        using var streamReader = new StreamReader(asset.Stream);
        var shaderSourceString = streamReader.ReadToEnd();

        return new ShaderSource
        {
            Info = info,
            SourceCode = shaderSourceString,
            Type = info.Type
        };
    }
}