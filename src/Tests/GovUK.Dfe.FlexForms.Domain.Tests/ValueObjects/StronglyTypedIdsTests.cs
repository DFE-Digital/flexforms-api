using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using System;
using GovUK.Dfe.FlexForms.Domain.Common;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Tests.ValueObjects;

public class StronglyTypedIdsTests
{
    [Theory]
    [CustomAutoData]
    public void ApplicationId_Should_Be_Equal_When_Same_Value(Guid value)
    {
        // Arrange
        var id1 = new ApplicationId(value);
        var id2 = new ApplicationId(value);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void ApplicationId_Should_Not_Be_Equal_When_Different_Values(Guid value1, Guid value2)
    {
        // Arrange
        var id1 = new ApplicationId(value1);
        var id2 = new ApplicationId(value2);

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void UserId_Should_Be_Equal_When_Same_Value(Guid value)
    {
        // Arrange
        var id1 = new UserId(value);
        var id2 = new UserId(value);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void UserId_Should_Not_Be_Equal_When_Different_Values(Guid value1, Guid value2)
    {
        // Arrange
        var id1 = new UserId(value1);
        var id2 = new UserId(value2);

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void FileId_Should_Be_Equal_When_Same_Value(Guid value)
    {
        // Arrange
        var id1 = new FileId(value);
        var id2 = new FileId(value);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void FileId_Should_Not_Be_Equal_When_Different_Values(Guid value1, Guid value2)
    {
        // Arrange
        var id1 = new FileId(value1);
        var id2 = new FileId(value2);

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void TemplateId_Should_Be_Equal_When_Same_Value(Guid value)
    {
        // Arrange
        var id1 = new TemplateId(value);
        var id2 = new TemplateId(value);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void TemplateId_Should_Not_Be_Equal_When_Different_Values(Guid value1, Guid value2)
    {
        // Arrange
        var id1 = new TemplateId(value1);
        var id2 = new TemplateId(value2);

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void TemplateVersionId_Should_Be_Equal_When_Same_Value(Guid value)
    {
        // Arrange
        var id1 = new TemplateVersionId(value);
        var id2 = new TemplateVersionId(value);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void TemplateVersionId_Should_Not_Be_Equal_When_Different_Values(Guid value1, Guid value2)
    {
        // Arrange
        var id1 = new TemplateVersionId(value1);
        var id2 = new TemplateVersionId(value2);

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void RoleId_Should_Be_Equal_When_Same_Value(Guid value)
    {
        // Arrange
        var id1 = new RoleId(value);
        var id2 = new RoleId(value);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void RoleId_Should_Not_Be_Equal_When_Different_Values(Guid value1, Guid value2)
    {
        // Arrange
        var id1 = new RoleId(value1);
        var id2 = new RoleId(value2);

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void PermissionId_Should_Be_Equal_When_Same_Value(Guid value)
    {
        // Arrange
        var id1 = new PermissionId(value);
        var id2 = new PermissionId(value);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void PermissionId_Should_Not_Be_Equal_When_Different_Values(Guid value1, Guid value2)
    {
        // Arrange
        var id1 = new PermissionId(value1);
        var id2 = new PermissionId(value2);

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    [Theory]
    [CustomAutoData]
    public void All_Ids_Should_Implement_IStronglyTypedId()
    {
        // Arrange & Act
        var applicationId = new ApplicationId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var fileId = new FileId(Guid.NewGuid());
        var templateId = new TemplateId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var permissionId = new PermissionId(Guid.NewGuid());

        // Assert
        Assert.IsAssignableFrom<IStronglyTypedId>(applicationId);
        Assert.IsAssignableFrom<IStronglyTypedId>(userId);
        Assert.IsAssignableFrom<IStronglyTypedId>(fileId);
        Assert.IsAssignableFrom<IStronglyTypedId>(templateId);
        Assert.IsAssignableFrom<IStronglyTypedId>(templateVersionId);
        Assert.IsAssignableFrom<IStronglyTypedId>(roleId);
        Assert.IsAssignableFrom<IStronglyTypedId>(permissionId);
    }

    [Theory]
    [CustomAutoData]
    public void All_Ids_Should_Have_Correct_Value_Property()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var applicationId = new ApplicationId(guid);
        var userId = new UserId(guid);
        var fileId = new FileId(guid);
        var templateId = new TemplateId(guid);
        var templateVersionId = new TemplateVersionId(guid);
        var roleId = new RoleId(guid);
        var permissionId = new PermissionId(guid);

        // Assert
        Assert.Equal(guid, applicationId.Value);
        Assert.Equal(guid, userId.Value);
        Assert.Equal(guid, fileId.Value);
        Assert.Equal(guid, templateId.Value);
        Assert.Equal(guid, templateVersionId.Value);
        Assert.Equal(guid, roleId.Value);
        Assert.Equal(guid, permissionId.Value);
    }
} 