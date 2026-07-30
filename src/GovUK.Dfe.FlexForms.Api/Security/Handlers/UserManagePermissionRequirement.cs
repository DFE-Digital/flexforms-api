using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Requires Admin/SuperAdmin or a User Manage claim to administer users.
/// </summary>
public sealed class UserManagePermissionRequirement : IAuthorizationRequirement;
