namespace Repository;

public static class ForMeRanking
{
    public static IReadOnlyList<int> SelectRepresentatives(IReadOnlyList<float[]> vectors, int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCount, 100);
        if (vectors.Count == 0) return [];
        var selected = new List<int> { 0 };
        var minimumDistances = Enumerable.Repeat(double.PositiveInfinity, vectors.Count).ToArray();
        while (selected.Count < Math.Min(maximumCount, vectors.Count))
        {
            var latest = vectors[selected[^1]];
            var next = -1;
            var greatest = 0d;
            for (var i = 0; i < vectors.Count; i++)
            {
                var distance = 0d;
                for (var dimension = 0; dimension < latest.Length; dimension++)
                {
                    var difference = (double)latest[dimension] - vectors[i][dimension];
                    distance += difference * difference;
                }
                minimumDistances[i] = Math.Min(minimumDistances[i], distance);
                if (minimumDistances[i] <= greatest) continue;
                greatest = minimumDistances[i];
                next = i;
            }
            if (next < 0) break;
            selected.Add(next);
        }
        return selected;
    }

    public static IReadOnlyList<int> Merge(IReadOnlyList<IReadOnlyList<int>> neighborhoods)
    {
        var result = new List<int>(500);
        var seen = new HashSet<int>();
        var positions = new int[neighborhoods.Count];
        while (result.Count < 500)
        {
            var advanced = false;
            for (var i = 0; i < neighborhoods.Count && result.Count < 500; i++)
            {
                while (positions[i] < neighborhoods[i].Count)
                {
                    var id = neighborhoods[i][positions[i]++];
                    if (!seen.Add(id)) continue;
                    result.Add(id);
                    advanced = true;
                    break;
                }
            }
            if (!advanced) break;
        }
        return result;
    }
}
