using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>Stores a <see cref="Dictionary{TKey, TValue}"/> of doubles as a JSON object column — <see cref="PromptScore.ComponentScores"/>'s "componentScores (json breakdown)" (architecture.md §3). Companion to <see cref="StringDictionaryValueConverter"/>.</summary>
internal static class StringDoubleDictionaryValueConverter
{
    public static readonly ValueConverter<Dictionary<string, double>, string> Instance = new(
        value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<Dictionary<string, double>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<string, double>());

    public static readonly ValueComparer<Dictionary<string, double>> Comparer = new(
        (a, b) => (a ?? new Dictionary<string, double>()).OrderBy(kv => kv.Key)
            .SequenceEqual((b ?? new Dictionary<string, double>()).OrderBy(kv => kv.Key)),
        v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
        v => new Dictionary<string, double>(v));
}
