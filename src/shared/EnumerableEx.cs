internal static class EnumerableEx
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
    {
        return source.Where(x => x is not null).Cast<T>();
    }

    extension<TSource>(IEnumerable<TSource> source)
    {
        public Dictionary<string, TElement> ToCaseInsensitiveDictionary<TElement>(Func<TSource, string> keySelector, Func<TSource, TElement> valueSelector)
        {
            return source.ToDictionary(keySelector, valueSelector, StringComparer.InvariantCultureIgnoreCase);
        }

        public Dictionary<string, TSource> ToCaseInsensitiveDictionary(Func<TSource, string> keySelector)
        {
            return source.ToDictionary(keySelector, x => x, StringComparer.InvariantCultureIgnoreCase);
        }
    }

    extension<T>(IEnumerable<Task<T>> source)
    {
        public Task<T[]> WhenAll()
        {
            return Task.WhenAll(source);
        }

        public Task<Task<T>> WhenAny()
        {
            return Task.WhenAny(source);
        }
    }

    extension(IEnumerable<string> source)
    {
        public string ConcatString()
        {
            return string.Concat(source);
        }

        public string JoinString(string separator)
        {
            return string.Join(separator, source);
        }
    }
}
