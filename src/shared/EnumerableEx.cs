internal static class EnumerableEx
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
        => source.Where(x => x is not null).Cast<T>();

    extension<TSource>(IEnumerable<TSource> source)
    {
        public Dictionary<string, TElement> ToCaseInsensitiveDictionary<TElement>(Func<TSource, string> keySelector, Func<TSource, TElement> valueSelector)
            => source.ToDictionary(keySelector, valueSelector, StringComparer.InvariantCultureIgnoreCase);

        public Dictionary<string, TSource> ToCaseInsensitiveDictionary(Func<TSource, string> keySelector)
            => source.ToDictionary(keySelector, x => x, StringComparer.InvariantCultureIgnoreCase);
    }

    extension<T>(IEnumerable<Task<T>> source)
    {
        public Task<T[]> WhenAll()
            => Task.WhenAll(source);

        public Task<Task<T>> WhenAny()
            => Task.WhenAny(source);
    }

    extension(IEnumerable<string> source)
    {
        public string ConcatString()
            => string.Concat(source);

        public string JoinString(string separator)
            => string.Join(separator, source);
    }
}
