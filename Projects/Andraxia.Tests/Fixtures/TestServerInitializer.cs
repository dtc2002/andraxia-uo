using System.Reflection;
using System.Threading;
using Server;

namespace Andraxia.Tests;

/// <summary>
/// Minimal, process-wide ModernUO bootstrap for Andraxia tests.
/// </summary>
internal static class TestServerInitializer
{
    private static bool _initialized;
    private static readonly Lock _lock = new();

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }

            Core.ApplicationAssembly = Assembly.GetExecutingAssembly();
            Core.Assembly = typeof(Core).Assembly;
            Core.LoopContext = new EventLoopContext();
            Core.Expansion = Expansion.EJ;

            ServerConfiguration.Load(true);
            ServerConfiguration.AssemblyDirectories.Add(Core.BaseDirectory);
            AssemblyHandler.LoadAssemblies(["UOContent.dll", "Andraxia.dll"]);

            Server.Timer.Init(0);

            _initialized = true;
        }
    }
}
