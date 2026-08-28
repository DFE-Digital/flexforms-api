using System.Collections.Concurrent;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Providers;
using GovUK.Dfe.CoreLibs.Email.Services;
using GovUK.Dfe.CoreLibs.Email.Settings;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Builds per-tenant <see cref="IEmailService"/> from TenantConfig Email settings
/// (GovUkNotify ApiKey). Host GlobalConfiguration Email is used only to register CoreLibs DI.
/// </summary>
public interface ITenantEmailServiceFactory
{
    IEmailService GetRequiredEmailService();
}

public sealed class TenantEmailServiceFactory(
    IHttpContextAccessor httpContextAccessor,
    ILoggerFactory loggerFactory,
    ILogger<TenantEmailServiceFactory> logger) : ITenantEmailServiceFactory
{
    private readonly ConcurrentDictionary<string, IEmailService> _cache = new(StringComparer.Ordinal);

    public IEmailService GetRequiredEmailService()
    {
        var tenant = GetCurrentTenant();
        TenantEmailConfiguration.EnsureConfiguredForTenant(tenant);

        var provider = tenant.Settings.GetValue<string>("Email:Provider")!;
        if (!string.Equals(provider, "GovUkNotify", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' Email Provider '{provider}' is not supported. Expected GovUkNotify.");
        }

        var apiKey = tenant.Settings.GetValue<string>("Email:GovUkNotify:ApiKey")!;
        var baseUrl = tenant.Settings.GetValue<string>("Email:GovUkNotify:BaseUrl");
        var notifyTimeout = tenant.Settings.GetValue<int?>("Email:GovUkNotify:TimeoutSeconds") ?? 30;
        var serviceTimeout = tenant.Settings.GetValue<int?>("Email:TimeoutSeconds") ?? notifyTimeout;
        var supportEmail = tenant.Settings.GetValue<string>("Email:ServiceSupportEmailAddress");
        var defaultFrom = tenant.Settings.GetValue<string>("Email:DefaultFromEmail");
        var defaultFromName = tenant.Settings.GetValue<string>("Email:DefaultFromName");

        // Cache by tenant + key material so key rotation gets a new client.
        var cacheKey = $"{tenant.Id:N}|{apiKey}|{baseUrl}|{notifyTimeout}|{serviceTimeout}";

        return _cache.GetOrAdd(cacheKey, _ =>
        {
            logger.LogInformation(
                "Creating GOV.UK Notify email client for tenant {TenantName}",
                tenant.Name);

            var emailOptions = new EmailOptions
            {
                Provider = "GovUkNotify",
                ServiceSupportEmailAddress = supportEmail,
                DefaultFromEmail = defaultFrom,
                DefaultFromName = defaultFromName,
                TimeoutSeconds = serviceTimeout > 0 ? serviceTimeout : 30,
                GovUkNotify = new GovUkNotifyOptions
                {
                    ApiKey = apiKey,
                    BaseUrl = baseUrl,
                    TimeoutSeconds = notifyTimeout > 0 ? notifyTimeout : 30
                }
            };

            var options = Microsoft.Extensions.Options.Options.Create(emailOptions);
            var notificationClient = new NotificationClientWrapper(apiKey);
            var providerLogger = loggerFactory.CreateLogger<GovUkNotifyEmailProvider>();
            var emailProvider = new GovUkNotifyEmailProvider(options, providerLogger, notificationClient);
            var emailLogger = loggerFactory.CreateLogger<EmailService>();
            return new EmailService(emailProvider, options, emailLogger);
        });
    }

    private TenantConfiguration GetCurrentTenant()
    {
        var requestServices = httpContextAccessor.HttpContext?.RequestServices
            ?? throw new InvalidOperationException("No HttpContext available for tenant email service.");

        var tenantContextAccessor = requestServices.GetRequiredService<ITenantContextAccessor>();
        return tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException("No tenant context available for email operation.");
    }
}
