using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Requires Admin/SuperAdmin or a template Manage claim to administer templates.
/// </summary>
public sealed class TemplateManagePermissionRequirement : IAuthorizationRequirement;
