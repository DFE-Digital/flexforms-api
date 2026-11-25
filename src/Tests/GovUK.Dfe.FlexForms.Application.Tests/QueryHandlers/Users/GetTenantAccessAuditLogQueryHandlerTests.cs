using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Users;

/// <summary>The user access audit trail, and the event catalogue (both admin-only reads).</summary>
public class GetTenantAccessAuditLogQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private readonly ITenantAccessAuditQuery _audit = Substitute.For<ITenantAccessAuditQuery>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();

    public GetTenantAccessAuditLogQueryHandlerTests()
    {
        _tenantContext.CurrentTenant.Returns(new TenantConfiguration(TenantId, "Transfers", new ConfigurationBuilder().Build(), []));
    }

    [Fact]
    public async Task UserManager_SeesTheCurrentTenantsEntries()
    {
        _permissions.CanManageUsers().Returns(true);
        var occurred = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
        var row = new TenantAccessAudit
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SubjectUserId = Guid.NewGuid(),
            SubjectEmail = "bob@example.com",
            Action = "RoleChanged",
            RoleName = "Admin",
            ActorUserId = Guid.NewGuid(),
            ActorEmail = "alice@example.com",
            Details = "User → Admin",
            OccurredAtUtc = occurred
        };
        _audit.ListAsync(TenantId, 25, Arg.Any<CancellationToken>()).Returns([row]);

        var result = await new GetTenantAccessAuditLogQueryHandler(_audit, _tenantContext, _permissions)
            .Handle(new GetTenantAccessAuditLogQuery(25), CancellationToken.None);

        Assert.Equal(TenantId, result.Value!.TenantId);
        var entry = Assert.Single(result.Value.Entries);
        Assert.Equal(row.Id, entry.Id);
        Assert.Equal(row.SubjectUserId, entry.SubjectUserId);
        Assert.Equal("bob@example.com", entry.SubjectEmail);
        Assert.Equal("RoleChanged", entry.Action);
        Assert.Equal("Admin", entry.RoleName);
        Assert.Equal(row.ActorUserId, entry.ActorUserId);
        Assert.Equal("alice@example.com", entry.ActorEmail);
        Assert.Equal("User → Admin", entry.Details);
        Assert.Equal(occurred, entry.OccurredAtUtc);
    }

    [Fact]
    public async Task NotAUserManager_IsForbidden()
    {
        var result = await new GetTenantAccessAuditLogQueryHandler(_audit, _tenantContext, _permissions)
            .Handle(new GetTenantAccessAuditLogQuery(), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _audit.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Fact]
    public async Task NoTenantContext_IsForbidden()
    {
        _permissions.CanManageUsers().Returns(true);
        _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await new GetTenantAccessAuditLogQueryHandler(_audit, _tenantContext, _permissions)
            .Handle(new GetTenantAccessAuditLogQuery(), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _audit.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task EventCatalogue_InteractiveAdminOrSuperAdminGetsTheCatalogue(bool tenantAdmin, bool superAdmin)
    {
        _permissions.IsInteractiveTenantAdmin().Returns(tenantAdmin);
        _permissions.IsInteractivePlatformAdmin().Returns(superAdmin);

        var result = await new GetEventCatalogueQueryHandler(_permissions).Handle(new GetEventCatalogueQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.Events);
    }

    [Fact]
    public async Task EventCatalogue_EveryoneElseIsForbidden()
    {
        var result = await new GetEventCatalogueQueryHandler(_permissions).Handle(new GetEventCatalogueQuery(), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }
}
