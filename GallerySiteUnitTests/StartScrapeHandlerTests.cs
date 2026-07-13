using Application.Features.Administration.StartScrape;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.Extensions.Options;
using Moq;
using Service;
using Service.Contracts;
using System.Runtime.CompilerServices;

namespace GallerySiteUnitTests;

public sealed class StartScrapeHandlerTests
{
    [Fact]
    public async Task Handle_MissingCap_UsesConfiguredDefault()
    {
        var (handler, run) = CreateHandler(defaultImagesPerRun: 100);

        var result = await handler.Handle(new Command(ScrapeMode.Incremental), CancellationToken.None);

        Assert.Equal(100, result.MaxImages);
        Assert.NotNull(run.Value);
        Assert.Equal(100, run.Value!.MaxImages);
    }

    [Fact]
    public async Task Handle_ValidCap_PersistsRequestedCap()
    {
        var (handler, run) = CreateHandler();

        var result = await handler.Handle(new Command(ScrapeMode.Full, 500), CancellationToken.None);

        Assert.Equal(500, result.MaxImages);
        Assert.Equal(ScrapeMode.Full, run.Value!.Mode);
        Assert.Equal(500, run.Value.MaxImages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task Handle_InvalidCap_ThrowsBadRequestWithoutQueueingRun(int maxImages)
    {
        var (handler, run) = CreateHandler();

        await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(
            new Command(ScrapeMode.Incremental, maxImages), CancellationToken.None));

        Assert.Null(run.Value);
    }

    private static (Handler Handler, StrongBox<ScrapeRun?> Run) CreateHandler(int defaultImagesPerRun = 100)
    {
        var run = new StrongBox<ScrapeRun?>();
        var repositories = new Mock<IRepositoryManager>();
        repositories.Setup(repository => repository.AddScrapeRunAsync(It.IsAny<ScrapeRun>(), It.IsAny<CancellationToken>()))
            .Callback<ScrapeRun, CancellationToken>((value, _) => run.Value = value)
            .Returns(Task.CompletedTask);
        repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new Handler(repositories.Object, new AdminUser(), TimeProvider.System,
            Options.Create(new ParserSettings { Enabled = true, DefaultImagesPerRun = defaultImagesPerRun })), run);
    }

    private sealed class AdminUser : IUserContext
    {
        public int? UserId => 1;
        public string? Login => "admin";
        public bool IsInRole(string role) => role == nameof(AppUserRole.Admin);
        public void RequireAuthenticated() { }
    }
}
