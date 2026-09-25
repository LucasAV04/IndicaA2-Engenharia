using Application.DTOs.Precificacao;
using Application.DTOs.Vistoria;
using Application.Interfaces.Stores;
using Application.Mapping;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.Services;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class PrecificacaoServiceTests
{
    private static readonly DateTime Agora = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private sealed class Relogio : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Agora); }
    [Fact] public async Task CriacaoNormalizaEPropagaToken()
    {
        var store = new Mock<IPrecificacaoStore>(); var token = new CancellationTokenSource().Token;
        await new PrecificacaoService(store.Object, new Relogio()).CriarTipoAsync(new("  Teste  "), token);
        store.Verify(s => s.CriarTipoAsync(It.Is<TipoPlanta>(t => t.Nome == "Teste" && t.CreatedAt == Agora), token), Times.Once);
    }
    [Fact] public async Task PublicacaoDelegaVersaoEsperadaSemMutarHistorico()
    {
        var store = new Mock<IPrecificacaoStore>(); var tipo = Guid.NewGuid(); var dto = new PublicarPrecoDto(1, ModalidadeAcrescimo.Fixo, 0, 4);
        var preco = new PrecoVistoria(tipo, "Teste", 1, ModalidadeAcrescimo.Fixo, 0, 5, Agora);
        store.Setup(s => s.PublicarAsync(tipo, dto, Agora, It.IsAny<CancellationToken>())).ReturnsAsync(preco);
        var r = await new PrecificacaoService(store.Object, new Relogio()).PublicarAsync(tipo, dto);
        Assert.Equal(5, r.Versao); Assert.Equal(preco.Id, r.Id); store.VerifyNoOtherCallsExceptPublication(tipo, dto);
    }
    [Fact] public async Task PrecoAusentePropagaSemCriarVistoria()
    {
        var store = new Mock<IPrecificacaoStore>(); var dto = new SimularVistoriaDto(Guid.NewGuid(), 10, PacoteVistoria.Simples);
        store.Setup(s => s.SimularAsync(dto, Agora, It.IsAny<CancellationToken>())).ThrowsAsync(new PrecificacaoException("preco_ausente"));
        var ex = await Assert.ThrowsAsync<PrecificacaoException>(() => new PrecificacaoService(store.Object, new Relogio()).SimularAsync(dto));
        Assert.Equal("preco_ausente", ex.Codigo); Assert.Single(store.Invocations);
    }
    [Fact] public async Task SimulacaoNaoEscreveEUsaRelogio()
    {
        var store = new Mock<IPrecificacaoStore>(); var dto = new SimularVistoriaDto(Guid.NewGuid(), 10, PacoteVistoria.Simples);
        var c = MotorPrecificacaoVistoria.Calcular(new(dto.TipoPlantaId, "Teste", 1, ModalidadeAcrescimo.Fixo, 0, 1, Agora), "Teste", 10, PacoteVistoria.Simples, Agora);
        store.Setup(s => s.SimularAsync(dto, Agora, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        var r = await new PrecificacaoService(store.Object, new Relogio()).SimularAsync(dto);
        Assert.True(r.Simulacao); Assert.Equal(10, r.ValorFinal); Assert.Single(store.Invocations);
    }
    [Fact] public async Task CancelamentoNaoAcessaStore()
    {
        var store = new Mock<IPrecificacaoStore>(); var s = new PrecificacaoService(store.Object, new Relogio());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.CriarTipoAsync(new("Teste"), new(true)));
        Assert.Empty(store.Invocations);
    }
    [Fact] public async Task RenomeacaoDesativacaoHistoricoPropagamToken()
    {
        var store = new Mock<IPrecificacaoStore>(); var id = Guid.NewGuid(); var token = new CancellationTokenSource().Token;
        store.Setup(s => s.HistoricoAsync(id, token)).ReturnsAsync(Array.Empty<PrecoVistoria>());
        var s = new PrecificacaoService(store.Object, new Relogio());
        await s.RenomearTipoAsync(id, new("  Novo  "), token); await s.DesativarTipoAsync(id, token); await s.HistoricoAsync(id, token);
        store.Verify(x => x.RenomearTipoAsync(id, "Novo", Agora, token)); store.Verify(x => x.DesativarTipoAsync(id, Agora, token)); store.Verify(x => x.HistoricoAsync(id, token));
    }
    [Fact] public void DtoLegadoESnapshotSaoExplicitos()
    {
        var antigo = new Vistoria(Guid.NewGuid(), "Texto original", 10, PacoteVistoria.Simples, Agora).ToResponseDto();
        Assert.True(antigo.Legado); Assert.Null(antigo.Precificacao); Assert.Null(antigo.TipoPlantaId);
        var p = new PrecoVistoria(Guid.NewGuid(), "Teste", 1, ModalidadeAcrescimo.Fixo, 0, 1, Agora);
        var atual = Vistoria.CriarCalculada(Guid.NewGuid(), Agora, MotorPrecificacaoVistoria.Calcular(p, "Teste", 10, PacoteVistoria.Simples, Agora)).ToResponseDto();
        Assert.False(atual.Legado); Assert.False(atual.Precificacao!.Simulacao); Assert.Equal(p.TipoPlantaId, atual.TipoPlantaId);
    }
    [Fact] public async Task NovaVistoriaSemCatalogoNaoAcessaDependencias()
    {
        var store = new Mock<IPrecificacaoStore>(); var users = new Mock<IUsuarioRepository>();
        var s = new VistoriaService(Mock.Of<IVistoriaRepository>(), users.Object, store.Object, new Relogio());
        await Assert.ThrowsAsync<DomainException>(() => s.CriarAsync(new CreateVistoriaDto()));
        Assert.Empty(store.Invocations); Assert.Empty(users.Invocations);
    }
}

internal static class PrecificacaoMockAssertions
{
    internal static void VerifyNoOtherCallsExceptPublication(this Mock<IPrecificacaoStore> store, Guid tipo, PublicarPrecoDto dto)
    { store.Verify(s => s.PublicarAsync(tipo, dto, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once); store.VerifyNoOtherCalls(); }
}
