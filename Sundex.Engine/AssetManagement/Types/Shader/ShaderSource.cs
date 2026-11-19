using OpenTK.Graphics.OpenGL;
using Sundex.Engine.AssetManagement.Abstract.Loading;

namespace Sundex.Engine.AssetManagement.Types.Shader;

public class ShaderSource : ILoadableAsset<ShaderSource, ShaderInfo>
{
    public ShaderInfo Info { get; set; } = new();
    public ShaderType Type { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public static IAssetLoader<ShaderSource, ShaderInfo> AssetLoader { get; } = new ShaderLoader();
}