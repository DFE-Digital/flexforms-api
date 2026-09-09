using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using MockQueryable;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class LeadApplicantLookupTests
{
    private readonly IApplicationRepository _applicationRepository = Substitute.For<IApplicationRepository>();
    private readonly IEaRepository<User> _userRepository = Substitute.For<IEaRepository<User>>();
    private readonly ILogger _logger = Substitute.For<ILogger>();

    public LeadApplicantLookupTests()
    {
        _applicationRepository.Query().Returns(Array.Empty<Domain.Entities.Application>().AsQueryable().BuildMock());
        _userRepository.Query().Returns(Array.Empty<User>().AsQueryable().BuildMock());
    }

    [Fact]
    public async Task GetNameAsync_ReturnsCreatorName_WhenApplicationAndUserExist()
    {
        var applicationId = new ApplicationId(Guid.NewGuid());
        var leadId = new UserId(Guid.NewGuid());
        var lead = CreateUser(leadId, "Ada Lead");

        _applicationRepository.Query().Returns(new[]
        {
            new Domain.Entities.Application(
                applicationId,
                "APP-REF",
                new TemplateVersionId(Guid.NewGuid()),
                DateTime.UtcNow,
                leadId,
                GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ApplicationStatus.InProgress)
        }.AsQueryable().BuildMock());
        _userRepository.Query().Returns(new[] { lead }.AsQueryable().BuildMock());

        var name = await LeadApplicantLookup.GetNameAsync(
            _applicationRepository, _userRepository, applicationId, _logger, CancellationToken.None);

        Assert.Equal("Ada Lead", name);
    }

    [Fact]
    public async Task GetNameAsync_ReturnsEmpty_WhenApplicationIsMissing()
    {
        var name = await LeadApplicantLookup.GetNameAsync(
            _applicationRepository,
            _userRepository,
            new ApplicationId(Guid.NewGuid()),
            _logger,
            CancellationToken.None);

        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public async Task GetNameAsync_ReturnsEmpty_WhenCreatorUserIsMissing()
    {
        var applicationId = new ApplicationId(Guid.NewGuid());

        _applicationRepository.Query().Returns(new[]
        {
            new Domain.Entities.Application(
                applicationId,
                "APP-REF",
                new TemplateVersionId(Guid.NewGuid()),
                DateTime.UtcNow,
                new UserId(Guid.NewGuid()),
                GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ApplicationStatus.InProgress)
        }.AsQueryable().BuildMock());

        var name = await LeadApplicantLookup.GetNameAsync(
            _applicationRepository, _userRepository, applicationId, _logger, CancellationToken.None);

        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public async Task GetNameAsync_ReturnsEmpty_WhenQueryThrows()
    {
        _applicationRepository.Query().Returns(_ => throw new InvalidOperationException("db down"));

        var name = await LeadApplicantLookup.GetNameAsync(
            _applicationRepository,
            _userRepository,
            new ApplicationId(Guid.NewGuid()),
            _logger,
            CancellationToken.None);

        Assert.Equal(string.Empty, name);
    }

    private static User CreateUser(UserId id, string name) =>
        new(id, new RoleId(Guid.NewGuid()), name, "ada@example.com", DateTime.UtcNow, null, null, null);
}
