using GallerySiteBackend.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GallerySiteUnitTests;

public sealed class ManagementControllerContractTests
{
    [Theory]
    [InlineData(typeof(ImageController), "ReplaceTags", "PUT", "{id:int}/tags")]
    [InlineData(typeof(ImageController), "Hide", "DELETE", "{id:int}")]
    [InlineData(typeof(ImageController), "Restore", "POST", "{id:int}/restore")]
    [InlineData(typeof(AdministrationController), "Tags", "GET", "tags")]
    [InlineData(typeof(AdministrationController), "ChangeTagModeration", "PATCH", "tags/{id:int}/moderation")]
    [InlineData(typeof(AdministrationController), "HiddenImages", "GET", "images/hidden")]
    [InlineData(typeof(AdministrationController), "GetUploadPermission", "GET", "users/{id:int}/upload-permission")]
    [InlineData(typeof(AdministrationController), "UpdateUploadPermission", "PUT", "users/{id:int}/upload-permission")]
    public void ManagementRoutes_ExposeExpectedHttpContracts(Type controllerType, string methodName, string verb, string template)
    {
        var method = controllerType.GetMethod(methodName) ?? throw new InvalidOperationException($"{methodName} was not found.");
        var route = method.GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>().Single();

        Assert.Equal(template, route.Template);
        Assert.Contains(verb, route.HttpMethods);
    }

    [Theory]
    [InlineData("PendingImages")]
    [InlineData("ChangeModeration")]
    [InlineData("Tags")]
    [InlineData("ChangeTagModeration")]
    [InlineData("HiddenImages")]
    public void StaffEndpoints_AllowAdminAndModerator(string methodName)
    {
        var method = typeof(AdministrationController).GetMethod(methodName) ?? throw new InvalidOperationException($"{methodName} was not found.");
        var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

        Assert.Equal("Admin,Moderator", authorize.Roles);
    }

    [Theory]
    [InlineData("StartScrape")]
    [InlineData("GetScrape")]
    [InlineData("GetUploadPermission")]
    [InlineData("UpdateUploadPermission")]
    public void ScrapeEndpoints_RemainAdminOnly(string methodName)
    {
        var method = typeof(AdministrationController).GetMethod(methodName) ?? throw new InvalidOperationException($"{methodName} was not found.");
        var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

        Assert.Equal("Admin", authorize.Roles);
    }
}
