using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Tenants;

namespace ParkingSaaS.Application.Tenants;

/// <summary>Platform-administrator operations over tenants. Not tenant-scoped.</summary>
public interface ITenantProvisioningService
{
    Task<TenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken ct);
    Task<TenantResponse> CreateAddOnLocationAsync(Guid tenantId, CreateTenantAddOnLocationRequest request, CancellationToken ct);
    Task<TenantResponse> ChangeStatusAsync(Guid id, UpdateTenantStatusRequest request, CancellationToken ct);
    Task<TenantResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<TenantResponse>> ListAsync(PageQuery query, CancellationToken ct);
}
