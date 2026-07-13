using Entities.Models;
using GallerySiteBackend.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GallerySiteUnitTests;

public sealed class InteractionAndRankingContractTests
{
    [Theory]
    [InlineData(typeof(ImageController), "Comments", "GET", "{imageId:int}/comments")]
    [InlineData(typeof(ImageController), "CreateComment", "POST", "{imageId:int}/comments")]
    [InlineData(typeof(ImageController), "Like", "PUT", "{imageId:int}/likes/me")]
    [InlineData(typeof(ImageController), "Unlike", "DELETE", "{imageId:int}/likes/me")]
    [InlineData(typeof(CommentsController), "Delete", "DELETE", "{commentId:int}")]
    [InlineData(typeof(RankingsController), "Get", "GET", "{period}")]
    [InlineData(typeof(AdministrationController), "DeleteComment", "DELETE", "comments/{commentId:int}")]
    [InlineData(typeof(AdministrationController), "GetCommentRestriction", "GET", "users/{id:int}/comment-restriction")]
    [InlineData(typeof(AdministrationController), "SetCommentRestriction", "PUT", "users/{id:int}/comment-restriction")]
    [InlineData(typeof(AdministrationController), "DeleteCommentRestriction", "DELETE", "users/{id:int}/comment-restriction")]
    public void InteractionRoutes_ExposeExpectedHttpContracts(Type controllerType, string methodName, string verb, string template)
    {
        var method = controllerType.GetMethod(methodName) ?? throw new InvalidOperationException($"{methodName} was not found.");
        var route = method.GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>().Single();
        Assert.Equal(template, route.Template);
        Assert.Contains(verb, route.HttpMethods);
    }

    [Theory]
    [InlineData("DeleteComment")]
    [InlineData("GetCommentRestriction")]
    [InlineData("SetCommentRestriction")]
    [InlineData("DeleteCommentRestriction")]
    public void CommentStaffEndpoints_AllowAdminAndModerator(string methodName)
    {
        var method = typeof(AdministrationController).GetMethod(methodName) ?? throw new InvalidOperationException($"{methodName} was not found.");
        var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("Admin,Moderator", authorize.Roles);
    }

    [Fact]
    public void RankingPeriodBounds_UsesUtcCalendarBoundaries()
    {
        var now = new DateTimeOffset(2026, 7, 15, 17, 42, 0, TimeSpan.Zero); // Wednesday
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), RankingPeriodBounds.Current(RankingPeriod.Daily, now).StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero), RankingPeriodBounds.Current(RankingPeriod.Weekly, now).StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), RankingPeriodBounds.Current(RankingPeriod.Monthly, now).StartUtc);
    }

    [Fact]
    public void RankingPeriodBounds_RecognizesOnlyValidCompletedBoundaries()
    {
        Assert.True(RankingPeriodBounds.IsBoundary(RankingPeriod.Weekly, new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(RankingPeriodBounds.IsBoundary(RankingPeriod.Weekly, new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(RankingPeriodBounds.IsBoundary(RankingPeriod.Monthly, new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(RankingPeriodBounds.IsBoundary(RankingPeriod.Daily, new DateTimeOffset(2026, 7, 1, 2, 0, 0, TimeSpan.FromHours(2))));
    }
}
