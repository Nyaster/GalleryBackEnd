using Repository;

namespace GallerySiteUnitTests;

public sealed class ForMeRankingTests
{
    [Fact]
    public void SelectRepresentatives_DistinctInterests_PreservesBothAndBreaksTiesByInputOrder()
    {
        float[][] vectors = [[0, 0], [1, 0], [10, 0], [10, 0]];

        var selected = ForMeRanking.SelectRepresentatives(vectors);

        Assert.Equal(new[] { 0, 2, 1 }, selected);
    }

    [Fact]
    public void SelectRepresentatives_ManyDifferentLikes_IsBoundedAtTen()
    {
        var vectors = Enumerable.Range(0, 100).Select(value => new[] { (float)value }).ToArray();
        Assert.Equal(10, ForMeRanking.SelectRepresentatives(vectors).Count);
    }

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
