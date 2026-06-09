using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using FloraCore.Tests;

namespace FloraCore.Tests.Web.Controllers;

public class ApiContractTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiContractTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ApiContract_ShouldMatchCommittedSpecification()
    {
        // 1. Fetch live generated OpenAPI spec
        var response = await _client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var liveSpecJson = await response.Content.ReadAsStringAsync();

        // 2. Load committed spec from disk
        var specPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../Specs/openapi.json");
        
        if (!File.Exists(specPath))
        {
            // Auto-bootstrap spec if it doesn't exist
            var directory = Path.GetDirectoryName(specPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllTextAsync(specPath, liveSpecJson);
            Assert.Fail("Specs/openapi.json was missing and has been auto-generated. Please commit it.");
        }

        var committedSpecJson = await File.ReadAllTextAsync(specPath);

        // 3. Parse and normalize to ignore whitespace and formatting differences
        using var liveDoc = JsonDocument.Parse(liveSpecJson);
        using var committedDoc = JsonDocument.Parse(committedSpecJson);

        // 4. Assert contract equality
        var liveFormatted = JsonSerializer.Serialize(liveDoc, new JsonSerializerOptions { WriteIndented = true });
        var committedFormatted = JsonSerializer.Serialize(committedDoc, new JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(committedFormatted, liveFormatted);
    }
}
