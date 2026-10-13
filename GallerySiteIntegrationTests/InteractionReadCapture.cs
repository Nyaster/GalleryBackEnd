using System.Data.Common;
using Entities.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GallerySiteIntegrationTests;

internal sealed class InteractionReadCapture : DbCommandInterceptor, IMaterializationObserver
{
    public List<string[]> SelectedColumns { get; } = [];
    public List<object> Entities { get; } = [];

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        SelectedColumns.Add(Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray());
        return ValueTask.FromResult(result);
    }

    public void EntityMaterialized(object entity)
    {
        if (entity is AppUser or Comment or Feedback) Entities.Add(entity);
    }
}
