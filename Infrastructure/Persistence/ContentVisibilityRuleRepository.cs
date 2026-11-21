using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Repository.Configuration;
using Shared.DataTransferObjects;

namespace Repository;

public sealed class ContentVisibilityRuleRepository(RepositoryContext context) : IContentVisibilityRuleRepository
{
    private const string DuplicateGroupMessage = "A rule with this tag group already exists.";

    public Task<List<ContentVisibilityRuleDto>> ListAsync(CancellationToken cancellationToken)
        => Project(context.ContentVisibilityRules.OrderBy(rule => rule.Id)).ToListAsync(cancellationToken);

    public async Task<ContentVisibilityRuleDto> GetAsync(int id, CancellationToken cancellationToken)
        => await Project(context.ContentVisibilityRules.Where(rule => rule.Id == id))
            .SingleOrDefaultAsync(cancellationToken) ?? throw RuleNotFound();

    public async Task<ContentVisibilityRuleDto> SaveAsync(int? id, int[] tagIds, CancellationToken cancellationToken)
    {
        var sortedTagIds = tagIds.Distinct().Order().ToArray();
        if (sortedTagIds.Length == 0 || sortedTagIds[0] <= 0 || sortedTagIds.Length != tagIds.Length)
            throw new Base400BadRequestException("A rule must contain distinct positive tag IDs.");

        var canonicalGroup =
            string.Join(',', sortedTagIds.Select(tagId => tagId.ToString(CultureInfo.InvariantCulture)));
        var groupKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalGroup)));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        ContentVisibilityRule rule;
        if (id is { } ruleId)
        {
            var lockedRules = await context.ContentVisibilityRules.FromSqlInterpolated($"""
                 SELECT * FROM "ContentVisibilityRules" WHERE "Id" = {ruleId} FOR UPDATE
                 """).ToListAsync(cancellationToken);
            rule = lockedRules.SingleOrDefault() ?? throw RuleNotFound();
            await context.Entry(rule).Collection(existing => existing.TagLinks).LoadAsync(cancellationToken);
        }
        else
        {
            rule = new ContentVisibilityRule { TagGroupKey = groupKey };
            context.ContentVisibilityRules.Add(rule);
        }

        // Hold a shared lock through commit so tag moderation cannot invalidate this check mid-save.
        var approvedTagIds = await context.Database.SqlQuery<int>($"""
                                                                   SELECT "Id" AS "Value" FROM "Tags"
                                                                   WHERE "Id" = ANY ({sortedTagIds}) AND "ModerationStatus" = {nameof(TagModerationStatus.Approved)}
                                                                   ORDER BY "Id" FOR SHARE
                                                                   """).ToListAsync(cancellationToken);
        if (approvedTagIds.Count != sortedTagIds.Length)
            throw new Base400BadRequestException("All tags in a rule must exist and be approved.");

        // Reject edits to an owned group without waiting for the other rule's write lock.
        // The unique index still resolves races for a previously unclaimed group.
        if (id is not null && groupKey != rule.TagGroupKey &&
            await context.ContentVisibilityRules.AnyAsync(existing =>
                existing.TagGroupKey == groupKey && existing.Id != rule.Id, cancellationToken))
            throw new Base409ConflictException(DuplicateGroupMessage);

        rule.TagGroupKey = groupKey;
        var previousTagIds = rule.TagLinks.Select(link => link.TagId).ToArray();
        foreach (var link in rule.TagLinks.Where(link => Array.BinarySearch(sortedTagIds, link.TagId) < 0).ToArray())
            rule.TagLinks.Remove(link);
        foreach (var tagId in sortedTagIds.Except(previousTagIds))
            rule.TagLinks.Add(new ContentVisibilityRuleTag { TagId = tagId });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  {
                                                      SqlState: PostgresErrorCodes.UniqueViolation,
                                                      ConstraintName: ContentVisibilityRuleEfConfiguration.GroupKeyIndex
                                                  })
        {
            throw new Base409ConflictException(DuplicateGroupMessage);
        }

        return new ContentVisibilityRuleDto(rule.Id, sortedTagIds);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var deleted = await context.ContentVisibilityRules.Where(rule => rule.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
            throw RuleNotFound();
    }

    private static IQueryable<ContentVisibilityRuleDto> Project(IQueryable<ContentVisibilityRule> rules)
        => rules.Select(rule => new ContentVisibilityRuleDto(rule.Id,
            rule.TagLinks.OrderBy(link => link.TagId).Select(link => link.TagId).ToArray()));

    private static Base404ReturnException RuleNotFound() => new("Content visibility rule not found.");
}