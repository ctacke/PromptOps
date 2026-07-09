using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>Stores a <see cref="Dictionary{TKey, TValue}"/> of strings as a JSON object column. Companion to <see cref="StringListValueConverter"/>.</summary>
internal static class StringDictionaryValueConverter
{
    public static readonly ValueConverter<Dictionary<string, string>, string> Instance = new(
        value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>());

    public static readonly ValueComparer<Dictionary<string, string>> Comparer = new(
        (a, b) => (a ?? new Dictionary<string, string>()).OrderBy(kv => kv.Key)
            .SequenceEqual((b ?? new Dictionary<string, string>()).OrderBy(kv => kv.Key)),
        v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
        v => new Dictionary<string, string>(v));
}
