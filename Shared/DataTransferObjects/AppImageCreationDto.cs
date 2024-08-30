using Microsoft.AspNetCore.Http;

namespace Shared.DataTransferObjects;

public record AppImageCreationDto(IFormFile ImageFile, bool IsHidden, IEnumerable<string> Tags);