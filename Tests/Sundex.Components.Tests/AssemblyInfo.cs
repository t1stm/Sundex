using System.Runtime.CompilerServices;
using Sundex.Engine.AssetManagement;
using Sundex.Markup.Legacy;

[assembly: SundexAssets]

// UIContext's mock providers are shared between tests in the process.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Sundex.Components.Tests;

internal static class LegacyMarkupSetup
{
    [ModuleInitializer]
    internal static void Register() => CSharp.Register();
}
