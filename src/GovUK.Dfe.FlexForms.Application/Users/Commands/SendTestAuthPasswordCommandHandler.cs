using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

public sealed record SendTestAuthPasswordCommand(string Email) : IRequest<Result<bool>>;

public sealed class SendTestAuthPasswordCommandHandler(
    ILogger<SendTestAuthPasswordCommandHandler> logger,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthPasswordService testAuthPasswordService,
    IEmailTemplateResolver emailTemplateResolver,
    IEmailService emailService) : IRequestHandler<SendTestAuthPasswordCommand, Result<bool>>
{
    public const string EnvironmentPlaceholder = "environment";
    public const string TempPasswordPlaceholder = "temp_password";
    public const string ServiceNamePlaceholder = "service_name";
    public const string ServiceNameSettingKey = "Layout:ServiceName";

    public async Task<Result<bool>> Handle(SendTestAuthPasswordCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenantContextAccessor.CurrentTenant;
        if (!TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenant))
        {
            logger.LogWarning(
                "Test authentication password requested for {Email} but Test Authentication is not enabled in {Environment}",
                request.Email,
                hostEnvironment.EnvironmentName);
            return Result<bool>.Forbid("Test authentication is not enabled for this tenant.");
        }

        var emailTemplateId = await emailTemplateResolver.ResolveTenantEmailTemplateAsync(EmailTypes.TestAuthPassword);
        if (string.IsNullOrWhiteSpace(emailTemplateId))
        {
            throw new InvalidOperationException(
                $"Could not resolve GOV.UK Notify template for email type '{EmailTypes.TestAuthPassword}'. " +
                $"Add EmailTemplates:{{ProductKey}}:{EmailTypes.TestAuthPassword} to the tenant configuration.");
        }

        var email = request.Email.Trim();
        var password = await testAuthPasswordService.IssueAsync(email);
        if (password is null)
        {
            return Result<bool>.Success(true);
        }

        var emailMessage = new EmailMessage
        {
            ToEmail = email,
            TemplateId = emailTemplateId,
            Personalization = new Dictionary<string, object>
            {
                [EnvironmentPlaceholder] = hostEnvironment.EnvironmentName,
                [TempPasswordPlaceholder] = password,
                [ServiceNamePlaceholder] = ResolveServiceName(tenant!)
            }
        };

        try
        {
            await emailService.SendEmailAsync(emailMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send test authentication password email to {Email}", email);
            throw;
        }

        logger.LogInformation("Test authentication password email sent to {Email}", email);
        return Result<bool>.Success(true);
    }

    private static string ResolveServiceName(TenantConfiguration tenant)
    {
        var serviceName = tenant.Settings[ServiceNameSettingKey];
        return string.IsNullOrWhiteSpace(serviceName) ? tenant.Name : serviceName.Trim();
    }
}
