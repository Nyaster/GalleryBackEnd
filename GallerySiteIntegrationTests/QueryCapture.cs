using System.Data.Common;
using Entities.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GallerySiteIntegrationTests;

internal sealed class QueryCapture(Func<Task>? afterFirstCount = null) : DbCommandInterceptor, IMaterializationInterceptor
{
    private bool _changed;

    public List<string> Reads { get; } = [];
    public List<int[]> IdParameters { get; } = [];
    public List<object> Entities { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Reads.Add(command.CommandText);
        IdParameters.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<int[]>());
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (!_changed && afterFirstCount is not null && command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase))
        {
            _changed = true;
            await afterFirstCount();
        }
        return result;
    }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is AppImage or AppUser or ImageLike or Comment or ImageTag or RankingSnapshot or RankingSnapshotEntry)
            Entities.Add(entity);
        return entity;
    }

    public void Clear()
    {
        Reads.Clear();
        IdParameters.Clear();
        Entities.Clear();
    }
}
