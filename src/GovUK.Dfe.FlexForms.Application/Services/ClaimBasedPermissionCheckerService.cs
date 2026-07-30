using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Implementation of IPermissionCheckerService that checks permissions based on claims in the current user's ClaimsPrincipal
/// </summary>
public sealed class ClaimBasedPermissionCheckerService(IHttpContextAccessor httpContextAccessor)
    : IPermissionCheckerService
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    /// <inheritdoc />
    public bool HasPermission(ResourceType resourceType, string resourceId, AccessType accessType)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        if (PermissionClaimEvaluator.HasFullAdminAccess(user))
            return true;

        return PermissionClaimEvaluator.HasPermissionClaimOrTenantWide(user, resourceType, resourceId, accessType);
    }

    /// <inheritdoc />
    public bool HasTemplatePermission(string templateId, AccessType accessType)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        if (PermissionClaimEvaluator.HasFullAdminAccess(user))
            return true;

        return PermissionClaimEvaluator.HasPermissionClaim(user, ResourceType.Template, templateId, accessType);
    }
    
    /// <inheritdoc />
    public bool HasAnyPermission(ResourceType resourceType, AccessType accessType)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        if (PermissionClaimEvaluator.CanReadAllApplications(user) && resourceType == ResourceType.Application && accessType == AccessType.Read)
            return true;

        return PermissionClaimEvaluator.HasAnyExplicitPermissionClaim(user, resourceType, accessType);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetResourceIdsWithPermission(ResourceType resourceType, AccessType accessType)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return Array.Empty<string>();

        return PermissionClaimEvaluator.HasAnyExplicitPermissionClaim(user, resourceType, accessType)
            ? user.Claims
                .Where(c => c.Type == PermissionClaimEvaluator.PermissionClaimType
                    && c.Value.StartsWith($"{resourceType}:", StringComparison.OrdinalIgnoreCase)
                    && c.Value.EndsWith($":{accessType}", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Value.Split(':')[1], PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value.Split(':')[1])
                .ToList()
                .AsReadOnly()
            : Array.Empty<string>();
    }

    /// <inheritdoc />
    public bool IsApplicationOwner(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            return false;

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        if (PermissionClaimEvaluator.HasFullAdminAccess(user))
            return true;

        return PermissionClaimEvaluator.CanWriteApplication(user, applicationId);
    }

    /// <inheritdoc />
    public bool IsApplicationOwner(GovUK.Dfe.FlexForms.Domain.Entities.Application application, string currentUserId)
    {
        if (PermissionClaimEvaluator.HasFullAdminAccess(_httpContextAccessor.HttpContext?.User!))
            return true;

        if (application == null) return false;
        if (string.IsNullOrEmpty(currentUserId)) return false;

        return application.CreatedBy.Value.ToString() == currentUserId;
    }

    /// <inheritdoc />
    public bool CanManageContributors(string applicationId)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        if (PermissionClaimEvaluator.HasFullAdminAccess(user))
            return true;

        return IsApplicationOwner(applicationId);
    }

    /// <inheritdoc />
    public bool IsAdmin()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        return PermissionClaimEvaluator.HasFullAdminAccess(user);
    }

    /// <inheritdoc />
    public bool IsInteractiveTenantAdmin()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        return PermissionClaimEvaluator.IsInteractiveTenantAdmin(user);
    }

    /// <inheritdoc />
    public bool IsInteractivePlatformAdmin()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        return PermissionClaimEvaluator.IsInteractivePlatformAdmin(user);
    }

    /// <inheritdoc />
    public bool CanReadAllApplications()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        return PermissionClaimEvaluator.CanReadAllApplications(user);
    }

    /// <inheritdoc />
    public bool CanManageTemplates()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        return PermissionClaimEvaluator.CanManageTemplates(user);
    }

    /// <inheritdoc />
    public bool CanManageUsers()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return false;

        return PermissionClaimEvaluator.CanManageUsers(user);
    }
}
