using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Tenants;

namespace ParkingSaaS.Application.Tenants;

/// <summary>Platform-administrator operations over tenants. Not tenant-scoped.</summary>
public interface ITenantProvisioningService
{
    Task<TenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken ct);
    Task<TenantResponse> ChangeStatusAsync(Guid id, UpdateTenantStatusRequest request, CancellationToken ct);
    Task<TenantResponse> ChangePlanAsync(Guid id, UpdateTenantPlanRequest request, CancellationToken ct);
    Task<TenantResponse> UpdateCapacityAddonAsync(Guid id, UpdateTenantCapacityAddonRequest request, CancellationToken ct);
    Task<TenantResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<TenantResponse>> ListAsync(PageQuery query, CancellationToken ct);
    Task<IReadOnlyList<TenantAuditLogResponse>> GetAuditHistoryAsync(Guid id, CancellationToken ct);
}
