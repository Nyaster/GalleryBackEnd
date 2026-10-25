using Repository;

namespace GallerySiteUnitTests;

public sealed class ForMeRankingTests
{
    [Fact]
    public void SelectRepresentatives_DistinctInterests_PreservesBothAndBreaksTiesByInputOrder()
    {
        float[][] vectors = [[0, 0], [1, 0], [10, 0], [10, 0]];

        var selected = ForMeRanking.SelectRepresentatives(vectors, 25);

        Assert.Equal(new[] { 0, 2, 1 }, selected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(100)]
    public void SelectRepresentatives_ManyDifferentLikes_RespectsConfiguredLimit(int limit)
    {
        var vectors = Enumerable.Range(0, 100).Select(value => new[] { (float)value }).ToArray();
        Assert.Equal(limit, ForMeRanking.SelectRepresentatives(vectors, limit).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void SelectRepresentatives_EmptyInputAndInvalidLimit_RejectsLimit(int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ForMeRanking.SelectRepresentatives([], limit));

    [Fact]
    public void SelectRepresentatives_NullInput_RejectsInput()
        => Assert.Throws<ArgumentNullException>(() => ForMeRanking.SelectRepresentatives(null!, 25));

    [Fact]
    public void SelectRepresentatives_EmptyInput_ReturnsNoReferences()
        => Assert.Empty(ForMeRanking.SelectRepresentatives([], 25));

    [Fact]
    public void SelectRepresentatives_IdenticalLikes_UsesOneReference()
        => Assert.Equal(new[] { 0 }, ForMeRanking.SelectRepresentatives([[1f], [1f], [1f]], 25));

    [Fact]
    public void Merge_OverlappingNeighborhoods_PreservesVarietyWithoutDuplicates()
    {
        IReadOnlyList<int>[] lists = [[1, 2, 3], [1, 10, 11], [2, 20]];
        Assert.Equal(new[] { 1, 10, 2, 3, 11, 20 }, ForMeRanking.Merge(lists));
    }

    [Fact]
    public void Merge_SingleInterest_CanReturnFiveHundredImages()
    {
        IReadOnlyList<int>[] lists = [Enumerable.Range(1, 600).ToArray()];
        Assert.Equal(Enumerable.Range(1, 500), ForMeRanking.Merge(lists));
    }
}