using System.ComponentModel.DataAnnotations;
using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record ImageTagChangeDto(
    long Id,
    int ImageId,
    ImageTagChangeKind Kind,
    ImageTagChangeStatus Status,
    int? EditedByUserId,
    string EditedByLogin,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> PreviousTags,
    IReadOnlyList<string> ProposedTags,
    IReadOnlyList<string> AddedTags,
    IReadOnlyList<string> RemovedTags,
    DateTimeOffset? AppliedAtUtc,
    int? ReviewedByUserId,
    string? ReviewedByLogin,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewNote,
    int? RevertedByUserId,
    string? RevertedByLogin,
    DateTimeOffset? RevertedAtUtc,
    string? ReversionNote);

public sealed record PageableImageTagChangesDto(int Page, int PageSize, int Total, IReadOnlyList<ImageTagChangeDto> Changes);

public enum TagChangeDecision
{
    Approve,
    Reject
}

public sealed record TagChangeModerationDto(
    [param: Required, EnumDataType(typeof(TagChangeDecision))] TagChangeDecision? Decision,
    [param: StringLength(500)] string? Note);
