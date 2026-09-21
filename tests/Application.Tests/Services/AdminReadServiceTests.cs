using System.Text.Json;
using Application.DTOs.DadosPix;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Moq;
using Xunit;

namespace Application.Tests.Services;

public sealed class AdminReadServiceTests
{
    [Fact]
    public async Task ListagemPixPropagaTokenEMapeiaSemChave()
    {
        var token = new CancellationTokenSource().Token;
        var repo = new Mock<IPagamentoPixRepository>(MockBehavior.Strict);
        var pix = PagamentoPix.Criar(Guid.NewGuid(), Guid.NewGuid(), 12.34m, TipoChavePix.Email, "ficticio@example.invalid");
        repo.Setup(x => x.ObterTodosAsync(token)).ReturnsAsync([pix]);
        var service = new PagamentoPixService(Mock.Of<ICashbackRepository>(MockBehavior.Strict), Mock.Of<IDadosPixRepository>(MockBehavior.Strict), repo.Object);
        var result = await service.ObterTodosAsync(token);
        Assert.Equal(pix.Id, Assert.Single(result).Id);
        Assert.DoesNotContain(pix.ChavePix, JsonSerializer.Serialize(result));
        repo.VerifyAll();
    }

    [Theory]
    [InlineData("consultar")]
    [InlineData("salvar")]
    [InlineData("remover")]
    public async Task DadosPixPropagaCancelamentoNaValidacaoDoUsuario(string acao)
    {
        var id = Guid.NewGuid();
        var token = new CancellationTokenSource().Token;
        var users = new Mock<IUsuarioRepository>(MockBehavior.Strict);
        users.Setup(x => x.ObterPorIdAsync(id, token)).ThrowsAsync(new OperationCanceledException(token));
        var dados = new Mock<IDadosPixRepository>(MockBehavior.Strict);
        var service = new DadosPixService(dados.Object, users.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acao switch {
            "consultar" => service.ObterPorUsuarioIdAsync(id, token),
            "salvar" => service.CadastrarOuAtualizarAsync(id, new DadosPixDto(), token),
            _ => service.RemoverAsync(id, token)
        });
        users.VerifyAll();
        dados.VerifyNoOtherCalls();
    }
}
