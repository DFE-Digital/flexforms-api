using System.Security.Claims;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

[RateLimit(5, 30)]
public sealed record RegisterUserCommand(string SubjectToken, Guid? TemplateId = null) : IRequest<Result<UserDto>>, IRateLimitedRequest;

public sealed class RegisterUserCommandHandler(
    IEaRepository<User> userRepo,
    IEaRepository<Template> templateRepo,
    IExternalIdentityValidator externalValidator,
    IHttpContextAccessor httpContextAccessor,
    IUserFactory userFactory,
    IUnitOfWork unitOfWork,
    ITenantContextAccessor tenantContextAccessor,
    ITenantTemplateResolver tenantTemplateResolver) : IRequestHandler<RegisterUserCommand, Result<UserDto>>
{
    private sealed record TemplateResolution(
        bool IsSuccess,
        TemplateId? TemplateId,
        string? Error,
        DomainErrorCode? ErrorCode)
    {
        public static TemplateResolution Ok(TemplateId? templateId) =>
            new(true, templateId, null, null);

        public static TemplateResolution Fail(DomainErrorCode code, string error) =>
            new(false, null, error, code);
    }

    public async Task<Result<UserDto>> Handle(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get tenant-specific test auth options so only the correct tenant uses test authentication
            TestAuthenticationOptions? tenantTestAuthOptions = null;
            if (tenantContextAccessor.CurrentTenant != null)
            {
                tenantTestAuthOptions = new TestAuthenticationOptions();
                tenantContextAccessor.CurrentTenant.Settings
                    .GetSection(TestAuthenticationOptions.SectionName)
                    .Bind(tenantTestAuthOptions);
            }

            // Validate external token and extract claims
            var externalUser = await externalValidator
                .ValidateIdTokenAsync(request.SubjectToken, false, false, internalAuthOptions: null, tenantTestAuthOptions, cancellationToken);

            var email = externalUser.FindFirst(ClaimTypes.Email)?.Value
                        ?? throw new SecurityTokenException("RegisterUserCommandHandler > Missing email");

            var fullName = $"{externalUser.FindFirst(ClaimTypes.GivenName)?.Value} {externalUser.FindFirst(ClaimTypes.Surname)?.Value}";

            var name = externalUser.FindFirst("name")?.Value
                       ?? externalUser.FindFirst("given_name")?.Value
                       ?? email; // Fallback to email if name not available

            if (string.IsNullOrWhiteSpace(fullName))
                fullName = name;

            var now = DateTime.UtcNow;

            // Load user by email with template permissions to check access
            var dbUser = await (new GetUserWithAllTemplatePermissionsQueryObject(email))
                .Apply(userRepo.Query().AsNoTracking())
                .Include(u => u.Role)
                .Include(u => u.Permissions)
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            if (dbUser is not null)
            {
                // Existing users: only grant additional template access when explicitly requested
                if (request.TemplateId.HasValue)
                {
                    var explicitGrant = await TryResolveExplicitTemplateAsync(
                        request.TemplateId.Value,
                        cancellationToken);

                    if (!explicitGrant.IsSuccess)
                    {
                        return MapFailure(explicitGrant);
                    }

                    if (explicitGrant.TemplateId is not null)
                    {
                        var hasTemplatePermission = dbUser.TemplatePermissions
                            .Any(tp => tp.TemplateId.Value == request.TemplateId.Value);

                        if (!hasTemplatePermission)
                        {
                            var userToUpdate = await (new GetUserWithAllTemplatePermissionsQueryObject(email))
                                .Apply(userRepo.Query())
                                .Include(u => u.Role)
                                .Include(u => u.Permissions)
                                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

                            if (userToUpdate is null)
                                return Result<UserDto>.Failure("User not found for permission update");

                            userFactory.EnsureUserHasTemplatePermission(
                                userToUpdate,
                                explicitGrant.TemplateId,
                                userToUpdate.Id!,
                                now);

                            await unitOfWork.CommitAsync(cancellationToken);

                            return Result<UserDto>.Success(MapUser(userToUpdate));
                        }
                    }
                }

                return Result<UserDto>.Success(MapUser(dbUser));
            }

            // New user: resolve which template (if any) to grant
            var templateToAssignResult = await ResolveTemplateForNewUserAsync(request.TemplateId, cancellationToken);
            if (!templateToAssignResult.IsSuccess)
                return MapFailure(templateToAssignResult);

            var userId = new UserId(Guid.NewGuid());
            var newUser = userFactory.CreateUser(
                userId,
                new RoleId(RoleConstants.UserRoleId),
                fullName,
                email,
                templateToAssignResult.TemplateId,
                now);

            await userRepo.AddAsync(newUser, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return Result<UserDto>.Success(MapUser(newUser));
        }
        catch (SecurityTokenException ex)
        {
            return Result<UserDto>.Failure($"Invalid token: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<UserDto>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// For new users: explicit TemplateId wins when valid/live;
    /// otherwise auto-assign only when the tenant has exactly one live template.
    /// </summary>
    private async Task<TemplateResolution> ResolveTemplateForNewUserAsync(
        Guid? requestedTemplateId,
        CancellationToken cancellationToken)
    {
        if (requestedTemplateId.HasValue)
        {
            return await TryResolveExplicitTemplateAsync(requestedTemplateId.Value, cancellationToken);
        }

        var liveTemplates = await GetLiveTemplatesForCurrentTenantAsync(cancellationToken);
        if (liveTemplates.Count == 1)
        {
            return TemplateResolution.Ok(liveTemplates[0].Id);
        }

        // 0 or many live templates → register with no form access
        return TemplateResolution.Ok(null);
    }

    private async Task<TemplateResolution> TryResolveExplicitTemplateAsync(
        Guid templateGuid,
        CancellationToken cancellationToken)
    {
        var templateId = new TemplateId(templateGuid);

        if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
        {
            return TemplateResolution.Fail(DomainErrorCode.Forbidden, "Template does not belong to the current tenant");
        }

        var templateEntity = await new GetTemplateByIdQueryObject(templateId)
            .Apply(templateRepo.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        if (templateEntity is null)
        {
            return TemplateResolution.Fail(DomainErrorCode.NotFound, "Template not found");
        }

        if (!templateEntity.IsLive)
        {
            return TemplateResolution.Fail(DomainErrorCode.Forbidden, "Template is not live");
        }

        return TemplateResolution.Ok(templateId);
    }

    private async Task<IReadOnlyList<Template>> GetLiveTemplatesForCurrentTenantAsync(
        CancellationToken cancellationToken)
    {
        var tenantTemplateIds = await tenantTemplateResolver.GetTemplateIdsForCurrentTenantAsync(cancellationToken);
        if (tenantTemplateIds.Count == 0)
        {
            return Array.Empty<Template>();
        }

        var tenantGuids = tenantTemplateIds.Select(id => id.Value).ToHashSet();

        return await templateRepo.Query()
            .AsNoTracking()
            .Where(t => t.IsLive && tenantGuids.Contains(t.Id!.Value))
            .ToListAsync(cancellationToken);
    }

    private static Result<UserDto> MapFailure(TemplateResolution source) =>
        source.ErrorCode switch
        {
            DomainErrorCode.NotFound => Result<UserDto>.NotFound(source.Error ?? "Not found"),
            DomainErrorCode.Forbidden => Result<UserDto>.Forbid(source.Error ?? "Forbidden"),
            DomainErrorCode.Conflict => Result<UserDto>.Conflict(source.Error ?? "Conflict"),
            DomainErrorCode.Validation => Result<UserDto>.Validation(source.Error ?? "Validation failed"),
            _ => Result<UserDto>.Failure(source.Error ?? "Request failed")
        };

    private static UserDto MapUser(User user) =>
        new()
        {
            UserId = user.Id!.Value,
            Name = user.Name,
            Email = user.Email,
            RoleId = user.RoleId.Value,
            Authorization = CreateAuthorizationFromUser(user)
        };

    private static UserAuthorizationDto? CreateAuthorizationFromUser(User user)
    {
        if (user.Permissions == null || !user.Permissions.Any())
            return null;

        return new UserAuthorizationDto
        {
            Permissions = user.Permissions
                .Select(p => new UserPermissionDto
                {
                    ApplicationId = p.ApplicationId?.Value,
                    ResourceType = p.ResourceType,
                    ResourceKey = p.ResourceKey,
                    AccessType = p.AccessType
                })
                .ToArray(),
            Roles = new List<string> { user.Role?.Name ?? "User" }
        };
    }
}
