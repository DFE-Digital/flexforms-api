using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Utilities.RateLimiting;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

public sealed record RevealTenantSettingSecretCommand(
    Guid TenantId,
    string Category,
    string Target,
    string Path,
    string Reason) : IRequest<Result<RevealTenantSettingSecretResponse>>;

internal sealed class RevealTenantSettingSecretCommandValidator : AbstractValidator<RevealTenantSettingSecretCommand>
{
    public RevealTenantSettingSecretCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Target)
            .NotEmpty()
            .Must(t => t is "Shared" or "Api" or "Web")
            .WithMessage("Target must be one of: Shared, Api, Web.");
        RuleFor(x => x.Path).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(400)
            .WithMessage("A reason of at least 10 characters is required.");
    }
}

/// <summary>
/// Break-glass: returns one secret leaf to an interactive SuperAdmin.
/// Rate-limited and written to the tenant setting audit log. Never used by the Web UI.
/// </summary>
public sealed class RevealTenantSettingSecretCommandHandler(
    ITenantSettingsQuery settingsQuery,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantSettingAuditWriter auditWriter,
    IHttpContextAccessor httpContextAccessor,
    IRateLimiterFactory<string> rateLimiterFactory)
    : IRequestHandler<RevealTenantSettingSecretCommand, Result<RevealTenantSettingSecretResponse>>
{
    private const int MaxReveals = 5;
    private static readonly TimeSpan RevealWindow = TimeSpan.FromMinutes(10);

    public async Task<Result<RevealTenantSettingSecretResponse>> Handle(
        RevealTenantSettingSecretCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<RevealTenantSettingSecretResponse>.Forbid(
                "Only interactive SuperAdmin users can reveal tenant setting secrets.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<RevealTenantSettingSecretResponse>.Forbid(
                "Administrators may only reveal secrets for their own tenant.");
        }

        var actorEmail = ResolveActorEmail();
        var limiter = rateLimiterFactory.Create(MaxReveals, RevealWindow);
        if (!limiter.IsAllowed($"reveal:{actorEmail}"))
        {
            throw new RateLimitExceededException(
                "Too many secret reveal requests. Please retry later.");
        }

        var list = await settingsQuery.ListSettingsAsync(request.TenantId, cancellationToken);
        if (list is null)
        {
            return Result<RevealTenantSettingSecretResponse>.NotFound(
                $"Tenant '{request.TenantId}' was not found.");
        }

        var category = request.Category.Trim();
        var target = request.Target.Trim();
        var path = request.Path.Trim();
        var existing = list.Settings.FirstOrDefault(s =>
            string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return Result<RevealTenantSettingSecretResponse>.NotFound(
                $"Setting '{category}' (Target={target}) was not found.");
        }

        if (!existing.IsSecret)
        {
            return Result<RevealTenantSettingSecretResponse>.Validation(
                "This setting is not secret. Use GET settings to read non-secret values.");
        }

        if (!TenantSettingSecretJson.TryGetValueAtPath(existing.SettingsJson, path, out var value))
        {
            return Result<RevealTenantSettingSecretResponse>.NotFound(
                $"No string value exists at path '{path}'.");
        }

        var reason = request.Reason.Trim();
        var details = Truncate($"{path} — {reason}", 500);

        await auditWriter.AppendAsync(
            request.TenantId,
            category,
            target,
            TenantSettingSecretRedaction.RevealAuditAction,
            actorEmail,
            wasSecret: true,
            cancellationToken,
            details);

        var revealedAt = DateTime.UtcNow;
        return Result<RevealTenantSettingSecretResponse>.Success(
            new RevealTenantSettingSecretResponse(
                category,
                target,
                path,
                value,
                value.Length,
                TenantSettingSecretJson.Fingerprint(value),
                revealedAt,
                actorEmail));
    }

    private string ResolveActorEmail()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user?.FindFirst(ClaimTypes.Email)?.Value
               ?? user?.FindFirst("email")?.Value
               ?? user?.Identity?.Name
               ?? "unknown";
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
