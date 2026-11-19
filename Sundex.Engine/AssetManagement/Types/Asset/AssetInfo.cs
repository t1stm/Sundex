using System.Text.Json;
using Sundex.Engine.AssetManagement.Abstract.Loading;
using Sundex.Engine.Common;

namespace Sundex.Engine.AssetManagement.Types.Asset;

public class AssetInfo : ILoaderInfo
{
    public string Location { get; set; } = string.Empty;
    public StorageLocation Storage { get; set; } = StorageLocation.Unknown;

    public override string ToString()
    {
        return JsonSerializer.Serialize(this, SerializerOptions.Json);
    }
}