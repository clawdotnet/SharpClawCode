using Microsoft.Extensions.Options;

namespace SharpClaw.Code.Web.Configuration;

/// <summary>
/// Validates <see cref="WebSearchOptions"/> after binding.
/// </summary>
public sealed class WebSearchOptionsValidator : IValidateOptions<WebSearchOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, WebSearchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EndpointTemplate))
        {
            return ValidateOptionsResult.Fail("WebSearchOptions.EndpointTemplate is required.");
        }

        if (!options.EndpointTemplate.Contains("{query}", StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail("WebSearchOptions.EndpointTemplate must include the '{query}' placeholder.");
        }

        return ValidateOptionsResult.Success;
    }
}
