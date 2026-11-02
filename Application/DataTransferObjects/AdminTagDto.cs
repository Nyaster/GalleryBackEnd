using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record AdminTagDto(int Id, string Name, TagModerationStatus Status, DateTimeOffset CreatedAtUtc, int LinkedImageCount);

public sealed record PageableTagsDto(int Page, int PageSize, int Total, IReadOnlyList<AdminTagDto> Tags);
