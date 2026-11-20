using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Attributes;

namespace Sundex.Engine.Renderer.Buffers;

[PreloadGraphicsContext]
public sealed class UnitQuad : IGamePreloadable
{
    public const int IndexCount = 6;
    public static GpuBuffer<float> Positions { get; private set; } = null!;
    public static GpuBuffer<float> PositionsWithUV { get; private set; } = null!;
    public static GpuBuffer<int> Indices { get; private set; } = null!;

    public static void Preload(AssetProvider assetProvider)
    {
        Positions = new(BufferUsage.Vertex);
        Positions.SetData([0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0, 0]);
        PositionsWithUV = new(BufferUsage.Vertex);
        PositionsWithUV.SetData([0, 1, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0]);
        Indices = new(BufferUsage.Index);
        Indices.SetData([0, 1, 3, 1, 2, 3]);
    }
}
