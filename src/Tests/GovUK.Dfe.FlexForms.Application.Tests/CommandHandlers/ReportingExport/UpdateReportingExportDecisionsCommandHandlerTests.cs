using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.ReportingExport;

public class UpdateReportingExportDecisionsCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly ReportingExportChangeResultDto Applied = new(ReportingExportChangeStatus.Applied, 4, 1, [], Guid.NewGuid());

    private static readonly IReadOnlyList<ReportingExportDecisionRequest> Decisions =
        [new ReportingExportDecisionRequest { FieldId = "email", Decision = ReportingExportDecision.Denied, Reason = "Personal data" }];

    private readonly IReportingExportPolicyService _policyService = Substitute.For<IReportingExportPolicyService>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantTemplateResolver _templateResolver = Substitute.For<ITenantTemplateResolver>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly UpdateReportingExportDecisionsCommandHandler _handler;

    public UpdateReportingExportDecisionsCommandHandlerTests()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(new TenantConfiguration(TenantId, "Transfers", new ConfigurationBuilder().Build(), Array.Empty<string>()));
        _templateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateId), Arg.Any<CancellationToken>()).Returns(true);
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "ada@example.gov.uk")], "Test")),
        });
        _handler = new UpdateReportingExportDecisionsCommandHandler(
            _policyService, _tenantContext, _permissionChecker, _templateResolver, _httpContextAccessor);
    }

    [Fact]
    public async Task Handle_ShouldChangeDecisionsAsSignedInAdmin()
    {
        _policyService.ChangeDecisionsAsync(TenantId, TemplateId, Decisions, "ada@example.gov.uk", Arg.Any<CancellationToken>())
            .Returns(Applied);

        var result = await _handler.Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, Decisions), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(Applied, result.Value);
    }

    [Fact]
    public async Task Handle_ShouldUseFallbackEmailClaim_WhenStandardClaimIsBlank()
    {
        _httpContextAccessor.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, " "), new Claim("email", "grace@example.gov.uk")], "Test"));
        _policyService.ChangeDecisionsAsync(TenantId, TemplateId, Decisions, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Applied);

        await _handler.Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, Decisions), CancellationToken.None);

        await _policyService.Received(1).ChangeDecisionsAsync(
            TenantId, TemplateId, Decisions, "grace@example.gov.uk", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotInteractiveTenantAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

        var result = await _handler.Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, Decisions), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().ChangeDecisionsAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTemplateBelongsToAnotherTenant()
    {
        _templateResolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, Decisions), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        await _policyService.DidNotReceiveWithAnyArgs().ChangeDecisionsAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnValidation_WhenReportingRejectsTheChange()
    {
        _policyService.ChangeDecisionsAsync(TenantId, TemplateId, Decisions, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new ArgumentException("These fields are not in the template: email."));

        var result = await _handler.Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, Decisions), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Equal("These fields are not in the template: email.", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenReportingRefusesFlexForms()
    {
        _policyService.ChangeDecisionsAsync(TenantId, TemplateId, Decisions, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("FlexForms is not allowed to manage reporting export."));

        var result = await _handler.Handle(new UpdateReportingExportDecisionsCommand(TenantId, TemplateId, Decisions), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("FlexForms is not allowed to manage reporting export.", result.Error);
    }
}
