using System.ComponentModel.DataAnnotations;

namespace Shared.DataTransferObjects;

public sealed record UpdateContentPreferencesDto([Required] bool? DisplayAllContent);

public sealed record SaveContentVisibilityRuleDto([Required] int[] TagIds);

public sealed record ContentVisibilityRuleDto(int Id, IReadOnlyList<int> TagIds);
