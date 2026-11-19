namespace Sundex.Engine.AssetManagement;

/// <summary>Registers an assembly's embedded assets and graphics preloads with Sundex.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SundexAssetsAttribute : Attribute;
