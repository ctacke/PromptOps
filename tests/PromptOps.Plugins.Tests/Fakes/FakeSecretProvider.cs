using PromptOps.Application.Providers;

namespace PromptOps.Plugins.Tests.Fakes;

internal sealed class FakeSecretProvider(string? token = null) : ISecretProvider
{
    public Task<string?> GetSecretAsync(string scope, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(token);
}
