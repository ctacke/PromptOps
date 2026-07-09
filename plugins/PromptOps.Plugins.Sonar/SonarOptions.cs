namespace PromptOps.Plugins.Sonar;

/// <summary>Bound from the daemon's <c>Plugins:sonar</c> configuration section.</summary>
public sealed class SonarOptions
{
    /// <summary>SonarQube/SonarCloud server base URL, e.g. <c>https://sonar.example.com</c>. Collection is skipped (returns null) when unset.</summary>
    public string? BaseUrl { get; set; }
}
