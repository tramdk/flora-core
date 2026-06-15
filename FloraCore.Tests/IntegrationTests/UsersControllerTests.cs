using System.Net;
using System.Net.Http.Json;
using FloraCore.Application.Features.Users.Commands;
using FloraCore.Application.Features.Users.Queries;
using FloraCore.Application.Features.Users.DTOs;
using FloraCore.Application.Features.Users.DTOs;
using Xunit;
using FloraCore.Application.Features.Auth.Commands;
using FloraCore.Application.Common.Models;
using System.Net.Http.Headers;

namespace FloraCore.Tests.IntegrationTests;

public class UsersControllerTests : BaseIntegrationTest
{
    public UsersControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<string> GetTokenAsync(string email = "test@example.com", string password = "Password123!")
    {
        var registerCommand = new RegisterCommand(email, password, "Test User");
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);

        var loginCommand = new LoginCommand(email, password);
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
        var result = await GetResponseDataAsync<LoginResponse>(response);
        return result!.AccessToken;
    }

    private async Task<string> GetAdminTokenAsync()
    {
        // Register with Admin role (seeded roles allow this in test env)
        var registerCommand = new RegisterCommand("admin-users-test@example.com", "AdminPass123!", "Admin User", "Admin");
        await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);

        var loginCommand = new LoginCommand("admin-users-test@example.com", "AdminPass123!");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
        var result = await GetResponseDataAsync<LoginResponse>(response);
        return result!.AccessToken;
    }

    private record LoginResponse(string AccessToken, string RefreshToken);

    [Fact]
    public async Task CreateUser_AsAdmin_ReturnCreated()
    {
        var token = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var command = new CreateUserCommand("newuser-admin@example.com", "Password123!", "New User");
        var response = await _client.PostAsJsonAsync("/api/v1/users", command);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AsRegularUser_ReturnsForbidden()
    {
        var token = await GetTokenAsync("regular-create@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var command = new CreateUserCommand("blocked@example.com", "Password123!", "Blocked User");
        var response = await _client.PostAsJsonAsync("/api/v1/users", command);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_AsAdmin_ReturnsList()
    {
        var token = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users");

        response.EnsureSuccessStatusCode();
        var result = await GetResponseDataAsync<PagedResult<UserDto>>(response);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetUsers_AsRegularUser_ReturnsForbidden()
    {
        var token = await GetTokenAsync("regular-list@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_AsAdmin_ReturnsNoContent()
    {
        var adminToken = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Admin creates a user first
        var createCmd = new CreateUserCommand("todelete-admin@example.com", "Password123!", "To Delete");
        var createResponse = await _client.PostAsJsonAsync("/api/v1/users", createCmd);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var userId = await GetResponseDataAsync<Guid>(createResponse);

        // Then deletes it
        var deleteResponse = await _client.DeleteAsync($"/api/v1/users/{userId}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
