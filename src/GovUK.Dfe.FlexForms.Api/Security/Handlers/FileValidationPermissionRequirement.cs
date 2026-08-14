using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Requires a service principal that holds a FileValidation Write grant.
/// Admin roles do not satisfy this requirement.
/// </summary>
public sealed class FileValidationPermissionRequirement : IAuthorizationRequirement;
