namespace Entities.Models;

public sealed class ContentVisibilityRule
{
    public int Id { get; set; }
    public required string TagGroupKey { get; set; }
    public ICollection<ContentVisibilityRuleTag> TagLinks { get; set; } = [];
}

public sealed class ContentVisibilityRuleTag
{
    public int ContentVisibilityRuleId { get; set; }
    public int TagId { get; set; }
}