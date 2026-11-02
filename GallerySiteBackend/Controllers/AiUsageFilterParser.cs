using Entities.Exceptions;
using Entities.Models;

namespace GallerySiteBackend.Presentation;

internal static class AiUsageFilterParser
{
    public static IReadOnlyList<AiUsageClassification> Parse(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return [];

        var parsed = new List<AiUsageClassification>();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!Enum.TryParse<AiUsageClassification>(value, true, out var classification) ||
                !Enum.IsDefined(classification))
                throw new Base400BadRequestException($"Invalid aiUsage value '{value}'.");
            parsed.Add(classification);
        }

        return parsed.Distinct().ToArray();
    }
}
