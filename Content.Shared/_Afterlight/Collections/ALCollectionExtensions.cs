namespace Content.Shared._Afterlight.Collections;

public static class ALCollectionExtensions
{
    public static TValue? GetValueOrNullStruct<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) where TValue : struct
    {
        return dictionary.TryGetValue(key, out var value) ? value : null;
    }
}
