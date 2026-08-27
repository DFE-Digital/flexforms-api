using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
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
    private readonly IFileStorageService _tenantDiskStorage;
    private readonly ITenantAzureFileStorageFactory _tenantAzureFactory;
    private readonly ITenantDiskFileStorageFactory _tenantDiskFactory;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IFileStorageService _tenantAzureStorage;
    private readonly TenantAwareFileStorageService _service;

    public TenantAwareFileStorageServiceTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _logger = Substitute.For<ILogger<TenantAwareFileStorageService>>();
        _innerFileStorageService = Substitute.For<IFileStorageService>();
        _tenantDiskStorage = Substitute.For<IFileStorageService>();
        _tenantAzureFactory = Substitute.For<ITenantAzureFileStorageFactory>();
        _tenantDiskFactory = Substitute.For<ITenantDiskFileStorageFactory>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        _tenantAzureStorage = Substitute.For<IFileStorageService>();

        var services = new ServiceCollection();
        services.AddSingleton(_innerFileStorageService);
        services.AddSingleton(_tenantContextAccessor);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _tenantAzureFactory.GetRequiredAzureFileStorage().Returns(_tenantAzureStorage);
        _tenantDiskFactory.GetRequiredDiskFileStorage().Returns(_tenantDiskStorage);

        _service = new TenantAwareFileStorageService(
            _httpContextAccessor, _tenantAzureFactory, _tenantDiskFactory, _logger);
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

    private static TenantConfiguration CreateTenantWithHybridFileStorage(string baseDirectory)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Hybrid",
                ["FileStorage:Local:BaseDirectory"] = baseDirectory,
                ["FileStorage:Local:CreateDirectoryIfNotExists"] = "true",
                ["FileStorage:Azure:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net",
                ["FileStorage:Azure:ShareName"] = "uploads"
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
                ["FileStorage:Azure:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net",
                ["FileStorage:Azure:ShareName"] = "uploads",
                ["FileStorage:Local:AllowedExtensions:0"] = "pdf",
                ["FileStorage:Local:MaxFileSizeBytes"] = "1000"
            })
            .Build();

        return new TenantConfiguration(Guid.NewGuid(), "TestTenant", settings, Array.Empty<string>());
    }

    [Fact]
    public async Task UploadAsync_ShouldCallTenantDiskFactory_WhenLocal()
    {
        var tenant = CreateTenantWithLocalFileStorage("/uploads/tenantA");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);
        var stream = new MemoryStream();

        await _service.UploadAsync("test/path", stream, "file.txt");

        await _tenantDiskStorage.Received(1).UploadAsync(
            "test/path", stream, "file.txt", Arg.Any<CancellationToken>());
        _tenantAzureFactory.DidNotReceive().GetRequiredAzureFileStorage();
        await _innerFileStorageService.DidNotReceiveWithAnyArgs()
            .UploadAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task UploadAsync_ShouldCallTenantDiskFactory_WhenHybrid()
    {
        var tenant = CreateTenantWithHybridFileStorage("/uploads");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);
        var stream = new MemoryStream();

        await _service.UploadAsync("test/path", stream, "file.pdf");

        await _tenantDiskStorage.Received(1).UploadAsync(
            "test/path", stream, "file.pdf", Arg.Any<CancellationToken>());
        _tenantAzureFactory.DidNotReceive().GetRequiredAzureFileStorage();
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenNoTenantContext()
    {
        _tenantContextAccessor.CurrentTenant.Returns((TenantConfiguration?)null);
        var stream = new MemoryStream();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.txt"));
        Assert.Contains("No tenant context", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenFileStorageProviderMissing()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Local:BaseDirectory"] = "/uploads/orphan"
            })
            .Build();
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "BrokenTenant", settings, Array.Empty<string>()));
        var stream = new MemoryStream();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.txt"));
        Assert.Contains("FileStorage:Provider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_ShouldUseTenantAzureFactory_WhenProviderIsAzure()
    {
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenantWithAzureFileStorage());
        var stream = new MemoryStream("hi"u8.ToArray());

        await _service.UploadAsync("test/path", stream, "file.pdf");

        await _tenantAzureStorage.Received(1).UploadAsync(
            "test/path", stream, "file.pdf", Arg.Any<CancellationToken>());
        _tenantDiskFactory.DidNotReceive().GetRequiredDiskFileStorage();
    }

    [Fact]
    public async Task UploadAsync_ShouldRejectDisallowedExtension_WhenAzureTenantHasAllowedExtensions()
    {
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenantWithAzureFileStorage());
        var stream = new MemoryStream("hi"u8.ToArray());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.exe"));

        Assert.Contains("not allowed", ex.Message, StringComparison.OrdinalIgnoreCase);
        _tenantAzureFactory.DidNotReceive().GetRequiredAzureFileStorage();
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallTenantDiskFactory_WhenLocal()
    {
        var tenant = CreateTenantWithLocalFileStorage("/uploads/tenantA");
        _tenantContextAccessor.CurrentTenant.Returns(tenant);

        await _service.DeleteAsync("test/path");

        await _tenantDiskStorage.Received(1).DeleteAsync("test/path", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_ShouldThrowInvalidOperationException_WhenNoHttpContext()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var stream = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync("test/path", stream, "file.txt"));
    }
}
