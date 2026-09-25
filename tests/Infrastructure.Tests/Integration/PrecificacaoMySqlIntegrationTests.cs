using Application.DTOs.Precificacao;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Repositories;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class PrecificacaoMySqlIntegrationTests(MySqlIntegrationFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private PrecificacaoMySqlStore Store => new(fixture.ConnectionFactory);
    private static PublicarPrecoDto Entrada(int versao = 0, ModalidadeAcrescimo modalidade = ModalidadeAcrescimo.Fixo) => new(2.1234m, modalidade, 5m, versao);
    private async Task<TipoPlanta> Tipo()
    { var t = new TipoPlanta("Planta fictícia " + Guid.NewGuid().ToString("N"), Agora); await Store.CriarTipoAsync(t, default); return t; }
    private async Task<Guid> Usuario()
    { var u = IntegrationTestData.CriarUsuario(); await new UsuarioMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(u, default); return u.Id; }
    private async Task<Vistoria> Criar(Guid tipo, PacoteVistoria pacote = PacoteVistoria.Simples) =>
        await Store.CriarVistoriaAsync(await Usuario(), tipo, 10, pacote, Agora, Agora, default);
    private async Task<string> Snapshot(string tabela)
    {
        Assert.Contains(tabela, new[] { "vistorias", "tipos_planta", "precos_vistoria", "pagamentos_vistoria", "cashbacks", "pagamentos_pix", "operacoes_pagamento_pix" });
        using var c = new ProcessamentoPixCenario(fixture);
        return await c.SnapshotAsync($"SELECT * FROM {tabela} ORDER BY id");
    }
    private async Task<long> Escalar(string sql)
    { await using var c = fixture.ConnectionFactory.Create(); await c.OpenAsync(); await using var cmd = new MySqlCommand(sql, c); return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }

    [MySqlIntegrationFact]
    public async Task CatalogoVazioNaoGeraSeedsOuPrecos()
    { await fixture.LimparDadosAsync(); Assert.Empty(await Store.ListarTiposAsync(default)); Assert.Empty(await Store.ListarAtivosAsync(default)); }

    [MySqlIntegrationFact]
    public async Task CriarTipoNormalizaNomeERejeitaDuplicidadeDeCaixa()
    {
        await fixture.LimparDadosAsync(); var t = new TipoPlanta("  Exemplo fictício  ", Agora); await Store.CriarTipoAsync(t, default);
        var e = await Assert.ThrowsAsync<PrecificacaoException>(() => Store.CriarTipoAsync(new("EXEMPLO FICTÍCIO", Agora), default));
        Assert.Equal("nome_duplicado", e.Codigo); Assert.Equal("Exemplo fictício", (await Store.ListarTiposAsync(default)).Single().Nome);
    }

    [MySqlIntegrationFact]
    public async Task ConcorrenciaNomesEquivalentesTemUmaCriacao()
    {
        await fixture.LimparDadosAsync(); var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Executar(string nome) { await inicio.Task.WaitAsync(TimeSpan.FromSeconds(10)); try { await Store.CriarTipoAsync(new(nome, Agora), default); return true; } catch (PrecificacaoException e) when(e.Codigo == "nome_duplicado") { return false; } }
        var a = Executar(" Concorrente fictício "); var b = Executar("CONCORRENTE FICTÍCIO"); inicio.SetResult();
        var resultados = await Task.WhenAll(a,b).WaitAsync(TimeSpan.FromSeconds(10)); Assert.Single(resultados, x => x); Assert.Single(await Store.ListarTiposAsync(default));
    }

    [MySqlIntegrationFact]
    public async Task PrimeiraVersaoAtivaEPersistenciaDecimal()
    { await fixture.LimparDadosAsync(); var t = await Tipo(); var p = await Store.PublicarAsync(t.Id, Entrada(), Agora, default); var l = (await Store.ListarAtivosAsync(default)).Single(); Assert.Equal(p.Id,l.Id); Assert.Equal(1,l.Versao); Assert.Equal(2.1234m,l.PrecoM2); Assert.True(l.Ativo); Assert.Null(l.DesativadoEm); }

    [MySqlIntegrationFact]
    public async Task PublicacaoPreservaHistoricoInativaAnteriorEOrdena()
    {
        await fixture.LimparDadosAsync(); var t = await Tipo(); var p = await Store.PublicarAsync(t.Id, Entrada(), Agora, default);
        await Store.PublicarAsync(t.Id,new(3,ModalidadeAcrescimo.Percentual,7,1),Agora.AddMinutes(1),default);
        var h = (await Store.HistoricoAsync(t.Id,default)).ToArray(); Assert.Equal(new[]{2,1},h.Select(x=>x.Versao)); Assert.True(h[0].Ativo); Assert.False(h[1].Ativo);
        Assert.Equal(p.PrecoM2,h[1].PrecoM2); Assert.Equal(p.Acrescimo,h[1].Acrescimo); Assert.Equal(p.CreatedAt,h[1].CreatedAt); Assert.Equal(Agora.AddMinutes(1),h[1].DesativadoEm); Assert.Single(await Store.ListarAtivosAsync(default));
    }

    [MySqlIntegrationFact]
    public async Task PublicacoesConcorrentesConflitamSemDuasAtivas()
    {
        await fixture.LimparDadosAsync(); var t = await Tipo(); var bloqueado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controlado = new PrecificacaoMySqlStore(fixture.ConnectionFactory, async (p,_,_,ct) => { if(p == PontoTransacionalPrecificacao.TipoBloqueado) { bloqueado.TrySetResult(); await liberar.Task.WaitAsync(TimeSpan.FromSeconds(10),ct); } });
        var a = controlado.PublicarAsync(t.Id,Entrada(),Agora,default); Task<PrecoVistoria>? b = null;
        try { await Task.WhenAny(bloqueado.Task,a).WaitAsync(TimeSpan.FromSeconds(10)); if(a.IsCompleted) await a; b = Store.PublicarAsync(t.Id,Entrada(),Agora,default); }
        finally
        {
            liberar.TrySetResult();
            async Task ObservarConcorrente()
            {
                if (b is null) return;
                var ex = await Assert.ThrowsAsync<PrecificacaoException>(() => b);
                Assert.Equal("versao_conflitante", ex.Codigo);
            }
            await Task.WhenAll(a, ObservarConcorrente()).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Single(await Store.ListarAtivosAsync(default)); Assert.Single(await Store.HistoricoAsync(t.Id,default));
    }

    [MySqlIntegrationFact]
    public async Task DesativacaoNaoReutilizaNumeroHistorico()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); var p=await Store.PublicarAsync(t.Id,Entrada(),Agora,default); await Store.DesativarPrecoAsync(t.Id,p.Id,Agora,default); Assert.Empty(await Store.ListarAtivosAsync(default)); var novo=await Store.PublicarAsync(t.Id,Entrada(1),Agora,default); Assert.Equal(2,novo.Versao); Assert.NotEqual(p.Id,novo.Id); }

    [MySqlIntegrationFact]
    public async Task TipoComPrecoAtivoNaoPodeSerDesativado()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default); var antes=await Snapshot("tipos_planta"); var ex=await Assert.ThrowsAsync<PrecificacaoException>(()=>Store.DesativarTipoAsync(t.Id,Agora,default)); Assert.Equal("desative_preco_primeiro",ex.Codigo); Assert.Equal(antes,await Snapshot("tipos_planta")); }

    [MySqlIntegrationFact]
    public async Task TipoInativoNaoRecebePrecoNemVistoria()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.DesativarTipoAsync(t.Id,Agora,default); Assert.False((await Store.ListarTiposAsync(default)).Single().Ativo); await Assert.ThrowsAsync<PrecificacaoException>(()=>Store.PublicarAsync(t.Id,Entrada(),Agora,default)); await Assert.ThrowsAsync<PrecificacaoException>(()=>Criar(t.Id)); Assert.Equal("[]",await Snapshot("vistorias")); }

    [MySqlIntegrationFact]
    public async Task SemPrecoFalhaFechadoSemVistoria()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Assert.ThrowsAsync<PrecificacaoException>(()=>Criar(t.Id)); Assert.Equal("[]",await Snapshot("vistorias")); }

    [MySqlIntegrationFact]
    public async Task SimplesGravaSnapshotCompleto()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); var p=await Store.PublicarAsync(t.Id,Entrada(),Agora,default); var v=await Criar(t.Id); var l=await new VistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(v.Id); Assert.Equal(v.Precificacao,l!.Precificacao); Assert.Equal(p.Id,l.Precificacao!.PrecoId); Assert.Equal(t.Id,l.Precificacao.TipoPlantaId); Assert.Equal(21.234m,l.Precificacao.ValorBase); Assert.Equal(21.23m,l.Precificacao.ValorFinal); Assert.Equal(0,l.Precificacao.Acrescimo); }

    [MySqlIntegrationFact]
    public async Task TotalFixoGravaValorCalculado()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default); Assert.Equal(26.23m,(await Criar(t.Id,PacoteVistoria.Total)).Precificacao!.ValorFinal); }

    [MySqlIntegrationFact]
    public async Task TotalPercentualArredondaSomenteFinal()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(modalidade:ModalidadeAcrescimo.Percentual),Agora,default); var c=(await Criar(t.Id,PacoteVistoria.Total)).Precificacao!; Assert.Equal(22.30m,c.ValorFinal); Assert.Equal(21.234m,c.ValorBase); }

    [MySqlIntegrationFact]
    public async Task LegadoPermaneceSemCatalogoSemConversao()
    { await fixture.LimparDadosAsync(); var v=IntegrationTestData.CriarVistoria(await Usuario()); var repo=new VistoriaMySqlRepository(fixture.ConnectionFactory); await repo.AdicionarAsync(v); var antes=await Snapshot("vistorias"); await Store.CriarTipoAsync(new(v.TipoPlanta,Agora),default); var l=await repo.ObterPorIdAsync(v.Id); Assert.Null(l!.Precificacao); Assert.Equal(v.TipoPlanta,l.TipoPlanta); Assert.Equal(antes,await Snapshot("vistorias")); }

    [MySqlIntegrationFact]
    public async Task RenomeacaoENovoPrecoNaoAlteramSnapshot()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default); await Criar(t.Id); var antes=await Snapshot("vistorias"); await Store.RenomearTipoAsync(t.Id,"Novo nome fictício",Agora,default); await Store.PublicarAsync(t.Id,Entrada(1),Agora,default); var h=(await Store.HistoricoAsync(t.Id,default)).ToArray(); Assert.Equal("Novo nome fictício",h[0].NomeTipoPlanta); Assert.Equal(t.Nome,h[1].NomeTipoPlanta); Assert.Equal(antes,await Snapshot("vistorias")); }

    [MySqlIntegrationFact]
    public async Task AtualizacaoNaoFinanceiraPreservaSnapshot()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default); var v=await Criar(t.Id); var snap=v.Precificacao; v.MarcarRealizada(); var repo=new VistoriaMySqlRepository(fixture.ConnectionFactory); await repo.AtualizarAsync(v); Assert.Equal(snap,(await repo.ObterPorIdAsync(v.Id))!.Precificacao); }

    [MySqlIntegrationFact]
    public async Task FalhaAposVistoriaReverteTudo() => await RollbackCriacao(PontoTransacionalPrecificacao.VistoriaInserida);
    [MySqlIntegrationFact]
    public async Task FalhaAposSnapshotReverteTudo() => await RollbackCriacao(PontoTransacionalPrecificacao.SnapshotGravado);
    private async Task RollbackCriacao(PontoTransacionalPrecificacao ponto)
    {
        await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default); var usuario=await Usuario();
        var store=new PrecificacaoMySqlStore(fixture.ConnectionFactory,(p,_,_,_) => p==ponto ? Task.FromException(new InvalidOperationException("falha fictícia")) : Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.CriarVistoriaAsync(usuario,t.Id,10,PacoteVistoria.Total,Agora,Agora,default)); Assert.Equal("[]",await Snapshot("vistorias")); Assert.Single(await Store.ListarAtivosAsync(default));
    }

    [MySqlIntegrationFact]
    public async Task FalhaPublicacaoReverteDesativacaoAnterior()
    { await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default); var antes=await Snapshot("precos_vistoria"); var store=new PrecificacaoMySqlStore(fixture.ConnectionFactory,(p,_,_,_)=>p==PontoTransacionalPrecificacao.PrecoDesativado ? Task.FromException(new InvalidOperationException("falha fictícia")):Task.CompletedTask); await Assert.ThrowsAsync<InvalidOperationException>(()=>store.PublicarAsync(t.Id,Entrada(1),Agora,default)); Assert.Equal(antes,await Snapshot("precos_vistoria")); }

    [MySqlIntegrationFact]
    public async Task PublicacaoConcorrenteComVistoriaNaoMisturaVersoes()
    {
        await fixture.LimparDadosAsync(); var t=await Tipo(); var p=await Store.PublicarAsync(t.Id,Entrada(),Agora,default); var usuario=await Usuario();
        var bloqueado=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var liberar=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store=new PrecificacaoMySqlStore(fixture.ConnectionFactory,async (p,_,_,ct)=>{ if(p==PontoTransacionalPrecificacao.TipoBloqueado){bloqueado.TrySetResult();await liberar.Task.WaitAsync(TimeSpan.FromSeconds(10),ct);} });
        var criacao=store.CriarVistoriaAsync(usuario,t.Id,10,PacoteVistoria.Total,Agora,Agora,default); Task<PrecoVistoria>? publicacao=null;
        try { await Task.WhenAny(bloqueado.Task,criacao).WaitAsync(TimeSpan.FromSeconds(10)); if(criacao.IsCompleted) await criacao; publicacao=Store.PublicarAsync(t.Id,new(9,ModalidadeAcrescimo.Percentual,20,1),Agora,default); }
        finally { liberar.TrySetResult(); await Task.WhenAll(criacao, publicacao is null ? Task.CompletedTask : publicacao).WaitAsync(TimeSpan.FromSeconds(10)); }
        var c=(await criacao).Precificacao!; Assert.Equal(p.Id,c.PrecoId); Assert.Equal(p.Versao,c.Versao); Assert.Equal(p.PrecoM2,c.PrecoM2); Assert.Equal(p.Modalidade,c.Modalidade); Assert.Equal(p.Acrescimo,c.Acrescimo); Assert.Equal(26.23m,c.ValorFinal);
        Assert.Equal(108m,(await Criar(t.Id,PacoteVistoria.Total)).Precificacao!.ValorFinal);
    }

    [MySqlIntegrationFact]
    public async Task SimulacaoESelecaoPreservamFinancasEAuditorias()
    {
        await fixture.LimparDadosAsync(); using var financeiro=new ProcessamentoPixCenario(fixture); await financeiro.CriarAsync(); var antes=await financeiro.SnapshotIntegralAsync(); var pagamentos=await Snapshot("pagamentos_vistoria");
        var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default); var vistorias=await Snapshot("vistorias");
        await Store.SimularAsync(new(t.Id,10,PacoteVistoria.Total),Agora,default); await Store.ListarTiposAsync(default); await Store.ListarAtivosAsync(default); await Store.HistoricoAsync(t.Id,default);
        Assert.Equal(vistorias,await Snapshot("vistorias")); Assert.Equal(antes,await financeiro.SnapshotIntegralAsync()); Assert.Equal(pagamentos,await Snapshot("pagamentos_vistoria"));
        await Criar(t.Id); Assert.Equal(antes,await financeiro.SnapshotIntegralAsync()); Assert.Equal(pagamentos,await Snapshot("pagamentos_vistoria"));
    }

    [MySqlIntegrationFact]
    public async Task CancelamentoNaoEscreve()
    { await fixture.LimparDadosAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Store.CriarTipoAsync(new("Teste",Agora),new(true))); Assert.Empty(await Store.ListarTiposAsync(default)); }

    [MySqlIntegrationFact]
    public async Task DashboardContaSomenteAtivosComOuSemPreco()
    {
        await fixture.LimparDadosAsync(); var dashboard=new AdminDashboardMySqlStore(fixture.ConnectionFactory); var vazio=await dashboard.ObterAsync(); Assert.Equal(0,vazio.TiposCadastrados); Assert.Equal(0,vazio.TiposSemConfiguracao);
        var com=await Tipo(); await Store.PublicarAsync(com.Id,Entrada(),Agora,default); await Tipo(); var inativo=await Tipo(); await Store.DesativarTipoAsync(inativo.Id,Agora,default);
        var d=await dashboard.ObterAsync(); Assert.Equal(2,d.TiposCadastrados); Assert.Equal(1,d.TiposComPrecoAtivo); Assert.Equal(1,d.TiposSemConfiguracao); Assert.Equal(1,d.UltimaVersaoPreco);
    }

    [MySqlIntegrationFact]
    public async Task Migration014ColunasDecimaisNullableEUnicidade()
    {
        Assert.Equal(2,await Escalar("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('tipos_planta','precos_vistoria');"));
        Assert.Equal(9,await Escalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='vistorias' AND column_name IN ('tipo_planta_id','preco_vistoria_id','preco_versao','preco_m2','preco_modalidade','preco_acrescimo','valor_base','valor_final','calculado_em') AND is_nullable='YES';"));
        Assert.Equal(1,await Escalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='precos_vistoria' AND column_name='preco_m2' AND numeric_precision=12 AND numeric_scale=4;"));
        Assert.Equal(1,await Escalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='vistorias' AND column_name='valor_base' AND numeric_precision=24 AND numeric_scale=6;"));
        Assert.Equal(1,await Escalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='vistorias' AND column_name='valor_final' AND numeric_precision=12 AND numeric_scale=2;"));
        Assert.Equal(1,await Escalar("SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='precos_vistoria' AND index_name='uq_precos_tipo_ativo' AND non_unique=0;"));
        Assert.Equal(1,await Escalar("SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='tipos_planta' AND index_name='uq_tipos_planta_nome' AND non_unique=0;"));
        Assert.Equal(3,await Escalar("SELECT COUNT(*) FROM information_schema.referential_constraints WHERE constraint_schema=DATABASE() AND constraint_name IN ('fk_precos_tipo','fk_vistorias_tipo_planta','fk_vistorias_preco') AND delete_rule IN ('RESTRICT','NO ACTION');"));
    }

    [MySqlIntegrationFact]
    public async Task ConstraintRejeitaDuasVersoesAtivasMesmoForaDoStore()
    {
        await fixture.LimparDadosAsync(); var t=await Tipo(); await Store.PublicarAsync(t.Id,Entrada(),Agora,default);
        await using var c=fixture.ConnectionFactory.Create(); await c.OpenAsync(); await using var cmd=new MySqlCommand("INSERT INTO precos_vistoria (id,tipo_planta_id,nome_tipo_planta,preco_m2,modalidade,acrescimo,versao,ativo,created_at,updated_at) SELECT @id,tipo_planta_id,nome_tipo_planta,preco_m2,modalidade,acrescimo,2,1,created_at,updated_at FROM precos_vistoria WHERE tipo_planta_id=@tipo;",c);
        cmd.Parameters.AddWithValue("@id",Guid.NewGuid().ToString());cmd.Parameters.AddWithValue("@tipo",t.Id.ToString());var ex=await Assert.ThrowsAsync<MySqlException>(()=>cmd.ExecuteNonQueryAsync());Assert.Equal(1062,ex.Number);Assert.Single(await Store.HistoricoAsync(t.Id,default));
    }

    [MySqlIntegrationFact]
    public async Task FalhaFkNaVistoriaNaoAlteraPreco()
    { await fixture.LimparDadosAsync();var t=await Tipo();await Store.PublicarAsync(t.Id,Entrada(),Agora,default);var antes=await Snapshot("precos_vistoria");await Assert.ThrowsAsync<MySqlException>(()=>Store.CriarVistoriaAsync(Guid.NewGuid(),t.Id,10,PacoteVistoria.Simples,Agora,Agora,default));Assert.Equal("[]",await Snapshot("vistorias"));Assert.Equal(antes,await Snapshot("precos_vistoria")); }

    [MySqlIntegrationFact]
    public async Task CancelamentoAposInsertReverteVistoriaESnapshot()
    {
        await fixture.LimparDadosAsync();var t=await Tipo();await Store.PublicarAsync(t.Id,Entrada(),Agora,default);var usuario=await Usuario();using var cts=new CancellationTokenSource();
        var store=new PrecificacaoMySqlStore(fixture.ConnectionFactory,(p,_,_,_)=>{if(p==PontoTransacionalPrecificacao.VistoriaInserida)cts.Cancel();return Task.CompletedTask;});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.CriarVistoriaAsync(usuario,t.Id,10,PacoteVistoria.Total,Agora,Agora,cts.Token));Assert.Equal("[]",await Snapshot("vistorias"));
    }

    [MySqlIntegrationFact]
    public async Task SchemaFinanceiroNaoRecebeColunasDePreco()
    {
        Assert.Equal(0,await Escalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name IN ('cashbacks','pagamentos_pix','pagamentos_vistoria','operacoes_pagamento_pix') AND column_name IN ('tipo_planta_id','preco_vistoria_id','preco_m2');"));
        Assert.Equal(2,await Escalar("SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='precos_vistoria' AND index_name='uq_precos_tipo_versao' AND non_unique=0;"));
        Assert.Equal(1,await Escalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='precos_vistoria' AND column_name='tipo_ativo' AND extra LIKE '%GENERATED%';"));
        Assert.Equal(1,await Escalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='tipos_planta' AND column_name='nome_normalizado' AND collation_name='utf8mb4_bin' AND character_maximum_length=150;"));
    }
}
