using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Requires Admin/SuperAdmin or <c>Template:Manage:Write</c> to administer templates.
/// </summary>
public sealed class TemplateManagePermissionRequirement : IAuthorizationRequirement;
