using Sundex.Engine.AssetManagement.Abstract.Metadata;

namespace Sundex.Engine.AssetManagement.Types.Asset;

public readonly struct AssetMetadata : IAssetMetadata<AssetMetadata, AssetInfo>
{
    public required DateTime ModifiedDate { get; init; }
    public required bool Found { get; init; }

    public static IMetadataLoader<AssetMetadata, AssetInfo> MetadataProvider { get; } = new AssetLoader();
}