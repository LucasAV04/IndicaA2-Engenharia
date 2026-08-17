using Application.DTOs.Indicacao;
using Domain.Enums;

namespace Application.Interfaces.Services
{
    public interface IIndicacaoService
    {
        Task<IndicacaoResponseDto> CriarAsync(
            CreateIndicacaoDto dto,
            CancellationToken cancellationToken = default);

        Task<IndicacaoResponseDto> CriarPorCodigoAsync(
            CreateIndicacaoPorCodigoDto dto,
            CancellationToken cancellationToken = default);

        Task<IndicacaoResponseDto> ObterPorIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<IndicacaoResponseDto>> ObterTodasAsync(
            CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<IndicacaoResponseDto>> ObterPorUsuarioIndicadorIdAsync(
            Guid usuarioIndicadorId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<IndicacaoResponseDto>> ObterPorStatusAsync(
            StatusIndicacao status,
            CancellationToken cancellationToken = default);

        Task VincularUsuarioIndicadoAsync(
            VincularUsuarioIndicadoDto dto,
            CancellationToken cancellationToken = default);

        Task VincularVistoriaAsync(
            VincularVistoriaDto dto,
            CancellationToken cancellationToken = default);

        Task MarcarVistoriaConcluidaAsync(
            Guid indicacaoId,
            CancellationToken cancellationToken = default);

        Task CancelarAsync(
            Guid indicacaoId,
            CancellationToken cancellationToken = default);
    }
}
