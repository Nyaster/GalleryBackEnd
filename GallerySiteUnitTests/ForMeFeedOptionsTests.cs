using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Configuration;

namespace GallerySiteUnitTests;

public sealed class ForMeFeedOptionsTests
{
    [Fact]
    public void Startup_NoOverride_UsesTwentyFiveReferences()
    {
        using var services = Configure(null);
        services.GetRequiredService<IStartupValidator>().Validate();
        Assert.Equal(25, services.GetRequiredService<IOptions<ForMeFeedOptions>>().Value.RepresentativeCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Startup_OutOfRangeOverride_RejectsConfiguration(int count)
    {
        using var services = Configure(count);
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    private static ServiceProvider Configure(int? count)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(count is null ? [] :
            new Dictionary<string, string?> { ["ForMeFeed:RepresentativeCount"] = count.Value.ToString() }).Build();
        var services = new ServiceCollection();
        services.AddOptions<ForMeFeedOptions>().Bind(configuration.GetSection("ForMeFeed"))
            .ValidateDataAnnotations().ValidateOnStart();
        return services.BuildServiceProvider();
    }
}
