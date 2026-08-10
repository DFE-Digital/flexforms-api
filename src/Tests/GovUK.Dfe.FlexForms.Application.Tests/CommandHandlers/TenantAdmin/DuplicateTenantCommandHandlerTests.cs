using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class DuplicateTenantCommandHandlerTests
{
    private readonly ITenantDuplicator _duplicator = Substitute.For<ITenantDuplicator>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly DuplicateTenantCommandHandler _handler;

    public DuplicateTenantCommandHandlerTests()
    {
        _handler = new DuplicateTenantCommandHandler(
            _duplicator,
            _tenantContext,
            _permissionChecker,
            _configProvider);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotSuperAdmin()
    {
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);

        var result = await _handler.Handle(CreateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _duplicator.DidNotReceiveWithAnyArgs().DuplicateAsync(
            default, default, default!, default!, default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenSourceIsNotCurrentTenant()
    {
        var current = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var other = Guid.Parse("22222222-2222-4222-8222-222222222222");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(current, "Transfers"));

        var result = await _handler.Handle(CreateCommand(other), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _duplicator.DidNotReceiveWithAnyArgs().DuplicateAsync(
            default, default, default!, default!, default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldDuplicateAndRefresh_WhenSuperAdminDuplicatesOwnTenant()
    {
        var sourceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var newId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var authSecret = new string('a', 32);
        var internalSecret = new string('b', 32);
        var serviceApiKey = new string('c', 32);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(sourceId, "Transfers"));

        _duplicator.DuplicateAsync(
                sourceId,
                newId,
                "Transfers Copy",
                "copy.dev.example",
                "https://copy.dev.example",
                authSecret,
                internalSecret,
                Arg.Is<IReadOnlyList<(string Email, string ApiKey)>>(keys =>
                    keys.Count == 1
                    && keys[0].Email == "svc@example.com"
                    && keys[0].ApiKey == serviceApiKey),
                "New service name",
                Arg.Any<CancellationToken>())
            .Returns(new DuplicateTenantResult(
                sourceId,
                newId,
                "Transfers Copy",
                "copy.dev.example",
                "https://copy.dev.example",
                5));

        var result = await _handler.Handle(
            new DuplicateTenantCommand(
                sourceId,
                newId,
                "Transfers Copy",
                EncodePayload(
                    "copy.dev.example",
                    "https://copy.dev.example",
                    authSecret,
                    internalSecret,
                    [("svc@example.com", serviceApiKey)],
                    "New service name")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.SettingsCopied);
        await _configProvider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnValidation_WhenPayloadIsNotBase64()
    {
        var sourceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(sourceId, "Transfers"));

        var result = await _handler.Handle(
            new DuplicateTenantCommand(
                sourceId,
                Guid.NewGuid(),
                "New Tenant",
                "not-valid-base64!!!"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("PayloadJson", result.Error);
        await _duplicator.DidNotReceiveWithAnyArgs().DuplicateAsync(
            default, default, default!, default!, default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnValidation_WhenDuplicatorRejects()
    {
        var sourceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(sourceId, "Transfers"));
        _duplicator.DuplicateAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<(string Email, string ApiKey)>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<DuplicateTenantResult>>(_ => throw new InvalidOperationException("Hostname already assigned"));

        var result = await _handler.Handle(CreateCommand(sourceId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("Hostname", result.Error);
    }

    private static DuplicateTenantCommand CreateCommand(Guid sourceTenantId) =>
        new(
            sourceTenantId,
            Guid.NewGuid(),
            "New Tenant",
            EncodePayload(
                "new.dev.example",
                "https://new.dev.example",
                new string('a', 32),
                new string('b', 32),
                [],
                "New service"));

    private static string EncodePayload(
        string hostname,
        string frontendOrigin,
        string authSecret,
        string internalSecret,
        IReadOnlyList<(string Email, string ApiKey)> serviceKeys,
        string serviceName)
    {
        var payload = new CloneTenantSecretsPayload
        {
            Hostname = hostname,
            FrontendOrigin = frontendOrigin,
            AuthorizationApiSecretKey = authSecret,
            InternalServiceAuthSecretKey = internalSecret,
            InternalServiceAuthServiceApiKeys = serviceKeys
                .Select(s => new CloneTenantServiceApiKeyPayload { Email = s.Email, ApiKey = s.ApiKey })
                .ToList()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var doc = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        doc["serviceName"] = serviceName;

        return WafSafeUtf8Base64.Encode(doc.ToJsonString());
    }

    private static TenantConfiguration CreateTenant(Guid id, string name) =>
        new(id, name, new ConfigurationBuilder().Build(), []);
}
