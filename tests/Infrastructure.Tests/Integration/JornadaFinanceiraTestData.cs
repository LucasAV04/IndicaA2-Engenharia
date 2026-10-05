using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;

namespace Infrastructure.Tests.Integration;

internal static class JornadaFinanceiraTestData
{
    // Os testes de liquidação precisam partir de uma vistoria efetivamente concluída.
    // A leitura de vistorias legadas continua coberta nos testes do repository.
    internal static async Task<Vistoria> CriarConcluidaAsync(MySqlIntegrationFixture fixture,Guid usuarioId)
    {
        var agora=DateTime.UtcNow;var store=new PrecificacaoMySqlStore(fixture.ConnectionFactory);
        var tipo=new TipoPlanta("Tipo fictício "+Guid.NewGuid().ToString("N"),agora);
        await store.CriarTipoAsync(tipo,default);
        await store.PublicarAsync(tipo.Id,new(4.999m,ModalidadeAcrescimo.Fixo,0,0),agora,default);
        var vistoria=await store.CriarVistoriaAsync(usuarioId,tipo.Id,100,PacoteVistoria.Simples,agora,agora,default);
        var repository=new VistoriaMySqlRepository(fixture.ConnectionFactory);
        vistoria.MarcarRealizada();
        await repository.AtualizarAsync(vistoria);
        vistoria.Concluir();
        await repository.AtualizarAsync(vistoria);
        return vistoria;
    }
}
