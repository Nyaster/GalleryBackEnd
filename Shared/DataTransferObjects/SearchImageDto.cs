namespace Shared.DataTransferObjects;

public record SearchImageDto(List<string> Tags, string OrderBy, int Page, int PageSize, bool FanImages);