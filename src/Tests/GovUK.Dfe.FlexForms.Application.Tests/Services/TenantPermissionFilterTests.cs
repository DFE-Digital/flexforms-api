using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantPermissionFilterTests
{
    private readonly TemplateId _tenantTemplateId = new(Guid.NewGuid());
    private readonly TemplateId _otherTemplateId = new(Guid.NewGuid());
    private readonly Guid _tenantApplicationId = Guid.NewGuid();
    private readonly Guid _otherApplicationId = Guid.NewGuid();

    [Fact]
    public void BelongsToTenant_ShouldKeepTenantTemplatePermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<TemplateId> { _tenantTemplateId };
        var map = new Dictionary<Guid, TemplateId>();

        var tenantPermission = CreatePermission(userId, ResourceType.Template, _tenantTemplateId.Value.ToString(), AccessType.Read);
        var otherPermission = CreatePermission(userId, ResourceType.Template, _otherTemplateId.Value.ToString(), AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(tenantPermission, tenantTemplateIds, map));
        Assert.False(TenantPermissionFilter.BelongsToTenant(otherPermission, tenantTemplateIds, map));
    }

    [Fact]
    public void BelongsToTenant_ShouldKeepOnlyTenantApplicationPermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<TemplateId> { _tenantTemplateId };
        var map = new Dictionary<Guid, TemplateId>
        {
            [_tenantApplicationId] = _tenantTemplateId,
            [_otherApplicationId] = _otherTemplateId
        };

        var tenantPermission = CreatePermission(userId, ResourceType.Application, _tenantApplicationId.ToString(), AccessType.Read);
        var otherPermission = CreatePermission(userId, ResourceType.Application, _otherApplicationId.ToString(), AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(tenantPermission, tenantTemplateIds, map));
        Assert.False(TenantPermissionFilter.BelongsToTenant(otherPermission, tenantTemplateIds, map));
    }

    [Fact]
    public void BelongsToTenant_ShouldRejectUnknownApplicationPermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<TemplateId> { _tenantTemplateId };
        var unknownApplicationId = Guid.NewGuid();
        var permission = CreatePermission(userId, ResourceType.Application, unknownApplicationId.ToString(), AccessType.Read);

        Assert.False(TenantPermissionFilter.BelongsToTenant(
            permission,
            tenantTemplateIds,
            new Dictionary<Guid, TemplateId>()));
    }

    [Fact]
    public void BelongsToTenant_ShouldKeepTenantWideAnyPermission()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<TemplateId> { _tenantTemplateId };
        var permission = CreatePermission(userId, ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(
            permission,
            tenantTemplateIds,
            new Dictionary<Guid, TemplateId>()));
    }

    private static Permission CreatePermission(
        UserId userId,
        ResourceType resourceType,
        string resourceKey,
        AccessType accessType) =>
        new(
            new PermissionId(Guid.NewGuid()),
            userId,
            null,
            resourceKey,
            resourceType,
            accessType,
            DateTime.UtcNow,
            userId);
}
