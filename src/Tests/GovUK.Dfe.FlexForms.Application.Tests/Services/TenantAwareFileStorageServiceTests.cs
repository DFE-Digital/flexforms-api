using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.CoreLibs.FileStorage.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantAwareFileStorageServiceTests
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TenantAwareFileStorageService> _logger;
    private readonly IFileStorageService _innerFileStorageService;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly TenantAwareFileStorageService _service;

    public TenantAwareFileStorageServiceTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _logger = Substitute.For<ILogger<TenantAwareFileStorageService>>();
        _innerFileStorageService = Substitute.For<IFileStorageService>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        var services = new ServiceCollection();
        services.AddSingleton(_innerFileStorageService);
        services.AddSingleton(_tenantContextAccessor);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _service = new TenantAwareFileStorageService(_httpContextAccessor, _logger);
    }

    private static TenantConfiguration CreateTenantWithLocalFileStorage(string baseDirectory)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Local",
                ["FileStorage:Local:BaseDirectory"] = baseDirectory,
                ["FileStorage:Local:CreateDirectoryIfNotExists"] = "true",
                ["FileStorage:Local:AllowOverwrite"] = "true"
            })
            .Build();

        return new TenantConfiguration(Guid.NewGuid(), "TestTenant", settings, Array.Empty<string>());
    }

    private static TenantConfiguration CreateTenantWithAzureFileStorage()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Azure",
                ["FileStorage:Azure:ConnectionString"] = "UseDevelopmentStorage=true",
                ["FileStorage:Azure:ShareName"] = "uploads"
            })
            .Build();

        return new TenantConfiguration(Guid.NewGuid(), "TestTenant", settings, Array.Empty<string>());
    }

    [Fact]
    public async Task UploadAsync_ShouldCallInnerService_WithTenantOptions_WhenTenantConfigured()
    {
        // Arrange
        var tenant = CreateTenantWithLocalFileStorage("/uploads/tenantA");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);
        var stream = new MemoryStream();

        // Act
        await _service.UploadAsync("test/path", stream, "file.txt");

        // Assert
        await _innerFileStorageService.Received(1).UploadAsync(
            "test/path", stream, "file.txt",
            Arg.Is<LocalFileStorageOptions>(o => o.BaseDirectory == "/uploads/tenantA"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenNoTenantContext()
    {
        // Arrange
        _tenantContextAccessor.CurrentTenant.Returns((TenantConfiguration?)null);
        var stream = new MemoryStream();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.txt"));
        Assert.Contains("No tenant context", ex.Message, StringComparison.Ordinal);
        await _innerFileStorageService.DidNotReceiveWithAnyArgs()
            .UploadAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenFileStorageProviderMissing()
    {
        // Arrange
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Local:BaseDirectory"] = "/uploads/orphan"
            })
            .Build();
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "BrokenTenant", settings, Array.Empty<string>()));
        var stream = new MemoryStream();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.txt"));
        Assert.Contains("FileStorage:Provider", ex.Message, StringComparison.Ordinal);
        await _innerFileStorageService.DidNotReceiveWithAnyArgs()
            .UploadAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenLocalBaseDirectoryMissing()
    {
        // Arrange
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Local"
            })
            .Build();
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "BrokenTenant", settings, Array.Empty<string>()));
        var stream = new MemoryStream();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.txt"));
        Assert.Contains("BaseDirectory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_ShouldCallInnerWithoutLocalOptions_WhenAzureConfigured()
    {
        // Arrange
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenantWithAzureFileStorage());
        var stream = new MemoryStream();

        // Act
        await _service.UploadAsync("test/path", stream, "file.txt");

        // Assert — Azure uses host-registered service after tenant validation
        await _innerFileStorageService.Received(1).UploadAsync(
            "test/path", stream, "file.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallInnerService_WithTenantOptions()
    {
        // Arrange
        var tenant = CreateTenantWithLocalFileStorage("/uploads/tenantA");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);

        // Act
        await _service.DeleteAsync("test/path");

        // Assert
        await _innerFileStorageService.Received(1).DeleteAsync(
            "test/path",
            Arg.Is<LocalFileStorageOptions>(o => o.BaseDirectory == "/uploads/tenantA"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_ShouldCallInnerService_WithTenantOptions()
    {
        // Arrange
        var tenant = CreateTenantWithLocalFileStorage("/uploads/tenantA");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);
        var expectedStream = new MemoryStream();
        _innerFileStorageService.DownloadAsync(Arg.Any<string>(), Arg.Any<LocalFileStorageOptions>(), Arg.Any<CancellationToken>())
            .Returns(expectedStream);

        // Act
        var result = await _service.DownloadAsync("test/path");

        // Assert
        Assert.Same(expectedStream, result);
    }

    [Fact]
    public async Task ExistsAsync_ShouldCallInnerService_WithTenantOptions()
    {
        // Arrange
        var tenant = CreateTenantWithLocalFileStorage("/uploads/tenantA");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);
        _innerFileStorageService.ExistsAsync(Arg.Any<string>(), Arg.Any<LocalFileStorageOptions>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.ExistsAsync("test/path");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UploadAsync_WithExplicitOverride_ShouldUseOverrideOptions()
    {
        // Arrange
        var tenant = CreateTenantWithLocalFileStorage("/uploads/tenantA");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);
        var overrideOptions = new LocalFileStorageOptions { BaseDirectory = "/custom/override" };
        var stream = new MemoryStream();

        // Act
        await _service.UploadAsync("test/path", stream, "file.txt", overrideOptions);

        // Assert
        await _innerFileStorageService.Received(1).UploadAsync(
            "test/path", stream, "file.txt",
            Arg.Is<LocalFileStorageOptions>(o => o.BaseDirectory == "/custom/override"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_ShouldThrowInvalidOperationException_WhenNoHttpContext()
    {
        // Arrange
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var stream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.txt"));
    }
}
