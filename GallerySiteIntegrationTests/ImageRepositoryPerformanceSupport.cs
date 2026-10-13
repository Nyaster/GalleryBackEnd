using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pgvector;
using Repository;
using Xunit.Abstractions;

namespace GallerySiteIntegrationTests;

[CollectionDefinition("Image repository performance", DisableParallelization = true)]
public sealed class ImageRepositoryPerformanceCollection;

public sealed class ImageRepositoryPerformanceFactAttribute : FactAttribute
{
    public ImageRepositoryPerformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GALLERY_RUN_PERFORMANCE") != "1")
            Skip = "Set GALLERY_RUN_PERFORMANCE=1 to run synthetic before/after measurements.";
    }
}

internal static class ImageRepositoryPerformanceSupport
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    internal static UserMadeImage Image(int index) => new()
    {
        Source = ImageSource.UserUpload, UploadedAtUtc = Now.AddSeconds(index),
        StorageKey = $"performance/{index}.jpg", ContentType = "image/jpeg", Width = 100, Height = 100,
        ModerationStatus = ModerationStatus.Approved, Visibility = ImageVisibility.Gallery,
        EmbeddingStatus = EmbeddingStatus.Pending, EmbeddingError = "Previous attempt failed",
        Embedding = new Vector(Enumerable.Repeat(0.125f, 1024).ToArray())
    };

    internal static async Task ResetAsync(PostgresFixture database)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Images\", \"Tags\" RESTART IDENTITY CASCADE");
    }

    internal static async Task MeasureAsync(string name, Func<Task> action, Func<Task>? prepare,
        ITestOutputHelper output)
    {
        for (var i = 0; i < 3; i++)
        {
            if (prepare is not null) await prepare();
            await action();
        }
        var times = new List<double>();
        var allocations = new List<long>();
        for (var i = 0; i < 9; i++)
        {
            if (prepare is not null) await prepare();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            await action();
            stopwatch.Stop();
            allocations.Add(GC.GetTotalAllocatedBytes(precise: true) - allocated);
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        output.WriteLine($"{name}: warmups=3 samples=9 median_ms={times.Order().ElementAt(4):F3} " +
                         $"median_allocated_bytes={allocations.Order().ElementAt(4)} " +
                         $"min_ms={times.Min():F3} max_ms={times.Max():F3}");
    }
}

// Counts the actual rows/columns consumed by EF, including the synchronous baseline claim.
internal sealed class ImageRepositoryReadMetrics : DbCommandInterceptor
{
    internal List<Read> Reads { get; } = [];
    internal sealed class Read(int columns)
    {
        internal int Columns { get; } = columns;
        internal int Rows { get; set; }
    }

    private DbDataReader Wrap(DbDataReader reader)
    {
        var read = new Read(reader.FieldCount);
        Reads.Add(read);
        return new CountingReader(reader, read);
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result) => Wrap(result);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Wrap(result));

    private sealed class CountingReader(DbDataReader inner, Read read) : DbDataReader
    {
        public override bool Read()
        {
            var result = inner.Read();
            if (result) read.Rows++;
            return result;
        }
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            var result = await inner.ReadAsync(cancellationToken);
            if (result) read.Rows++;
            return result;
        }
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length)
            => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length)
            => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
            => inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override DataTable? GetSchemaTable() => inner.GetSchemaTable();
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override void Close() => inner.Close();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
