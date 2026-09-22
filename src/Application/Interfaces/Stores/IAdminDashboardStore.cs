using Application.DTOs.Admin;

namespace Application.Interfaces.Stores;

public interface IAdminDashboardStore
{
    Task<DashboardResponseDto> ObterAsync(CancellationToken cancellationToken = default);
}
