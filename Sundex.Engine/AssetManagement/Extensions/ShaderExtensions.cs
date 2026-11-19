using Sundex.Engine.AssetManagement.Types.Shader;

namespace Sundex.Engine.AssetManagement.Extensions;

public static class ShaderExtensions
{
    public static ShaderSource[] LoadShaders(this AssetProvider assetProvider, params ReadOnlySpan<ShaderInfo> infos)
    {
        var array = new ShaderSource[infos.Length];
        assetProvider.Load(array, infos);
        return array;
    }
}