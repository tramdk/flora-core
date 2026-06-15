namespace FloraCore.Application.Common.Models;

/// <summary>
/// Strongly-typed configuration options for Vietnamese payment gateways.
/// </summary>
public class PaymentGatewaysOptions
{
    /// <summary>
    /// The section name in the configuration provider.
    /// </summary>
    public const string SectionName = "PaymentGateways";

    /// <summary>
    /// Gets or sets the API base URL.
    /// </summary>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Frontend client base URL.
    /// </summary>
    public string FrontendUrl { get; set; } = string.Empty;
}
