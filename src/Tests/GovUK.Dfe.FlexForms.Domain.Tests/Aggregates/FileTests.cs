using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using System;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Aggregates;

public class FileTests
{
    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Create_File_With_Valid_Parameters(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Act
        var file = new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);

        // Assert
        Assert.Equal(id, file.Id);
        Assert.Equal(applicationId, file.ApplicationId);
        Assert.Equal(name, file.Name);
        Assert.Equal(description, file.Description);
        Assert.Equal(originalFileName, file.OriginalFileName);
        Assert.Equal(fileName, file.FileName);
        Assert.Equal(path, file.Path);
        Assert.Equal(uploadedOn, file.UploadedOn);
        Assert.Equal(uploadedBy, file.UploadedBy);
        Assert.False(file.IsDeleted);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Throw_ArgumentNullException_When_Id_Is_Null(
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new File(null!, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize));
        Assert.Equal("id", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Throw_ArgumentNullException_When_ApplicationId_Is_Null(
        FileId id,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new File(id, null!, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize));
        Assert.Equal("applicationId", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Throw_ArgumentNullException_When_Name_Is_Null(
        FileId id,
        ApplicationId applicationId,
        string description,
        string originalFileName,
        string fileName,
        string path,
        long fileSize,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new File(id, applicationId, null!, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize));
        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Throw_ArgumentNullException_When_OriginalFileName_Is_Null(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new File(id, applicationId, name, description, null!, fileName, path, uploadedOn, uploadedBy, fileSize));
        Assert.Equal("originalFileName", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Throw_ArgumentNullException_When_FileName_Is_Null(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string path,
        long fileSize,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new File(id, applicationId, name, description, originalFileName, null!, path, uploadedOn, uploadedBy, fileSize));
        Assert.Equal("fileName", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Throw_ArgumentNullException_When_UploadedBy_Is_Null(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, null!, fileSize));
        Assert.Equal("uploadedBy", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Delete_Should_Set_IsDeleted_To_True_When_Not_Already_Deleted(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        string path,
        long fileSize,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Arrange
        var file = new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);

        // Act
        file.Delete();

        // Assert
        Assert.True(file.IsDeleted);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Delete_Should_Throw_InvalidOperationException_When_Already_Deleted(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        string path,
        long fileSize,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Arrange
        var file = new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);
        file.Delete(); // First deletion

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => file.Delete());
        Assert.Equal("File is already deleted.", exception.Message);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Allow_Null_Description(
        FileId id,
        ApplicationId applicationId,
        string name,
        string originalFileName,
        string fileName,
        string path,
        long fileSize,

        DateTime uploadedOn,
        UserId uploadedBy)
    {
        // Act
        var file = new File(id, applicationId, name, null, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);

        // Assert
        Assert.Null(file.Description);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void Constructor_Should_Default_ValidationStatus_To_NotRequired(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        var file = new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);

        Assert.Equal(GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.FileValidationStatus.NotRequired, file.ValidationStatus);
        Assert.Null(file.ValidationMessage);
        Assert.Null(file.ValidatedOn);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void RequireExternalValidation_Should_Set_Pending(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        var file = new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);

        file.RequireExternalValidation();

        Assert.Equal(GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.FileValidationStatus.Pending, file.ValidationStatus);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void RecordValidationResult_Should_Set_Failed_And_Raise_Event(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        var file = new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);
        file.RequireExternalValidation();
        var reportedAt = DateTime.UtcNow;

        file.RecordValidationResult(false, "Missing Amount column", reportedAt, "excel-fn");

        Assert.Equal(GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.FileValidationStatus.Failed, file.ValidationStatus);
        Assert.Equal("Missing Amount column", file.ValidationMessage);
        Assert.Equal(reportedAt, file.ValidatedOn);
        Assert.Equal("excel-fn", file.ValidationSource);
        Assert.Contains(file.DomainEvents, e => e is GovUK.Dfe.FlexForms.Domain.Events.FileValidationRecordedEvent);
    }

    [Theory]
    [CustomAutoData(typeof(FileCustomization))]
    public void RecordValidationResult_Should_Throw_When_NotRequired(
        FileId id,
        ApplicationId applicationId,
        string name,
        string description,
        string originalFileName,
        string fileName,
        long fileSize,
        string path,
        DateTime uploadedOn,
        UserId uploadedBy)
    {
        var file = new File(id, applicationId, name, description, originalFileName, fileName, path, uploadedOn, uploadedBy, fileSize);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            file.RecordValidationResult(true, null, DateTime.UtcNow, null));

        Assert.Equal("File does not require external validation.", ex.Message);
    }
} 