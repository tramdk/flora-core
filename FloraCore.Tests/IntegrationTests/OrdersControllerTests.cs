using FloraCore.Application.Common.Models;
using FloraCore.Application.Features.Auth.Commands;
using FloraCore.Application.Features.Orders.Commands;
using FloraCore.Domain.ValueObjects;
using FloraCore.Tests.IntegrationTests;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using FloraCore.Application.Features.Auth.DTOs;
using System.Text.Json;
using System;
using Microsoft.Extensions.DependencyInjection;
using FloraCore.Application.Common.Interfaces;
using Moq;

namespace FloraCore.Tests.IntegrationTests
{
    public class OrdersControllerTests : IClassFixture<OrdersControllerTestWebApplicationFactory>
    {
        private readonly OrdersControllerTestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public OrdersControllerTests(OrdersControllerTestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task Create_ShouldCreateOrderAndSendNotification()
        {
            // Arrange
            var registerCommand = new RegisterCommand(
                Email: $"test{Guid.NewGuid()}@example.com",
                Password: "P@sswOrd123",
                FullName: "Test User"
            );

            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);
            registerResponse.EnsureSuccessStatusCode();

            var loginCommand = new LoginCommand(
                Email: registerCommand.Email,
                Password: registerCommand.Password
            );

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
            loginResponse.EnsureSuccessStatusCode();

            var authResponse = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.Data!.AccessToken);

            var createOrderCommand = new CreateOrderCommand(
                UserId: authResponse!.Data!.User.Id, // Target actual authenticated user
                ShippingAddress: new Address { Street = "Street", City = "City", State = "State", ZipCode = "ZipCode" }
            );

            // Mock IAdminNotificationService
            var adminNotificationServiceMock = _factory.Services.GetRequiredService<Mock<IAdminNotificationService>>();

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/orders", createOrderCommand);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            // Verify that a notification was sent
            adminNotificationServiceMock.Verify(x => x.SendNewOrderNotification(It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task GetOrders_AsRegularUser_ReturnsForbidden()
        {
            // Arrange
            var registerCommand = new RegisterCommand(
                Email: $"user{Guid.NewGuid()}@example.com",
                Password: "P@sswOrd123",
                FullName: "Regular User"
            );

            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);
            registerResponse.EnsureSuccessStatusCode();

            var loginCommand = new LoginCommand(
                Email: registerCommand.Email,
                Password: registerCommand.Password
            );

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
            loginResponse.EnsureSuccessStatusCode();

            var authResponse = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.Data!.AccessToken);

            // Act
            var response = await _client.GetAsync("/api/v1/orders");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task GetOrders_AsAdmin_ReturnsSuccess()
        {
            // Arrange
            var registerCommand = new RegisterCommand(
                Email: $"admin{Guid.NewGuid()}@example.com",
                Password: "P@sswOrd123",
                FullName: "Admin User",
                Role: "Admin"
            );

            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);
            registerResponse.EnsureSuccessStatusCode();

            var loginCommand = new LoginCommand(
                Email: registerCommand.Email,
                Password: registerCommand.Password
            );

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
            loginResponse.EnsureSuccessStatusCode();

            var authResponse = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.Data!.AccessToken);

            // Act
            var response = await _client.GetAsync("/api/v1/orders");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task GetRevenueStatistics_AsRegularUser_ReturnsForbidden()
        {
            // Arrange
            var registerCommand = new RegisterCommand(
                Email: $"user{Guid.NewGuid()}@example.com",
                Password: "P@sswOrd123",
                FullName: "Regular User"
            );

            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);
            registerResponse.EnsureSuccessStatusCode();

            var loginCommand = new LoginCommand(
                Email: registerCommand.Email,
                Password: registerCommand.Password
            );

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
            loginResponse.EnsureSuccessStatusCode();

            var authResponse = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.Data!.AccessToken);

            // Act
            var response = await _client.GetAsync("/api/v1/orders/revenue");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task GetRevenueStatistics_AsAdmin_ReturnsSuccess()
        {
            // Arrange
            var registerCommand = new RegisterCommand(
                Email: $"admin{Guid.NewGuid()}@example.com",
                Password: "P@sswOrd123",
                FullName: "Admin User",
                Role: "Admin"
            );

            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerCommand);
            registerResponse.EnsureSuccessStatusCode();

            var loginCommand = new LoginCommand(
                Email: registerCommand.Email,
                Password: registerCommand.Password
            );

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginCommand);
            loginResponse.EnsureSuccessStatusCode();

            var authResponse = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse!.Data!.AccessToken);

            // Act
            var response = await _client.GetAsync("/api/v1/orders/revenue");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // Override CustomWebApplicationFactory to replace IAdminNotificationService with a Mock
    public class OrdersControllerTestWebApplicationFactory : FloraCore.Tests.CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                // Remove the original IAdminNotificationService registration
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAdminNotificationService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Add a Mock<IAdminNotificationService> registration
                var mockAdminNotificationService = new Mock<IAdminNotificationService>();
                services.AddSingleton(mockAdminNotificationService);
                services.AddSingleton(mockAdminNotificationService.Object);
            });
        }
    }
}
