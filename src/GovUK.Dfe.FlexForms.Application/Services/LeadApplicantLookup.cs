using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Resolves the lead applicant — the user who created the application — for email personalisation.
/// </summary>
public static class LeadApplicantLookup
{
    public const string BaselinePlaceholderKey = "lead_applicant_name";

    public static async Task<string> GetNameAsync(
        IApplicationRepository applicationRepository,
        IEaRepository<User> userRepository,
        ApplicationId applicationId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var createdBy = await applicationRepository.Query()
                .AsNoTracking()
                .Where(a => a.Id == applicationId)
                .Select(a => a.CreatedBy)
                .FirstOrDefaultAsync(cancellationToken);

            if (createdBy is null)
                return string.Empty;

            var leadApplicant = await new GetUserByIdQueryObject(createdBy)
                .Apply(userRepository.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            return leadApplicant?.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not resolve lead applicant name for application {ApplicationId}",
                applicationId.Value);
            return string.Empty;
        }
    }
}
