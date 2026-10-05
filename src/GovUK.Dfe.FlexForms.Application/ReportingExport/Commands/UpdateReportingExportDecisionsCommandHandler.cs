using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

public sealed record UpdateReportingExportDecisionsCommand(
    Guid TenantId,
    Guid TemplateId,
    IReadOnlyList<ReportingExportDecisionRequest> Decisions)
    : IRequest<Result<ReportingExportChangeResultDto>>;

public sealed class UpdateReportingExportDecisionsCommandValidator : AbstractValidator<UpdateReportingExportDecisionsCommand>
{
    public UpdateReportingExportDecisionsCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.Decisions)
            .NotEmpty().WithMessage("Choose at least one field to change.")
            .Must(d => d.Count <= 500).WithMessage("Change at most 500 fields at a time.");
        RuleForEach(x => x.Decisions).ChildRules(decision =>
        {
            decision.RuleFor(d => d.FieldId).NotEmpty().MaximumLength(200);
            decision.RuleFor(d => d.ParentFieldId).MaximumLength(200);
            decision.RuleFor(d => d.Decision).IsInEnum();
            decision.RuleFor(d => d.Reason).MaximumLength(1000);
        });
    }
}

/// <summary>Allows or denies fields of a template. A change starts a refresh of the tenant's exported data.</summary>
public sealed class UpdateReportingExportDecisionsCommandHandler(IReportingExportAccess access, IPrismExportControlClient prism)
    : IRequestHandler<UpdateReportingExportDecisionsCommand, Result<ReportingExportChangeResultDto>>
{
    public async Task<Result<ReportingExportChangeResultDto>> Handle(UpdateReportingExportDecisionsCommand request, CancellationToken cancellationToken)
    {
        if (await access.CheckAsync(request.TenantId, request.TemplateId, cancellationToken) is { } denial)
        {
            return denial.As<ReportingExportChangeResultDto>();
        }

        return await prism.ChangeDecisionsAsync(request.TenantId, request.TemplateId, request.Decisions, access.ActingUser, cancellationToken);
    }
}
