using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

public sealed record DuplicateTenantCommand(
    Guid SourceTenantId,
    Guid NewTenantId,
    string NewTenantName,
    string Hostname,
    string FrontendOrigin,
    string AuthorizationApiSecretKey,
    string InternalServiceAuthSecretKey,
    IReadOnlyList<(string Email, string ApiKey)> InternalServiceAuthServiceApiKeys)
    : IRequest<Result<DuplicateTenantResponse>>;

internal sealed class DuplicateTenantCommandValidator : AbstractValidator<DuplicateTenantCommand>
{
    public DuplicateTenantCommandValidator()
    {
        RuleFor(x => x.SourceTenantId).NotEmpty();
        RuleFor(x => x.NewTenantId).NotEmpty();
        RuleFor(x => x.NewTenantName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(255);
        RuleFor(x => x.FrontendOrigin).NotEmpty().MaximumLength(500);
        RuleFor(x => x.AuthorizationApiSecretKey).NotEmpty().MinimumLength(32);
        RuleFor(x => x.InternalServiceAuthSecretKey).NotEmpty().MinimumLength(32);
        RuleForEach(x => x.InternalServiceAuthServiceApiKeys).ChildRules(service =>
        {
            service.RuleFor(s => s.Email).NotEmpty();
            service.RuleFor(s => s.ApiKey).NotEmpty().MinimumLength(32);
        });
    }
}

/// <summary>
/// Creates a new TenantConfig tenant by copying settings from the caller's current tenant.
/// Interactive SuperAdmin only. Principals are not copied.
/// </summary>
public sealed class DuplicateTenantCommandHandler(
    ITenantDuplicator tenantDuplicator,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider)
    : IRequestHandler<DuplicateTenantCommand, Result<DuplicateTenantResponse>>
{
    public async Task<Result<DuplicateTenantResponse>> Handle(
        DuplicateTenantCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<DuplicateTenantResponse>.Forbid(
                "Only interactive SuperAdmin users can duplicate tenants.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<DuplicateTenantResponse>.Forbid(
                "Tenant context is required to duplicate a tenant.");
        }

        if (currentTenant.Id != request.SourceTenantId)
        {
            return Result<DuplicateTenantResponse>.Forbid(
                $"Cannot duplicate tenant '{request.SourceTenantId}'. " +
                $"Administrators may only duplicate their own tenant ('{currentTenant.Id}').");
        }

        try
        {
            var result = await tenantDuplicator.DuplicateAsync(
                request.SourceTenantId,
                request.NewTenantId,
                request.NewTenantName,
                request.Hostname,
                request.FrontendOrigin,
                request.AuthorizationApiSecretKey,
                request.InternalServiceAuthSecretKey,
                request.InternalServiceAuthServiceApiKeys,
                cancellationToken);

            await tenantConfigProvider.RefreshAsync(cancellationToken);

            return Result<DuplicateTenantResponse>.Success(
                new DuplicateTenantResponse(
                    result.SourceTenantId,
                    result.NewTenantId,
                    result.NewTenantName,
                    result.Hostname,
                    result.FrontendOrigin,
                    result.SettingsCopied,
                    $"Tenant '{result.NewTenantName}' created with {result.SettingsCopied} setting(s). " +
                    "Review secrets, identity providers and principals before using the new tenant in production."));
        }
        catch (KeyNotFoundException ex)
        {
            return Result<DuplicateTenantResponse>.NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<DuplicateTenantResponse>.Validation(ex.Message);
        }
    }
}
