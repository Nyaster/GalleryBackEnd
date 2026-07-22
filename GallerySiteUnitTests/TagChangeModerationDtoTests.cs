using System.Reflection;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class TagChangeModerationDtoTests
{
    [Fact]
    public void ValidationAttributes_AreDefinedOnPrimaryConstructorParameters()
    {
        var parameters = typeof(TagChangeModerationDto).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single().GetParameters();

        Assert.NotEmpty(parameters[0].GetCustomAttributes<System.ComponentModel.DataAnnotations.RequiredAttribute>());
        Assert.NotEmpty(parameters[1].GetCustomAttributes<System.ComponentModel.DataAnnotations.StringLengthAttribute>());
    }
}
