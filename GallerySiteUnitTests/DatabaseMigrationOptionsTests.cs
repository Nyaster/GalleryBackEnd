using GallerySiteBackend.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GallerySiteUnitTests;

public sealed class DatabaseMigrationOptionsTests
{
    [Fact]
    public void Default_UsesTenMinutes() => Assert.Equal(600, new DatabaseMigrationOptions().CommandTimeoutSeconds);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidTimeout_StartupAndDirectResolutionRejectConfiguration(int timeout)
    {
        var collection = new ServiceCollection();
        collection.AddOptions<DatabaseMigrationOptions>()
            .Configure(options => options.CommandTimeoutSeconds = timeout)
            .ValidateDataAnnotations().ValidateOnStart();
        using var services = collection.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<DatabaseMigrationOptions>>().Value);
    }
}
