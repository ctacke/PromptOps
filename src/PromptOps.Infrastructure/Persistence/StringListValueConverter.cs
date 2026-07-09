using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>
/// Stores a <see cref="List{T}"/> of strings as a JSON array column. Used explicitly (rather than
/// relying on EF Core's implicit primitive-collection support) so the on-disk shape is predictable
/// across providers.
/// </summary>
internal static class StringListValueConverter
{
    public static readonly ValueConverter<List<string>, string> Instance = new(
        value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

    public static readonly ValueComparer<List<string>> Comparer = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
        v => v.ToList());
}
