namespace Repository;

public static class PostgreSqlHashFunctions
{
    public static long HashInt4Extended(int value, long seed)
        => throw new NotSupportedException("This method is translated to PostgreSQL SQL and cannot be evaluated in memory.");
}
