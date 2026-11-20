using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace Sundex.Core.Renderer.Planes.Uniforms;

[StructLayout(LayoutKind.Explicit, Size = 96)]
public struct ColoredUniform
{
    [FieldOffset(0)] public Matrix4 Model;
    [FieldOffset(64)] public Vector4 Color;
    [FieldOffset(80)] public Vector4 ScaleAndBorderPx;
}