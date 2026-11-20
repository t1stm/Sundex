using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace Sundex.Core.Renderer.Planes.Uniforms;

[StructLayout(LayoutKind.Explicit, Size = 352)]
public struct GradientUniform
{
    [FieldOffset(0)] public Matrix4 Model;
    [FieldOffset(64)] public Vector4 ScaleAndBorderPx;
    [FieldOffset(80)] public GradientType GradientType;
    [FieldOffset(84)] public int GradientStopCount;

    [FieldOffset(96)] public Vector4 Color0;
    [FieldOffset(112)] public Vector4 Color1;
    [FieldOffset(128)] public Vector4 Color2;
    [FieldOffset(144)] public Vector4 Color3;
    [FieldOffset(160)] public Vector4 Color4;
    [FieldOffset(176)] public Vector4 Color5;
    [FieldOffset(192)] public Vector4 Color6;
    [FieldOffset(208)] public Vector4 Color7;

    [FieldOffset(224)] public float Stop0;
    [FieldOffset(240)] public float Stop1;
    [FieldOffset(256)] public float Stop2;
    [FieldOffset(272)] public float Stop3;
    [FieldOffset(288)] public float Stop4;
    [FieldOffset(304)] public float Stop5;
    [FieldOffset(320)] public float Stop6;
    [FieldOffset(336)] public float Stop7;
}