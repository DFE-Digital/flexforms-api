using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

/// <summary>Sets the tenant default when <see cref="TemplateId"/> is null, otherwise the template's.</summary>
public sealed record UpdateReportingExportDefaultCommand(
    Guid TenantId,
    Guid? TemplateId,
    ReportingExportModeSetting Mode,
    string? Reason)
    : IRequest<Result<ReportingExportChangeResultDto>>;

public sealed class UpdateReportingExportDefaultCommandValidator : AbstractValidator<UpdateReportingExportDefaultCommand>
{
    public UpdateReportingExportDefaultCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEqual(Guid.Empty);
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(1000);
        RuleFor(x => x.Reason)
            .NotEmpty()
            .When(x => x.Mode == ReportingExportModeSetting.ExportAll)
            .WithMessage("Give a reason for exporting new fields automatically, for example who agreed it.");
    }
}

/// <summary>Changes what happens to undecided fields. A change starts a refresh of the tenant's exported data.</summary>
public sealed class UpdateReportingExportDefaultCommandHandler(IReportingExportAccess access, IPrismExportControlClient prism)
    : IRequestHandler<UpdateReportingExportDefaultCommand, Result<ReportingExportChangeResultDto>>
{
    public async Task<Result<ReportingExportChangeResultDto>> Handle(UpdateReportingExportDefaultCommand request, CancellationToken cancellationToken)
    {
        if (await access.CheckAsync(request.TenantId, request.TemplateId, cancellationToken) is { } denial)
        {
            return denial.As<ReportingExportChangeResultDto>();
        }

        var body = new UpdateReportingExportDefaultRequest { Mode = request.Mode, Reason = request.Reason };
        return await prism.ChangeDefaultAsync(request.TenantId, request.TemplateId, body, access.ActingUser, cancellationToken);
    }
}
