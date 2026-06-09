namespace FloraCore.Application.Common.Models;

/// <summary>
/// Configuration settings for connecting to the Fanpage AI Manager crawler API.
/// </summary>
public class FanpageAiManagerSettings
{
    /// <summary>
    /// Gets or sets the base URL of the Fanpage AI Manager API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shared API key used to authenticate request.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
