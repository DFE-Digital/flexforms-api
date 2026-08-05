using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;

/// <summary>
/// Aggregate-root repository for <see cref="Application"/>.
/// </summary>
public interface IApplicationRepository : IEaRepository<Application>
{
    /// <summary>
    /// Returns the latest response for the given application, or null when none exist.
    /// </summary>
    Task<ApplicationResponse?> GetLatestResponseAsync(
        ApplicationId applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the latest response for each application id (apps with no responses are omitted).
    /// </summary>
    Task<IReadOnlyDictionary<ApplicationId, ApplicationResponse>> GetLatestResponsesAsync(
        IReadOnlyCollection<ApplicationId> applicationIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends a new response version to an application and updates last-modified tracking,
    /// without loading the full aggregate graph (e.g. historic responses).
    /// Returns null if the application does not exist.
    /// </summary>
    Task<(string ApplicationReference, ApplicationResponse Response)?> AppendResponseVersionAsync(
        ApplicationId applicationId,
        ApplicationResponse response,
        DateTime lastModifiedOn,
        UserId lastModifiedBy,
        CancellationToken cancellationToken);
}
