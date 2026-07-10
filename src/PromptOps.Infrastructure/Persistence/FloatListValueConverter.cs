using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>Stores a <see cref="List{T}"/> of floats (an embedding vector) as a JSON array column. Companion to <see cref="StringListValueConverter"/>.</summary>
internal static class FloatListValueConverter
{
    public static readonly ValueConverter<List<float>, string> Instance = new(
        value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<List<float>>(json, (JsonSerializerOptions?)null) ?? new List<float>());

    public static readonly ValueComparer<List<float>> Comparer = new(
        (a, b) => (a ?? new List<float>()).SequenceEqual(b ?? new List<float>()),
        v => v.Aggregate(0, (hash, f) => HashCode.Combine(hash, f)),
        v => v.ToList());
}
