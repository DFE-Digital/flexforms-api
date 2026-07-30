using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using System;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Factories;

public class UserFactoryTests
{
    private readonly UserFactory _factory = new();

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Create_User_With_Valid_Parameters(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act
        var user = _factory.CreateContributor(id, roleId, name, email, createdBy, applicationId, "APP-REF-001", templateId, createdOn);

        // Assert
        Assert.Equal(id, user.Id);
        Assert.Equal(roleId, user.RoleId);
        Assert.Equal(name, user.Name);
        Assert.Equal(email, user.Email);
        Assert.Equal(createdBy, user.CreatedBy);
        Assert.Equal(createdOn, user.CreatedOn);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Use_Current_DateTime_When_CreatedOn_Is_Null(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId)
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var user = _factory.CreateContributor(id, roleId, name, email, createdBy, applicationId, "APP-REF-001", templateId, null);

        // Assert
        Assert.Equal(id, user.Id);
        Assert.Equal(roleId, user.RoleId);
        Assert.Equal(name, user.Name);
        Assert.Equal(email, user.Email);
        Assert.Equal(createdBy, user.CreatedBy);
        Assert.True(user.CreatedOn >= beforeCreation);
        Assert.True(user.CreatedOn <= DateTime.UtcNow);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_Id_Is_Null(
        RoleId roleId,
        string name,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(null!, roleId, name, email, createdBy, applicationId, "APP-REF-001", templateId, createdOn));
        Assert.Equal("Id cannot be null (Parameter 'id')", exception.Message);
        Assert.Equal("id", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_RoleId_Is_Null(
        UserId id,
        string name,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, null!, name, email, createdBy, applicationId, "APP-REF-001", templateId, createdOn));
        Assert.Equal("RoleId cannot be null (Parameter 'roleId')", exception.Message);
        Assert.Equal("roleId", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_Name_Is_Null(
        UserId id,
        RoleId roleId,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, roleId, null!, email, createdBy, applicationId, "APP-REF-001", templateId, createdOn));
        Assert.Equal("Name cannot be null or empty (Parameter 'name')", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_Name_Is_Empty(
        UserId id,
        RoleId roleId,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, roleId, "", email, createdBy, applicationId, "APP-REF-001", templateId, createdOn));
        Assert.Equal("Name cannot be null or empty (Parameter 'name')", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_Email_Is_Null(
        UserId id,
        RoleId roleId,
        string name,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, roleId, name, null!, createdBy, applicationId, "APP-REF-001", templateId, createdOn));
        Assert.Equal("Email cannot be null or empty (Parameter 'email')", exception.Message);
        Assert.Equal("email", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_Email_Is_Empty(
        UserId id,
        RoleId roleId,
        string name,
        UserId createdBy,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, roleId, name, "", createdBy, applicationId, "APP-REF-001", templateId, createdOn));
        Assert.Equal("Email cannot be null or empty (Parameter 'email')", exception.Message);
        Assert.Equal("email", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_CreatedBy_Is_Null(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        ApplicationId applicationId,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, roleId, name, email, null!, applicationId, "APP-REF-001", templateId, createdOn));
        Assert.Equal("CreatedBy cannot be null (Parameter 'createdBy')", exception.Message);
        Assert.Equal("createdBy", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_ApplicationId_Is_Null(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        UserId createdBy,
        TemplateId templateId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, roleId, name, email, createdBy, null!, "APP-REF-001", templateId, createdOn));
        Assert.Equal("ApplicationId cannot be null (Parameter 'applicationId')", exception.Message);
        Assert.Equal("applicationId", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void CreateContributor_Should_Throw_ArgumentException_When_TemplateId_Is_Null(
        UserId id,
        RoleId roleId,
        string name,
        string email,
        UserId createdBy,
        ApplicationId applicationId,
        DateTime createdOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.CreateContributor(id, roleId, name, email, createdBy, applicationId, "APP-REF-001", null!, createdOn));
        Assert.Equal("TemplateId cannot be null (Parameter 'templateId')", exception.Message);
        Assert.Equal("templateId", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void AddPermissionToUser_Should_Add_Permission_To_User(
        User user,
        string resourceKey,
        ResourceType resourceType,
        AccessType[] accessTypes,
        UserId grantedBy,
        ApplicationId applicationId,
        DateTime grantedOn)
    {
        // Arrange
        var initialPermissionCount = user.Permissions.Count;

        // Act
        _factory.AddPermissionToUser(user, resourceKey, resourceType, accessTypes, grantedBy, applicationId, grantedOn);

        // Assert
        Assert.Equal(initialPermissionCount + accessTypes.Length, user.Permissions.Count);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void AddPermissionToUser_Should_Throw_ArgumentException_When_User_Is_Null(
        string resourceKey,
        ResourceType resourceType,
        AccessType[] accessTypes,
        UserId grantedBy,
        ApplicationId applicationId,
        DateTime grantedOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.AddPermissionToUser(null!, resourceKey, resourceType, accessTypes, grantedBy, applicationId, grantedOn));
        Assert.Equal("User cannot be null (Parameter 'user')", exception.Message);
        Assert.Equal("user", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void AddTemplatePermissionToUser_Should_Add_Template_Permission_To_User(
        User user,
        Guid templateId,
        AccessType[] accessTypes,
        UserId grantedBy,
        DateTime grantedOn)
    {
        // Arrange
        var initialTemplatePermissionCount = user.Permissions.Count(p => p.ResourceType == ResourceType.Template);

        // Act
        _factory.AddTemplatePermissionToUser(user, templateId.ToString(), accessTypes, grantedBy, grantedOn);

        // Assert
        Assert.Equal(
            initialTemplatePermissionCount + accessTypes.Length,
            user.Permissions.Count(p => p.ResourceType == ResourceType.Template));
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void AddTemplatePermissionToUser_Should_Throw_ArgumentException_When_User_Is_Null(
        string templateId,
        AccessType[] accessTypes,
        UserId grantedBy,
        DateTime grantedOn)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _factory.AddTemplatePermissionToUser(null!, templateId, accessTypes, grantedBy, grantedOn));
        Assert.Equal("User cannot be null (Parameter 'user')", exception.Message);
        Assert.Equal("user", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void RemovePermissionFromUser_Should_Remove_Permission_From_User(
        User user,
        Permission permission)
    {
        // Arrange
        _factory.AddPermissionToUser(user, "test-resource", ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()));
        var permissionToRemove = user.Permissions.First();

        // Act
        var result = _factory.RemovePermissionFromUser(user, permissionToRemove);

        // Assert
        Assert.True(result);
        Assert.DoesNotContain(permissionToRemove, user.Permissions);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void RemovePermissionFromUser_Should_Throw_ArgumentException_When_User_Is_Null(Permission permission)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _factory.RemovePermissionFromUser(null!, permission));
        Assert.Equal("User cannot be null (Parameter 'user')", exception.Message);
        Assert.Equal("user", exception.ParamName);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void RemovePermissionFromUser_Should_Throw_ArgumentException_When_Permission_Is_Null(User user)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _factory.RemovePermissionFromUser(user, null!));
        Assert.Equal("Permission cannot be null (Parameter 'permission')", exception.Message);
        Assert.Equal("permission", exception.ParamName);
    }
} 