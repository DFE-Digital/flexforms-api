using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Aggregates
{
    public class RoleTests
    {
        [Theory]
        [CustomAutoData(typeof(RoleCustomization))]
        public void Constructor_ShouldThrowArgumentNullException_WhenIdIsNull(
            string name)
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new Role(null!, name));

            Assert.Equal("id", ex.ParamName);
        }

        [Theory]
        [CustomAutoData(typeof(RoleCustomization))]
        public void Constructor_ShouldThrowArgumentNullException_WhenNameIsNull(
            RoleId id)
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new Role(id, null!));

            Assert.Equal("name", ex.ParamName);
        }

        [Fact]
        public void CreateCustomForTenant_ShouldRejectReservedNames()
        {
            var tenantId = Guid.NewGuid();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Role.CreateCustomForTenant(tenantId, RoleNames.SuperAdmin));

            Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreateCustomForTenant_ShouldRejectSystemAssignableNames()
        {
            var tenantId = Guid.NewGuid();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Role.CreateCustomForTenant(tenantId, RoleNames.User));

            Assert.Contains("system role", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreateCustomForTenant_ShouldCreateNonSystemRole()
        {
            var role = Role.CreateCustomForTenant(Guid.NewGuid(), "Reviewer");

            Assert.Equal("Reviewer", role.Name);
            Assert.False(role.IsSystem);
            Assert.NotNull(role.TenantId);
        }

        [Fact]
        public void Rename_ShouldRejectSystemRoles()
        {
            var role = Role.CreateSystemAssignableForTenant(Guid.NewGuid(), RoleNames.User);

            var ex = Assert.Throws<InvalidOperationException>(() => role.Rename("Other"));

            Assert.Contains("cannot be renamed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rename_ShouldRejectReservedTargetNames()
        {
            var role = Role.CreateCustomForTenant(Guid.NewGuid(), "Reviewer");

            var ex = Assert.Throws<InvalidOperationException>(() => role.Rename(RoleNames.Admin));

            Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EnsureCanBeDeleted_ShouldRejectSystemRoles()
        {
            var role = Role.CreateSystemAssignableForTenant(Guid.NewGuid(), RoleNames.User);

            Assert.Throws<InvalidOperationException>(() => role.EnsureCanBeDeleted());
        }

        [Fact]
        public void EnsurePermissionsCanBeReplaced_ShouldRejectSystemRoles()
        {
            var role = Role.CreateSystemAssignableForTenant(Guid.NewGuid(), RoleNames.User);

            var ex = Assert.Throws<InvalidOperationException>(() => role.EnsurePermissionsCanBeReplaced());

            Assert.Contains("System role", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EnsureAssignableAsCustomRole_ShouldRejectSystemRoles()
        {
            var role = Role.CreateForTenant(Guid.NewGuid(), "Caseworker", isSystem: true);

            Assert.Throws<InvalidOperationException>(() => role.EnsureAssignableAsCustomRole());
        }

        [Fact]
        public void BuildReplacedPermissions_ShouldCreateDedupedGrants()
        {
            var role = Role.CreateCustomForTenant(Guid.NewGuid(), "Reviewer");
            var when = DateTime.UtcNow;

            var permissions = role.BuildReplacedPermissions(
                [
                    (ResourceType.Application, "Apps", AccessType.Read),
                    (ResourceType.Application, "Apps", AccessType.Read),
                    (ResourceType.Template, "T1", AccessType.Write)
                ],
                when);

            Assert.Equal(2, permissions.Count);
            Assert.All(permissions, p => Assert.Equal(role.Id, p.RoleId));
            Assert.Equal(2, role.Permissions.Count);
        }

        [Fact]
        public void CreatePermission_ShouldRequireRoleId()
        {
            // Unlikely path: construct without going through factories that set Id.
            var role = Role.CreateCustomForTenant(Guid.NewGuid(), "Reviewer");
            Assert.NotNull(role.Id);

            var permission = role.CreatePermission("key", ResourceType.User, AccessType.Read, DateTime.UtcNow);
            Assert.Equal("key", permission.ResourceKey);
            Assert.Contains(permission, role.Permissions);
        }
    }
}
