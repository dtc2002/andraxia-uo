using Xunit;

namespace Andraxia.Tests;

[CollectionDefinition("Sequential Andraxia Tests", DisableParallelization = true)]
public class AndraxiaFixture : ICollectionFixture<AndraxiaFixture>
{
    public AndraxiaFixture() => TestServerInitializer.Initialize();
}
