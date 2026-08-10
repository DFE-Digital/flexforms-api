using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Resolves tenants from the <c>TenantHostnames</c> table.
/// </summary>
public sealed class TenantHostnameResolver(TenantConfigDbContext dbContext) : ITenantHostnameResolver
{
    public async Task<TenantHostnameResolution?> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return null;
        }

        var normalized = hostname.Trim();

        var match = await dbContext.TenantHostnames
            .AsNoTracking()
            .Where(h => h.Hostname == normalized)
            .Select(h => new
            {
                h.Hostname,
                h.TenantId,
                TenantName = h.Tenant.Name,
                h.Tenant.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null || !match.IsActive)
        {
            return null;
        }

        return new TenantHostnameResolution(match.TenantId, match.TenantName, match.Hostname);
    }

    public async Task<IReadOnlyList<string>> ListHostnamesForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.TenantHostnames
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId)
            .OrderBy(h => h.Hostname)
            .Select(h => h.Hostname)
            .ToListAsync(cancellationToken);
    }
}
