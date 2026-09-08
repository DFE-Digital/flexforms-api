using AutoFixture;
using AutoFixture.Kernel;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities
{
    /// <summary>
    /// AutoFixture customization for User. 
    /// Pass in any of these constructor‐parameters if you want to override defaults in a test.
    /// </summary>
    public class UserCustomization : ICustomization
    {
        public UserId? OverrideId { get; set; }
        public RoleId? OverrideRoleId { get; set; }
        public string? OverrideName { get; set; }
        public string? OverrideEmail { get; set; }
        public DateTime? OverrideCreatedOn { get; set; }
        public UserId? OverrideCreatedBy { get; set; }
        public DateTime? OverrideLastModifiedOn { get; set; }
        public UserId? OverrideLastModifiedBy { get; set; }
        public IEnumerable<Permission>? OverridePermissions { get; set; }
        public string? OverrideExternalProviderId { get; set; }

        public void Customize(IFixture fixture)
        {
            fixture.Customize<User>(composer => composer
                .FromFactory(new MethodInvoker(new GreedyConstructorQuery())));

            fixture.Customize<User>(composer => composer
                .FromFactory(() =>
                {
                    var id = OverrideId ?? new UserId(fixture.Create<Guid>());
                    var roleId = OverrideRoleId ?? new RoleId(fixture.Create<Guid>());
                    var name = OverrideName ?? fixture.Create<string>();
                    var email = (OverrideEmail ?? fixture.Create<string>()).Trim();
                    var createdOn = OverrideCreatedOn ?? fixture.Create<DateTime>();
                    var createdBy = OverrideCreatedBy ?? null;
                    var lastModifiedOn = OverrideLastModifiedOn ?? null;
                    var lastModifiedBy = OverrideLastModifiedBy ?? null;
                    var externalProviderId = OverrideExternalProviderId ?? null;

                    var perms = OverridePermissions ?? new List<Permission>();

                    return new User(
                        id,
                        roleId,
                        name,
                        email,
                        createdOn,
                        createdBy,
                        lastModifiedOn,
                        lastModifiedBy,
                        externalProviderId,
                        perms);
                }));
        }
    }
}
