using System.Runtime.CompilerServices;
using Companion.Core;

namespace Companion.Tests;

internal static class TestSetup
{
    /// <summary>Runs once when the test assembly loads: no test may write into %APPDATA%\Companion\logs.</summary>
    [ModuleInitializer]
    internal static void Init() => Log.Enabled = false;
}
