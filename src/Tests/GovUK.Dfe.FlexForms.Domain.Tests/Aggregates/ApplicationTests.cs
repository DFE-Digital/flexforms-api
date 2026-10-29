using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Aggregates;

public class ApplicationTests
{
    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Constructor_ShouldThrowArgumentNullException_WhenIdIsNull(
        string reference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        ApplicationStatus? status,
        DateTime? lastModifiedOn,
        UserId? lastModifiedBy)
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new Entities.Application(
                null!,       // id
                reference,
                templateVersionId,
                createdOn,
                createdBy,
                status,
                lastModifiedOn,
                lastModifiedBy));

        Assert.Equal("id", ex.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Constructor_ShouldThrowArgumentNullException_WhenReferenceIsNull(
        ApplicationId id,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        ApplicationStatus? status,
        DateTime? lastModifiedOn,
        UserId? lastModifiedBy)
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new Entities.Application(
                id,
                null!,       // reference
                templateVersionId,
                createdOn,
                createdBy,
                status,
                lastModifiedOn,
                lastModifiedBy));

        Assert.Equal("applicationReference", ex.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void UpdateLastModified_ShouldUpdateTracking_WhenValidParameters(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        DateTime lastModifiedOn,
        UserId lastModifiedBy)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy);

        // Act
        application.UpdateLastModified(lastModifiedOn, lastModifiedBy);

        // Assert
        Assert.Equal(lastModifiedOn, application.LastModifiedOn);
        Assert.Equal(lastModifiedBy, application.LastModifiedBy);
    }

    [Fact]
    public void ChangeOwner_PreservesTheSubmissionTimestamp_ForASubmittedApplication()
    {
        var originalOwner = new UserId(Guid.NewGuid());
        var newOwner = new UserId(Guid.NewGuid());
        var submittedOn = DateTime.UtcNow.AddDays(-2);
        var application = new Entities.Application(
            new ApplicationId(Guid.NewGuid()),
            "APP-100",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow.AddDays(-3),
            originalOwner,
            ApplicationStatus.Submitted,
            submittedOn,
            originalOwner);

        application.ChangeOwner(newOwner, DateTime.UtcNow, new UserId(Guid.NewGuid()));

        Assert.Equal(newOwner, application.CreatedBy);
        Assert.Equal(submittedOn, application.LastModifiedOn);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void UpdateLastModified_ShouldThrowArgumentNullException_WhenLastModifiedByIsNull(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        DateTime lastModifiedOn)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            application.UpdateLastModified(lastModifiedOn, null!));

        Assert.Equal("lastModifiedBy", ex.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void GetLatestResponse_ShouldReturnNull_WhenNoResponsesExist(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy);

        // Act
        var result = application.GetLatestResponse();

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void GetLatestResponse_ShouldReturnLatestResponse_WhenMultipleResponsesExist(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        string firstResponseBody,
        string secondResponseBody,
        UserId responseCreatedBy)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy);

        // Add responses manually using the AddResponse method
        var firstResponse = new ApplicationResponse(
            new ResponseId(Guid.NewGuid()),
            applicationId,
            firstResponseBody,
            createdOn,
            responseCreatedBy);

        var secondResponse = new ApplicationResponse(
            new ResponseId(Guid.NewGuid()),
            applicationId,
            secondResponseBody,
            createdOn.AddMinutes(1), // Ensure different timestamp
            responseCreatedBy);

        application.AddResponse(firstResponse);
        application.AddResponse(secondResponse);

        // Act
        var result = application.GetLatestResponse();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(secondResponse.Id, result.Id);
        Assert.Equal(secondResponseBody, result.ResponseBody);
        Assert.True(result.CreatedOn >= firstResponse.CreatedOn);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Submit_ShouldUpdateStatusAndLastModified_WhenValidParameters(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        TemplateId templateId,
        DateTime createdOn,
        UserId createdBy,
        DateTime submittedOn,
        UserId submittedBy)
    {
        // Arrange
        var templateVersion = new TemplateVersion(templateVersionId, templateId, "1.0", "{}", createdOn, createdBy);
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy,
            ApplicationStatus.InProgress); // Start with InProgress status
        
        // Set the TemplateVersion navigation property using reflection for testing
        typeof(Entities.Application)
            .GetProperty(nameof(Entities.Application.TemplateVersion))!
            .SetValue(application, templateVersion);

        // Act
        application.Submit(submittedOn, submittedBy, "test@example.com", "Test User");

        // Assert
        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(submittedOn, application.LastModifiedOn);
        Assert.Equal(submittedBy, application.LastModifiedBy);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Submit_ShouldThrowArgumentNullException_WhenSubmittedByIsNull(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        DateTime submittedOn)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy,
            ApplicationStatus.InProgress);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            application.Submit(submittedOn, null!, "test@example.com", "Test User"));

        Assert.Equal("submittedBy", ex.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Submit_ShouldThrowInvalidOperationException_WhenApplicationAlreadySubmitted(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        DateTime submittedOn,
        UserId submittedBy)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy,
            ApplicationStatus.Submitted); // Already submitted

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            application.Submit(submittedOn, submittedBy, "test@example.com", "Test User"));

        Assert.Equal("Application has already been submitted", ex.Message);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Submit_ShouldWorkFromDifferentStatuses_WhenNotAlreadySubmitted(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        TemplateId templateId,
        DateTime createdOn,
        UserId createdBy,
        DateTime submittedOn,
        UserId submittedBy)
    {
        // Create template version for navigation property
        var templateVersion = new TemplateVersion(templateVersionId, templateId, "1.0", "{}", createdOn, createdBy);
        
        // Test submitting from null status
        var applicationWithNullStatus = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy,
            null); // null status
        
        // Set the TemplateVersion navigation property using reflection for testing
        typeof(Entities.Application)
            .GetProperty(nameof(Entities.Application.TemplateVersion))!
            .SetValue(applicationWithNullStatus, templateVersion);

        applicationWithNullStatus.Submit(submittedOn, submittedBy, "test@example.com", "Test User");
        Assert.Equal(ApplicationStatus.Submitted, applicationWithNullStatus.Status);

        // Test submitting from InProgress status
        var applicationWithInProgress = new Entities.Application(
            new ApplicationId(Guid.NewGuid()),
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy,
            ApplicationStatus.InProgress);
        
        // Set the TemplateVersion navigation property using reflection for testing
        typeof(Entities.Application)
            .GetProperty(nameof(Entities.Application.TemplateVersion))!
            .SetValue(applicationWithInProgress, templateVersion);

        applicationWithInProgress.Submit(submittedOn, submittedBy, "test@example.com", "Test User");
        Assert.Equal(ApplicationStatus.Submitted, applicationWithInProgress.Status);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Submit_ShouldThrowArgumentException_WhenUserEmailIsNullOrEmpty(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        DateTime submittedOn,
        UserId submittedBy)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy,
            ApplicationStatus.InProgress);

        // Act & Assert - Test null email
        var ex1 = Assert.Throws<ArgumentException>(() =>
            application.Submit(submittedOn, submittedBy, null!, "Test User"));
        Assert.Equal("userEmail", ex1.ParamName);

        // Act & Assert - Test empty email
        var ex2 = Assert.Throws<ArgumentException>(() =>
            application.Submit(submittedOn, submittedBy, "", "Test User"));
        Assert.Equal("userEmail", ex2.ParamName);

        // Act & Assert - Test whitespace email
        var ex3 = Assert.Throws<ArgumentException>(() =>
            application.Submit(submittedOn, submittedBy, "   ", "Test User"));
        Assert.Equal("userEmail", ex3.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public void Submit_ShouldThrowArgumentException_WhenUserFullNameIsNullOrEmpty(
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        DateTime createdOn,
        UserId createdBy,
        DateTime submittedOn,
        UserId submittedBy)
    {
        // Arrange
        var application = new Entities.Application(
            applicationId,
            applicationReference,
            templateVersionId,
            createdOn,
            createdBy,
            ApplicationStatus.InProgress);

        // Act & Assert - Test null name
        var ex1 = Assert.Throws<ArgumentException>(() =>
            application.Submit(submittedOn, submittedBy, "test@example.com", null!));
        Assert.Equal("userFullName", ex1.ParamName);

        // Act & Assert - Test empty name
        var ex2 = Assert.Throws<ArgumentException>(() =>
            application.Submit(submittedOn, submittedBy, "test@example.com", ""));
        Assert.Equal("userFullName", ex2.ParamName);

        // Act & Assert - Test whitespace name
        var ex3 = Assert.Throws<ArgumentException>(() =>
            application.Submit(submittedOn, submittedBy, "test@example.com", "   "));
        Assert.Equal("userFullName", ex3.ParamName);
    }
}