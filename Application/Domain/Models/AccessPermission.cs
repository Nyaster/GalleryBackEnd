using Shared.DataTransferObjects;

namespace Entities.Models;

public static class AccessPermission
{
    public const string ClaimType = "gallery_permission";
    public const string ImagesReviewQueue = "images.reviewQueue";
    public const string ImagesReadRestricted = "images.readRestricted";
    public const string ImagesModerate = "images.moderate";
    public const string ImagesClassifyAi = "images.classifyAi";
    public const string ImagesHideAny = "images.hideAny";
    public const string ImagesRestoreAny = "images.restoreAny";
    public const string ImagesPublishAny = "images.publishAny";
    public const string TagsModerate = "tags.moderate";
    public const string ImageTagsReview = "imageTags.review";
    public const string ImageTagsEditAny = "imageTags.editAny";
    public const string CommentsDeleteAny = "comments.deleteAny";
    public const string CommentsManageRestrictions = "comments.manageRestrictions";
    public const string AnnouncementsManage = "announcements.manage";
    public const string FeedbackRead = "feedback.read";
    public const string VisibilityRulesManage = "visibilityRules.manage";
    public const string ScrapingRun = "scraping.run";
    public const string UploadsManageAccess = "uploads.manageAccess";
    public const string UploadsBypassRestrictions = "uploads.bypassRestrictions";
    public const string RolesManage = "roles.manage";
    public const string UploadsBypassReview = "uploads.bypassReview";
    public const string ImageTagsBypassReviewOnOwnImages = "imageTags.bypassReviewOnOwnImages";

    public static IReadOnlyList<AccessPermissionDto> Catalog { get; } = Array.AsReadOnly<AccessPermissionDto>(
    [
        new(ImagesReviewQueue, "View image review queues"),
        new(ImagesReadRestricted, "Read restricted images"),
        new(ImagesModerate,
            "Approve or reject readable, non-hidden images, including images in an accessible review queue"),
        new(ImagesClassifyAi,
            "Change AI classification on readable, non-hidden images, including images in an accessible review queue"),
        new(ImagesHideAny, "Hide any image"),
        new(ImagesRestoreAny,
            "Restore hidden images you can access through ownership, restricted read, or the review queue"),
        new(ImagesPublishAny,
            "Publish private images you can access through ownership, restricted read, or the review queue"),
        new(TagsModerate, "Moderate tags"),
        new(ImageTagsReview, "Read image tag history, review proposals, and revert eligible replacements"),
        new(ImageTagsEditAny, "Edit tags directly on any image"),
        new(CommentsDeleteAny, "Delete any comment"),
        new(CommentsManageRestrictions, "Manage account comment restrictions"),
        new(AnnouncementsManage, "Manage announcements"),
        new(FeedbackRead, "Read submitted feedback"),
        new(VisibilityRulesManage, "Manage content visibility rules"),
        new(ScrapingRun, "Run scraping jobs and read their status"),
        new(UploadsManageAccess, "Manage account upload access"),
        new(UploadsBypassRestrictions, "Bypass account upload restrictions"),
        new(RolesManage, "Manage role definitions and assignments"),
        new(UploadsBypassReview, "Bypass review of uploaded images"),
        new(ImageTagsBypassReviewOnOwnImages, "Apply tag changes directly on owned images")
    ]);

    public static IEnumerable<string> AllKeys => Catalog.Select(permission => permission.Key);

    public static bool HasLegacyPermission(string role, string permission) => role switch
    {
        nameof(AppUserRole.Admin) => permission is not (UploadsBypassReview or ImageTagsBypassReviewOnOwnImages)
                                     && AllKeys.Contains(permission, StringComparer.Ordinal),
        nameof(AppUserRole.Moderator) => permission is ImagesReviewQueue or ImagesReadRestricted or ImagesModerate
            or ImagesClassifyAi or ImagesHideAny or ImagesRestoreAny or ImagesPublishAny or TagsModerate
            or ImageTagsReview or ImageTagsEditAny or CommentsDeleteAny or CommentsManageRestrictions,
        _ => false
    };
}