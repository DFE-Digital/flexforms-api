using System.Diagnostics.CodeAnalysis;
using Asp.Versioning;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/diagnostics")]
[ExcludeFromCodeCoverage]
public class DiagnosticsController(IHostEnvironment env) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    public IActionResult Get()
    {
        var asm = typeof(Program).Assembly;

        var informationalVersion =
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var version = informationalVersion?.Split('+', 2)[0];
        var buildMetadata = informationalVersion?.Contains('+') == true
            ? informationalVersion.Split('+', 2)[1]
            : null;

        return Ok(new
        {
            service = asm.GetName().Name,
            environment = env.EnvironmentName,
            version = version ?? "unknown",
            informationalVersion,
            buildMetadata
        });
    }
}


