using System.Data.Common;
using System.Text.RegularExpressions;

namespace GallerySiteIntegrationTests;

internal static class AnnLimitParameter
{
    public static DbParameter Find(DbCommand command)
    {
        var match = Regex.Match(command.CommandText, @"\bLIMIT\s+@(?<name>[a-zA-Z0-9_]+)\s*;?\s*$", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException("Expected a tagged ANN query with a parameterized SQL LIMIT.");
        var name = match.Groups["name"].Value;
        return command.Parameters.Cast<DbParameter>().SingleOrDefault(parameter => parameter.ParameterName.TrimStart('@') == name)
            ?? throw new InvalidOperationException($"ANN SQL LIMIT parameter '{name}' was not found.");
    }
}
