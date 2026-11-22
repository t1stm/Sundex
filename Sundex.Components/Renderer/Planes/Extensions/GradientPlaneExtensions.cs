using Sundex.Core.Renderer.Planes;
using Sundex.Style.DSL.Abstract.Values.Keywords;

namespace Sundex.Components.Renderer.Planes.Extensions;

public static class GradientPlaneExtensions
{
    public static GradientPlane GenerateGradientPlane(this GradientValue gradient)
    {
        return new GradientPlane
        {
            GradientType = gradient.Type switch
            {
                "radial" => GradientPlane.Type.Radial,
                "conical" => GradientPlane.Type.Conical,
                "solid" => GradientPlane.Type.Solid,
                _ => GradientPlane.Type.Linear // also matches linear
            },
            GradientStops = [.. gradient.Stops.Select(stop => stop.Percentage)],
            GradientColors = [.. gradient.Stops.Select(stop => stop.Color.Vector)]
        };
    }
}