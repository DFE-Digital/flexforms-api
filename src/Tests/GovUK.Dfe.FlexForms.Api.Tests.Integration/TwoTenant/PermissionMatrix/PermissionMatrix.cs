using System.Net.Http.Json;
using GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant.PermissionMatrix;

/// <summary>One endpoint in the matrix: its key (controller type and method, as in <c>ExpectedSecurity.json</c>),
/// a request built from tenant A's seeded data, and the principals its policy lets through.</summary>
public sealed record MatrixEndpoint(string Key, string Method, string Path, Func<HttpContent>? Body = null)
{
    /// <summary>Principals the endpoint's authorization policy allows. Ignored when <see cref="Open"/> is set.</summary>
    public Principal Allowed { get; init; }

    /// <summary>The endpoint allows anonymous callers, so no policy runs.</summary>
    public bool Open { get; init; }

    public HttpRequestMessage ToRequest() =>
        new(new HttpMethod(Method), Path)
        {
            Content = Body?.Invoke() ?? (Method is "POST" or "PUT" ? JsonContent.Create(new { }) : null),
        };

    public override string ToString() => Key;
}

/// <summary>
/// Every controller action and who its authorization policy lets through (Layer A). Adding an action without a
/// row here fails <see cref="PermissionMatrixTests"/>.
/// </summary>
public static class PermissionMatrix
{
    public static readonly Guid NotificationId = Guid.Parse("0000000a-0000-4000-8000-00000000000e");
    public static readonly Guid RefreshId = Guid.Parse("0000000a-0000-4000-8000-00000000000f");

    private static TestTenant A => TestTenant.A;

    private static HttpContent Upload() =>
        new MultipartFormDataContent { { new StringContent("x"), "file", "x.pdf" }, { new StringContent("x"), "name" } };

    public static IReadOnlyList<MatrixEndpoint> Endpoints { get; } =
    [
        new("ApplicationsController.AddApplicationResponseAsync", "POST", $"v1/Applications/{A.ApplicationId}/responses") { Allowed = Principal.Contributor | Principal.Owner | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.AddContributorAsync", "POST", $"v1/Applications/{A.ApplicationId}/contributors") { Allowed = Principal.Contributor | Principal.Owner | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.ChangeApplicationOwnerAsync", "PUT", $"v1/Applications/{A.ApplicationId}/owner") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ApplicationsController.CreateApplicationAsync", "POST", $"v1/Applications") { Allowed = Principal.User | Principal.Owner | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.DeleteApplicationAsync", "DELETE", $"v1/Applications/{A.ApplicationId}") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ApplicationsController.DeleteFileAsync", "DELETE", $"v1/Applications/{A.ApplicationId}/files/{A.FileId}") { Allowed = Principal.Contributor | Principal.Owner | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.DownloadApplicationPreviewHtmlAsync", "GET", $"v1/Applications/reference/{A.ApplicationReference}/preview/download") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.DownloadFileAsync", "GET", $"v1/Applications/{A.ApplicationId}/files/{A.FileId}/download") { Allowed = Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.GetApplicationByReferenceAsync", "GET", $"v1/Applications/reference/{A.ApplicationReference}") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.GetApplicationsByTemplateAsync", "GET", $"v1/Applications/templates/{A.TemplateId}") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.GetApplicationsForUserAsync", "GET", $"v1/Users/{A.OwnerEmail}/applications") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.GetContributorsAsync", "GET", $"v1/Applications/{A.ApplicationId}/contributors") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.GetFileValidationGateAsync", "GET", $"v1/Applications/{A.ApplicationId}/file-validation-gate") { Allowed = Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.GetFilesForApplicationAsync", "GET", $"v1/Applications/{A.ApplicationId}/files") { Allowed = Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.GetMyApplicationsAsync", "GET", $"v1/me/applications") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.RemoveContributorAsync", "DELETE", $"v1/Applications/{A.ApplicationId}/contributors/{A.CaseworkerId}") { Allowed = Principal.Contributor | Principal.Owner | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.SubmitApplicationAsync", "POST", $"v1/Applications/{A.ApplicationId}/submit") { Allowed = Principal.Contributor | Principal.Owner | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ApplicationsController.UploadFileAsync", "POST", $"v1/Applications/{A.ApplicationId}/files", Upload) { Allowed = Principal.Contributor | Principal.Owner | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("DiagnosticsController.Get", "GET", $"v1/diagnostics") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("HostConfigController.GetHostConfiguration", "GET", $"v1/host-config") { Allowed = Principal.None },
        new("HubAuthController.CreateHubTicket", "POST", $"auth/hub-ticket") { Allowed = Principal.Users },
        new("HubAuthController.RedeemHubCookie", "GET", $"auth/hub-cookie") { Open = true },
        new("HubAuthController.SignOutHubCookie", "POST", $"auth/hub-signout") { Open = true },
        new("IntegrationsController.RecordFileValidationResultAsync", "POST", $"v1/integrations/files/{A.FileId}/validation-result") { Allowed = Principal.FileValidation },
        new("InternalPrismController.GetPrismApplicationStateAsync", "GET", $"v1/internal/prism/applications/{A.ApplicationId}/current") { Allowed = Principal.None },
        new("InternalPrismController.GetPrismResponseAsync", "GET", $"v1/internal/prism/responses/{A.ResponseId}") { Allowed = Principal.None },
        new("InternalPrismController.GetPrismTemplateVersionAsync", "GET", $"v1/internal/prism/template-versions/{A.TemplateVersionId}") { Allowed = Principal.None },
        new("InternalPrismController.GetPrismTenantsAsync", "GET", $"v1/internal/prism/tenants") { Allowed = Principal.None },
        new("InternalPrismController.ListPrismApplicationsAsync", "GET", $"v1/internal/prism/applications") { Allowed = Principal.None },
        new("NotificationsController.ClearAllNotificationsAsync", "DELETE", $"v1/Notifications/clear-all") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.ClearNotificationsByCategoryAsync", "DELETE", $"v1/Notifications/category/general") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.ClearNotificationsByContextAsync", "DELETE", $"v1/Notifications/context/matrix") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.CreateNotificationAsync", "POST", $"v1/Notifications") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.GetAllNotificationsAsync", "GET", $"v1/Notifications") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.GetNotificationsByCategoryAsync", "GET", $"v1/Notifications/category/general") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.GetUnreadNotificationCountAsync", "GET", $"v1/Notifications/unread/count") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.GetUnreadNotificationsAsync", "GET", $"v1/Notifications/unread") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.MarkAllNotificationsAsReadAsync", "PUT", $"v1/Notifications/read-all") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.MarkNotificationAsReadAsync", "PUT", $"v1/Notifications/{NotificationId}/read") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("NotificationsController.RemoveNotificationAsync", "DELETE", $"v1/Notifications/{NotificationId}") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("ReportingExportController.GetReportingExportRefreshAsync", "GET", $"v1/admin/tenants/{A.Id}/reporting-export/refreshes/{RefreshId}") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ReportingExportController.GetTemplateReportingExportDefaultAsync", "GET", $"v1/admin/tenants/{A.Id}/reporting-export/templates/{A.TemplateId}/default") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ReportingExportController.GetTemplateReportingExportPolicyAsync", "GET", $"v1/admin/tenants/{A.Id}/reporting-export/templates/{A.TemplateId}") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ReportingExportController.GetTenantReportingExportDefaultAsync", "GET", $"v1/admin/tenants/{A.Id}/reporting-export/default") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ReportingExportController.UpdateTemplateReportingExportDecisionsAsync", "PUT", $"v1/admin/tenants/{A.Id}/reporting-export/templates/{A.TemplateId}/decisions") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ReportingExportController.UpdateTemplateReportingExportDefaultAsync", "PUT", $"v1/admin/tenants/{A.Id}/reporting-export/templates/{A.TemplateId}/default") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("ReportingExportController.UpdateTenantReportingExportDefaultAsync", "PUT", $"v1/admin/tenants/{A.Id}/reporting-export/default") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("RolesController.CreateAsync", "POST", $"v1/Roles") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("RolesController.CreateFromTemplateAsync", "POST", $"v1/Roles/from-template") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("RolesController.DeleteAsync", "DELETE", $"v1/Roles/{A.CaseworkerRoleId}") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("RolesController.GetPermissionsAsync", "GET", $"v1/Roles/{A.CaseworkerRoleId}/permissions") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("RolesController.ListAsync", "GET", $"v1/Roles") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("RolesController.RenameAsync", "PUT", $"v1/Roles/{A.CaseworkerRoleId}") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("RolesController.SetPermissionsAsync", "PUT", $"v1/Roles/{A.CaseworkerRoleId}/permissions") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.CreateCustomApplicationStatusAsync", "POST", $"v1/Templates/{A.TemplateId}/custom-statuses") { Allowed = Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.CreateTemplateAsync", "POST", $"v1/Templates") { Allowed = Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.CreateTemplateVersionAsync", "POST", $"v1/Templates/{A.TemplateId}/versions") { Allowed = Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.GetAccessibleTemplatesAsync", "GET", $"v1/Templates") { Allowed = Principal.Authenticated },
        new("TemplatesController.GetCustomApplicationStatusesAsync", "GET", $"v1/Templates/{A.TemplateId}/custom-statuses") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.GetLatestTemplateSchemaAsync", "GET", $"v1/Templates/{A.TemplateId}/schema") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.GetTemplateSchemaByVersionAsync", "GET", $"v1/Templates/{A.TemplateId}/versions/1.0.0") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.GetTemplateVersionsAsync", "GET", $"v1/Templates/{A.TemplateId}/versions") { Allowed = Principal.User | Principal.Contributor | Principal.Owner | Principal.Caseworker | Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.GrantTemplateAccessToAllUsersAsync", "POST", $"v1/Templates/{A.TemplateId}/grant-all-users") { Allowed = Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TemplatesController.SetTemplateLiveAsync", "PUT", $"v1/Templates/{A.TemplateId}/live") { Allowed = Principal.FormManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("TenantAdminController.CloneTenant", "POST", $"v1/admin/tenants/{A.Id}/clone") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.DeleteTenantSetting", "DELETE", $"v1/admin/tenants/{A.Id}/settings") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.DuplicateTenant", "POST", $"v1/admin/tenants/{A.Id}/duplicate") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.ExportConfiguration", "GET", $"v1/admin/tenants/{A.Id}/export") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetCategoryCookbook", "GET", $"v1/admin/tenants/settings/cookbook") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetEffectiveConfiguration", "GET", $"v1/admin/tenants/{A.Id}/effective-config") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetEventCatalogue", "GET", $"v1/admin/tenants/event-catalogue") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetPlatformTenants", "GET", $"v1/admin/tenants/platform") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetSafeTenantSettings", "GET", $"v1/admin/tenants/{A.Id}/safe-settings") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetSettingAuditLog", "GET", $"v1/admin/tenants/{A.Id}/settings/audit") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetTenantHealth", "GET", $"v1/admin/tenants/{A.Id}/health") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetTenantSettings", "GET", $"v1/admin/tenants/{A.Id}/settings") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.GetTenants", "GET", $"v1/admin/tenants") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.ImportConfiguration", "POST", $"v1/admin/tenants/{A.Id}/import") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.RefreshTenantConfiguration", "POST", $"v1/admin/tenants/refresh") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.RevealTenantSettingSecret", "POST", $"v1/admin/tenants/{A.Id}/settings/reveal") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.SeedFromAppSettings", "POST", $"v1/admin/tenants/seed") { Allowed = Principal.None },
        new("TenantAdminController.UpsertSafeTenantSetting", "PUT", $"v1/admin/tenants/{A.Id}/safe-settings") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.UpsertTenantSetting", "POST", $"v1/admin/tenants/{A.Id}/settings") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantAdminController.ValidateTenantSetting", "POST", $"v1/admin/tenants/{A.Id}/settings/validate") { Allowed = Principal.Admin | Principal.SuperAdmin },
        new("TenantConfigController.GetTenantConfiguration", "GET", $"v1/tenant-config") { Allowed = Principal.Authenticated },
        new("TenantConfigController.GetTenantConfigurationByTenantId", "GET", $"v1/tenant-config/tenants/{A.Id}") { Allowed = Principal.None },
        new("TenantConfigController.ResolveTenantByHostname", "GET", $"v1/tenant-config/resolve") { Allowed = Principal.None },
        new("TokensController.ConfirmTestAuthAuthenticatorInviteSetup", "POST", $"v1/Tokens/test-auth-authenticator/invite/confirm") { Allowed = Principal.Services },
        new("TokensController.Exchange", "POST", $"v1/Tokens/exchange") { Allowed = Principal.Services },
        new("TokensController.RedeemTestAuthAuthenticatorInvite", "POST", $"v1/Tokens/test-auth-authenticator/invite/redeem") { Allowed = Principal.Services },
        new("TokensController.SendTestAuthPassword", "POST", $"v1/Tokens/test-auth-password") { Allowed = Principal.Services },
        new("TokensController.VerifyTestAuthAuthenticatorCode", "POST", $"v1/Tokens/test-auth-authenticator/verify") { Allowed = Principal.Services },
        new("TokensController.VerifyTestAuthPassword", "POST", $"v1/Tokens/test-auth-password/verify") { Allowed = Principal.Services },
        new("UserFeedbackController.PostAsync", "POST", $"v1/UserFeedback") { Open = true },
        new("UsersController.AssignUserRoleAsync", "POST", $"v1/Users/roles") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.BeginMyTestAuthAuthenticatorSetupAsync", "POST", $"v1/me/test-auth-authenticator/setup") { Allowed = Principal.Authenticated },
        new("UsersController.ClearTestAuthTemporaryLockoutAsync", "DELETE", $"v1/Users/{A.CaseworkerId}/test-auth-sign-in-lock/temporary") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.ConfirmMyTestAuthAuthenticatorSetupAsync", "POST", $"v1/me/test-auth-authenticator/setup/confirm") { Allowed = Principal.Authenticated },
        new("UsersController.CreateTestAuthAuthenticatorInviteAsync", "POST", $"v1/Users/test-auth-authenticator-invites") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.GetAccessAuditLogAsync", "GET", $"v1/Users/access-audit") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.GetCreatedApplicationsByEmailAsync", "GET", $"v1/Users/created-applications") { Allowed = Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.GetMyPermissionsAsync", "GET", $"v1/me/permissions") { Allowed = Principal.Authenticated },
        new("UsersController.GetMyTestAuthAuthenticatorStatusAsync", "GET", $"v1/me/test-auth-authenticator") { Allowed = Principal.Authenticated },
        new("UsersController.GetTenantUsersAsync", "GET", $"v1/Users/tenant") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.GetTestAuthSignInLockAsync", "GET", $"v1/Users/{A.CaseworkerId}/test-auth-sign-in-lock") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.GetUserPermissionsAsync", "GET", $"v1/Users/{A.CaseworkerId}/permissions") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.RegisterUserAsync", "POST", $"v1/Users/register") { Allowed = Principal.Services },
        new("UsersController.RemoveMyTestAuthAuthenticatorAsync", "DELETE", $"v1/me/test-auth-authenticator") { Allowed = Principal.Authenticated },
        new("UsersController.RemoveUserFromTenantAsync", "DELETE", $"v1/Users/{A.CaseworkerId}/tenant") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.SetTestAuthSignInLockAsync", "PUT", $"v1/Users/{A.CaseworkerId}/test-auth-sign-in-lock") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.SetUserPermissionsAsync", "PUT", $"v1/Users/{A.CaseworkerId}/permissions") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
        new("UsersController.UpdateUserTemplateAccessAsync", "PUT", $"v1/Users/{A.CaseworkerId}/templates") { Allowed = Principal.UserManager | Principal.Admin | Principal.SuperAdmin | Principal.ServiceAdmin },
    ];
}
