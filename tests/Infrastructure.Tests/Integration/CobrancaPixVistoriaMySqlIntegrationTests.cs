using Application.Recebimentos;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Infrastructure.Security;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class CobrancaPixVistoriaMySqlIntegrationTests(MySqlIntegrationFixture fixture)
{
    private static CobrancaPixProtector Protector() => new(Convert.ToBase64String(Enumerable.Range(1,32).Select(x=>(byte)x).ToArray()));
    private async Task<PagamentoVistoria> Pagamento()
    {
        await fixture.LimparDadosAsync();
        var usuario = IntegrationTestData.CriarUsuario();
        await new UsuarioMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(usuario,default);
        var vistoria = IntegrationTestData.CriarVistoria(usuario.Id);
        await new VistoriaMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(vistoria,default);
        var pagamento = IntegrationTestData.CriarPagamentoVistoria(vistoria.Id,12.34m);
        await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(pagamento,default);
        return pagamento;
    }

    [MySqlIntegrationFact]
    public async Task PreparacaoPersisteIdentidadeAuditoriaELeaseAntesDoProvider()
    {
        var pagamento = await Pagamento(); using var protector = Protector();
        var store = new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var geracao = await store.PrepararAsync(pagamento.Id,3600,default);
        var p = Assert.IsType<PreparacaoCobranca>(geracao.Preparacao);
        Assert.Equal(geracao.Id,p.Id); Assert.Equal(pagamento.Id,p.PagamentoId); Assert.Equal(pagamento.Valor,p.Valor);
        Assert.Equal(p.Id.ToString("N"),p.Txid); Assert.NotEqual(Guid.Empty,p.LeaseId);
        var audit = Assert.Single(await store.AuditoriaAsync(p.Id,default));
        Assert.Equal(p.OperacaoId,audit.Id); Assert.Equal(0,audit.Tipo); Assert.Null(audit.FinishedAt);
        Assert.Null(await store.AdquirirAsync(p.Id,default));
        var novamente = await store.PrepararAsync(pagamento.Id,3600,default);
        Assert.Equal(p.Id,novamente.Id); Assert.Null(novamente.Preparacao);
        Assert.Single(await store.ListarAsync(default));
    }

    [MySqlIntegrationFact]
    public async Task FalhaDePreparacaoReverteCobrancaAuditoriaEPagamento()
    {
        var pagamento = await Pagamento(); using var protector = Protector(); using var dados = new ProcessamentoPixCenario(fixture);
        var antes = await dados.SnapshotAsync("SELECT * FROM pagamentos_vistoria ORDER BY id");
        var store = new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector)
        { Interceptar = (p,_,_) => p=="Preparacao" ? Task.FromException(new InvalidOperationException("falha-controlada")) : Task.CompletedTask };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.PrepararAsync(pagamento.Id,3600,default));
        Assert.Empty(await store.ListarAsync(default));
        Assert.Equal("[]",await dados.SnapshotAsync("SELECT * FROM operacoes_cobranca_pix"));
        Assert.Equal(antes,await dados.SnapshotAsync("SELECT * FROM pagamentos_vistoria ORDER BY id"));
    }

    [MySqlIntegrationFact]
    public async Task RecuperacaoExpiradaConsultaMesmaIdentidadeERejeitaExecutorAntigo()
    {
        var pagamento = await Pagamento(); using var protector = Protector(); using var dados = new ProcessamentoPixCenario(fixture);
        var store = new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var inicial = (await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        await dados.ExecutarAsync("UPDATE cobrancas_pix_vistoria SET lease_expira_em=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 SECOND) WHERE id=@id",("@id",inicial.Id.ToString()));
        var recuperada = Assert.IsType<PreparacaoCobranca>(await store.AdquirirAsync(inicial.Id,default));
        Assert.Equal(OperacaoCobranca.Consultar,recuperada.Operacao);
        Assert.Equal(inicial.Id,recuperada.Id); Assert.Equal(inicial.Txid,recuperada.Txid); Assert.Equal(inicial.Valor,recuperada.Valor);
        Assert.NotEqual(inicial.LeaseId,recuperada.LeaseId);
        var antes = await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria ORDER BY id");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.FinalizarAsync(inicial,new(SituacaoCobrancaProvider.Indeterminada,"falso"),default));
        Assert.Equal(antes,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria ORDER BY id"));
        Assert.Single(await store.ListarAsync(default));
    }

    [MySqlIntegrationFact]
    public async Task CodigoPublicoCriptografadoELinkRotacionadoInvalidaAnterior()
    {
        var pagamento = await Pagamento(); using var protector = Protector(); using var dados = new ProcessamentoPixCenario(fixture);
        var store = new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var p = (await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        await store.FinalizarAsync(p,new(SituacaoCobrancaProvider.Ativa,"ok",p.Txid,p.Valor,DateTime.UtcNow,3600,0,"CODIGO_FICTICIO"),default);
        var hash = RecebimentoPixValidacao.HashLink(new string('A',64))!;
        await store.RotacionarLinkAsync(p.Id,hash,DateTime.UtcNow.AddHours(1),default);
        var publico = await store.ObterPublicaAsync(hash,default);
        Assert.NotNull(publico); Assert.Equal("CODIGO_FICTICIO",publico.PixCopiaECola); Assert.False(publico.Confirmada);
        Assert.DoesNotContain("CODIGO_FICTICIO",await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria"));
        await store.RotacionarLinkAsync(p.Id,RecebimentoPixValidacao.HashLink(new string('B',64))!,DateTime.UtcNow.AddHours(1),default);
        Assert.Null(await store.ObterPublicaAsync(hash,default));
    }

    [MySqlIntegrationFact]
    public async Task FalhaAoFinalizarReverteAuditoriaEConservaLease()
    {
        var pagamento = await Pagamento(); using var protector = Protector(); using var dados = new ProcessamentoPixCenario(fixture);
        var store = new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector)
        { Interceptar = (p,_,_) => p=="Finalizacao" ? Task.FromException(new InvalidOperationException("falha-controlada")) : Task.CompletedTask };
        var p = (await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        var antes = await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.FinalizarAsync(p,new(SituacaoCobrancaProvider.Indeterminada,"falso"),default));
        Assert.Equal(antes,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria"));
        Assert.Null(Assert.Single(await store.AuditoriaAsync(p.Id,default)).FinishedAt);
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
    }

    private async Task<(PagamentoVistoria Pagamento, PreparacaoCobranca Preparacao, EventoPix Evento)> Ativar(CobrancaPixVistoriaMySqlStore store)
    {
        var pagamento=await Pagamento();
        var p=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        var agora=DateTime.UtcNow.AddSeconds(-1);
        await store.FinalizarAsync(p,new(SituacaoCobrancaProvider.Ativa,"ok",p.Txid,p.Valor,agora.AddSeconds(-1),3600,0,"CODIGO_FICTICIO"),default);
        return (pagamento,p,RecebimentoPixValidacao.Canonicalizar(new("E00000000202609251200ABCDEFGHIJK",p.Txid,p.Valor,agora)));
    }

    [MySqlIntegrationFact]
    public async Task InboxDuplicadaNaoConfirmaSemConsultaEConfirmacaoEhAtomicaIdempotente()
    {
        using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,p,eventData)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{eventData,eventData},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        var repository=new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await repository.ObterPorIdAsync(pagamento.Id,default))!.Status);
        var preparacao=Assert.IsType<PreparacaoRecebimento>(await inbox.AdquirirAsync(id,default));
        await inbox.FinalizarAsync(preparacao,eventData,default);
        var confirmado=(await repository.ObterPorIdAsync(pagamento.Id,default))!;
        Assert.Equal(StatusPagamentoVistoria.Confirmado,confirmado.Status);
        Assert.Equal(eventData.Horario,confirmado.PagoEm);
        Assert.Equal(StatusCobrancaPixVistoria.Confirmada,Assert.Single(await store.ListarAsync(default)).Status);
        await inbox.PersistirAsync(new[]{eventData},default);
        Assert.Empty(await store.EventosAsync(20,default));
        Assert.Null(await inbox.AdquirirAsync(id,default));
        Assert.Equal(2,(await store.AuditoriaAsync(p.Id,default)).Count);
        using var dados=new ProcessamentoPixCenario(fixture);
        Assert.Equal("[]",await dados.SnapshotAsync("SELECT * FROM cashbacks"));
        Assert.Equal("[]",await dados.SnapshotAsync("SELECT * FROM pagamentos_pix"));
    }

    [MySqlIntegrationFact]
    public async Task FalhaEntreCobrancaEPagamentoReverteConfirmacaoEInbox()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,_,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory)
        { Interceptar=(p,_,_)=>p=="CobrancaAntesPagamento" ? Task.FromException(new InvalidOperationException("falha-controlada")) : Task.CompletedTask };
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        var preparacao=(await inbox.AdquirirAsync(id,default))!;
        var cobrancaAntes=await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria");
        var inboxAntes=await dados.SnapshotAsync("SELECT * FROM recebimentos_pix_inbox");
        var pagamentoAntes=await dados.SnapshotAsync("SELECT * FROM pagamentos_vistoria");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>inbox.FinalizarAsync(preparacao,evento,default));
        Assert.Equal(cobrancaAntes,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria"));
        Assert.Equal(inboxAntes,await dados.SnapshotAsync("SELECT * FROM recebimentos_pix_inbox"));
        Assert.Equal(pagamentoAntes,await dados.SnapshotAsync("SELECT * FROM pagamentos_vistoria"));
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
    }

    [MySqlIntegrationFact]
    public async Task ValorDivergenteNuncaConfirmaPagamento()
    {
        using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,_,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,evento with { Valor=1m },default);
        Assert.Equal(StatusCobrancaPixVistoria.DivergenciaFinanceira,Assert.Single(await store.ListarAsync(default)).Status);
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
        Assert.Empty(await store.EventosAsync(20,default));
    }

    [MySqlIntegrationFact]
    public async Task DuasPreparacoesConcorrentesPersistemUmaCobrancaEUmLease()
    {
        var pagamento=await Pagamento(); using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var inicio=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<GeracaoCobranca> Executar() { await inicio.Task; return await store.PrepararAsync(pagamento.Id,3600,default); }
        var tarefas=new[]{Executar(),Executar()};
        try
        {
            inicio.TrySetResult();
            var resultados=await Task.WhenAll(tarefas).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(resultados.Select(x=>x.Id).Distinct());
            Assert.Single(resultados,x=>x.Preparacao is not null);
            Assert.Single(await store.AuditoriaAsync(resultados[0].Id,default));
        }
        finally { inicio.TrySetResult(); await Task.WhenAll(tarefas).WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [MySqlIntegrationFact]
    public async Task TokenAdulteradoNaoFinalizaAuditoriaNemLiberaLease()
    {
        var pagamento=await Pagamento(); using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var p=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        var antes=await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria");
        var auditoria=await dados.SnapshotAsync("SELECT * FROM operacoes_cobranca_pix");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.FinalizarAsync(p with { LeaseId=Guid.NewGuid() },new(SituacaoCobrancaProvider.Indeterminada,"falso"),default));
        Assert.Equal(antes,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria"));
        Assert.Equal(auditoria,await dados.SnapshotAsync("SELECT * FROM operacoes_cobranca_pix"));
    }

    [MySqlIntegrationFact]
    public async Task CancelamentoLegadoNaoPodeContornarCobrancaPersistida()
    {
        var pagamento=await Pagamento(); using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        await store.PrepararAsync(pagamento.Id,3600,default);
        pagamento.Cancelar();
        var repository=new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        await Assert.ThrowsAsync<Domain.Exceptions.DomainException>(()=>repository.AtualizarAsync(pagamento,default));
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await repository.ObterPorIdAsync(pagamento.Id,default))!.Status);
    }

    [MySqlIntegrationFact]
    public async Task ReemissaoSomenteDepoisDaExpiracaoComprovadaMantemHistorico()
    {
        var pagamento=await Pagamento(); using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var original=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        await store.FinalizarAsync(original,new(SituacaoCobrancaProvider.Ativa,"ok",original.Txid,original.Valor,DateTime.UtcNow.AddHours(-2),3600,0,"CODIGO_FICTICIO"),default);
        Assert.Equal(StatusCobrancaPixVistoria.Expirada,Assert.Single(await store.ListarAsync(default)).Status);
        var nova=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        Assert.NotEqual(original.Id,nova.Id); Assert.NotEqual(original.Txid,nova.Txid);
        Assert.Equal(original.Valor,nova.Valor); Assert.Equal(2,(await store.ListarAsync(default)).Count);
        Assert.Single(await store.AuditoriaAsync(original.Id,default));
    }

    [MySqlIntegrationFact]
    public async Task CancelamentoEsperaRemocaoConfirmadaPeloProvider()
    {
        using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,p,evento)=await Ativar(store);
        var repository=new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        await store.SolicitarCancelamentoAsync(pagamento.Id,default);
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await repository.ObterPorIdAsync(pagamento.Id,default))!.Status);
        var remocao=(await store.AdquirirAsync(p.Id,default))!;
        Assert.Equal(OperacaoCobranca.Remover,remocao.Operacao);
        await store.FinalizarAsync(remocao,new(SituacaoCobrancaProvider.Removida,"ok",p.Txid,p.Valor,evento.Horario.AddSeconds(-1),3600,1),default);
        Assert.Equal(StatusPagamentoVistoria.Cancelado,(await repository.ObterPorIdAsync(pagamento.Id,default))!.Status);
        Assert.Equal(StatusCobrancaPixVistoria.Removida,Assert.Single(await store.ListarAsync(default)).Status);
    }

    [MySqlIntegrationFact]
    public async Task ConsultaIndeterminadaNaoConfirmaELimitaProximaSelecao()
    {
        using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,_,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,null,default);
        Assert.Empty(await store.EventosAsync(20,default));
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
    }

    [MySqlIntegrationFact]
    public async Task LeituraPublicaAuditoriaESelecaoNaoAlteramDados()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (_,p,_)=await Ativar(store);
        var hash=RecebimentoPixValidacao.HashLink(new string('A',64))!;
        await store.RotacionarLinkAsync(p.Id,hash,DateTime.UtcNow.AddHours(1),default);
        var cobranca=await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria");
        var pagamento=await dados.SnapshotAsync("SELECT * FROM pagamentos_vistoria");
        await store.ObterPublicaAsync(hash,default); await store.AuditoriaAsync(p.Id,default);
        await store.CobrançasAsync(20,default); await store.EventosAsync(20,default); await store.IndicadoresAsync(default);
        Assert.Equal(cobranca,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria"));
        Assert.Equal(pagamento,await dados.SnapshotAsync("SELECT * FROM pagamentos_vistoria"));
    }

    [MySqlIntegrationFact]
    public async Task Migration015ProtegeUnicidadePrecisaoEFksRestritivasSemSeed()
    {
        await fixture.LimparDadosAsync();
        await using var c=fixture.ConnectionFactory.Create(); await c.OpenAsync();
        async Task<long> Contar(string sql)
        { using var command=new MySqlConnector.MySqlCommand(sql,c); return Convert.ToInt64(await command.ExecuteScalarAsync()); }
        Assert.Equal(3,await Contar("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('cobrancas_pix_vistoria','recebimentos_pix_inbox','operacoes_cobranca_pix')"));
        Assert.Equal(4,await Contar("SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='cobrancas_pix_vistoria' AND constraint_type='UNIQUE'"));
        Assert.Equal(3,await Contar("SELECT COUNT(*) FROM information_schema.referential_constraints WHERE constraint_schema=DATABASE() AND table_name IN ('cobrancas_pix_vistoria','operacoes_cobranca_pix') AND delete_rule='RESTRICT'"));
        Assert.Equal(2,await Contar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name IN ('cobrancas_pix_vistoria','recebimentos_pix_inbox') AND column_name='valor' AND data_type='decimal' AND numeric_precision=12 AND numeric_scale=2"));
        Assert.Equal(2,await Contar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name IN ('cobrancas_pix_vistoria','recebimentos_pix_inbox') AND column_name='lease_expira_em' AND datetime_precision=6 AND is_nullable='YES'"));
        Assert.Equal(1,await Contar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='cobrancas_pix_vistoria' AND column_name='pagamento_nao_terminal' AND extra LIKE '%STORED GENERATED%'"));
        Assert.Equal(0,await Contar("SELECT COUNT(*) FROM cobrancas_pix_vistoria"));
        Assert.Equal(0,await Contar("SELECT COUNT(*) FROM recebimentos_pix_inbox"));
    }

    [MySqlIntegrationFact]
    public async Task RecebimentoTardioDeCobrancaExpiradaNaoConfirmaNovaCobranca()
    {
        var pagamento=await Pagamento(); using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var original=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        await store.FinalizarAsync(original,new(SituacaoCobrancaProvider.Ativa,"ok",original.Txid,original.Valor,DateTime.UtcNow.AddHours(-2),3600,0),default);
        var nova=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        var evento=RecebimentoPixValidacao.Canonicalizar(new("E00000000202609251200ABCDEFGHIJK",original.Txid,original.Valor,DateTime.UtcNow.AddSeconds(-1)));
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,evento,default);
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
        Assert.Equal(StatusCobrancaPixVistoria.CriacaoEmProcessamento,(await store.ListarAsync(default)).Single(x=>x.Id==nova.Id).Status);
        Assert.True((await store.IndicadoresAsync(default)).Divergencias>0);
    }

    [MySqlIntegrationFact]
    public async Task PagamentoInexistenteOuTerminalNaoCriaCobranca()
    {
        var pagamento=await Pagamento(); using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        await Assert.ThrowsAsync<Domain.Exceptions.PagamentoVistoriaNaoEncontradoException>(()=>store.PrepararAsync(Guid.NewGuid(),3600,default));
        pagamento.Cancelar();
        await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).AtualizarAsync(pagamento,default);
        await Assert.ThrowsAsync<Domain.Exceptions.DomainException>(()=>store.PrepararAsync(pagamento.Id,3600,default));
        Assert.Empty(await store.ListarAsync(default));
    }

    [MySqlIntegrationFact]
    public async Task DoisProcessadoresDisputamInboxComUmaConsultaEUmaConfirmacao()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,p,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        var entrou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider=new ProviderFalso { Receber=async idRecebido=>
        { Assert.Equal(evento.EndToEndId,idRecebido); entrou.TrySetResult(); await liberar.Task.WaitAsync(TimeSpan.FromSeconds(10)); return evento; } };
        var processor=new RecebimentoPixProcessamentoService(store,inbox,provider,new(){Habilitado=true});
        var primeira=processor.ProcessarEventoAsync(id,default);
        try
        {
            await AguardarProvider(entrou.Task,primeira);
            await processor.ProcessarEventoAsync(id,default).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(StatusPagamentoVistoria.Pendente,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
            Assert.Equal(1,provider.ConsultasRecebimento);
        }
        finally { liberar.TrySetResult(); await primeira.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(StatusPagamentoVistoria.Confirmado,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
        Assert.Single(await store.AuditoriaAsync(p.Id,default),x=>x.Tipo==3);
        Assert.Equal("[]",await dados.SnapshotAsync("SELECT * FROM cashbacks"));
        Assert.Equal("[]",await dados.SnapshotAsync("SELECT * FROM pagamentos_pix"));
    }

    private static async Task AguardarProvider(Task sinal,Task principal)
    {
        var primeira=await Task.WhenAny(sinal,principal).WaitAsync(TimeSpan.FromSeconds(10));
        if(primeira==principal) { await principal; Assert.True(sinal.IsCompletedSuccessfully,"Operação terminou antes do provider."); }
        await sinal.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [MySqlIntegrationFact]
    public async Task InboxExpiradaPermiteNovoExecutorSemAutorizarTokenAntigo()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (_,_,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        var antigo=(await inbox.AdquirirAsync(id,default))!;
        await dados.ExecutarAsync("UPDATE recebimentos_pix_inbox SET lease_expira_em=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 SECOND) WHERE id=@id",("@id",id.ToString()));
        var novo=(await new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory).AdquirirAsync(id,default))!;
        Assert.NotEqual(antigo.LeaseId,novo.LeaseId); Assert.Equal(antigo.Evento,novo.Evento);
        var antes=await dados.SnapshotAsync("SELECT * FROM recebimentos_pix_inbox");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>inbox.FinalizarAsync(antigo,evento,default));
        Assert.Equal(antes,await dados.SnapshotAsync("SELECT * FROM recebimentos_pix_inbox"));
        await inbox.FinalizarAsync(novo,evento,default);
        Assert.Equal(StatusCobrancaPixVistoria.Confirmada,Assert.Single(await store.ListarAsync(default)).Status);
    }

    [MySqlIntegrationFact]
    public async Task TimeoutRecuperaPorGetEAusenciaPermitePutSomenteNaInvocacaoPosterior()
    {
        var pagamento=await Pagamento(); using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        var provider=new ProviderFalso();
        var p=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        provider.Criar=(txid,valor,prazo)=>
        { Assert.Equal(p.Txid,txid); Assert.Equal(p.Valor,valor); Assert.Equal(3600,prazo); throw new HttpRequestException("falha-ficticia"); };
        var processor=new RecebimentoPixProcessamentoService(store,inbox,provider,new(){Habilitado=true});
        await Assert.ThrowsAsync<HttpRequestException>(()=>processor.ExecutarPreparacaoAsync(p,default));
        Assert.Equal(StatusCobrancaPixVistoria.Indeterminada,Assert.Single(await store.ListarAsync(default)).Status);
        provider.Consultar=txid=> { Assert.Equal(p.Txid,txid); return Task.FromResult(new ResultadoCobrancaProvider(SituacaoCobrancaProvider.Ausente,"404")); };
        await processor.ProcessarCobrancaAsync(p.Id,default);
        Assert.Equal(1,provider.Criacoes);
        Assert.Equal(StatusCobrancaPixVistoria.Preparada,Assert.Single(await store.ListarAsync(default)).Status);
        provider.Criar=(txid,valor,prazo)=>
        { Assert.Equal(p.Txid,txid); Assert.Equal(p.Valor,valor); Assert.Equal(3600,prazo); return Task.FromResult(new ResultadoCobrancaProvider(SituacaoCobrancaProvider.Ativa,"ok",txid,valor,DateTime.UtcNow.AddSeconds(-1),prazo,0)); };
        await processor.ProcessarCobrancaAsync(p.Id,default);
        Assert.Equal(2,provider.Criacoes); Assert.Equal(1,provider.Consultas);
        Assert.Equal(StatusCobrancaPixVistoria.Ativa,Assert.Single(await store.ListarAsync(default)).Status);
    }

    [MySqlIntegrationFact]
    public async Task ReemissaoConcorrenteCriaUmaNovaIdentidadePreservandoAnterior()
    {
        var pagamento=await Pagamento(); using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var p=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        await store.FinalizarAsync(p,new(SituacaoCobrancaProvider.Ativa,"ok",p.Txid,p.Valor,DateTime.UtcNow.AddHours(-2),3600,0),default);
        var historico=await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria WHERE status=6");
        var inicio=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<GeracaoCobranca> Rodar() { await inicio.Task; return await store.PrepararAsync(pagamento.Id,3600,default); }
        var tarefas=new[]{Rodar(),Rodar()};
        try
        {
            inicio.TrySetResult(); var resultados=await Task.WhenAll(tarefas).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(resultados.Select(x=>x.Id).Distinct()); Assert.Single(resultados,x=>x.Preparacao is not null);
            Assert.NotEqual(p.Id,resultados[0].Id);
            Assert.Equal(historico,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria WHERE status=6"));
            Assert.Equal(2,(await store.ListarAsync(default)).Count);
        }
        finally { inicio.TrySetResult(); await Task.WhenAll(tarefas).WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [MySqlIntegrationFact]
    public async Task TxidDesconhecidoERecebimentoDePagamentoCanceladoGeramDivergencia()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,_,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        var desconhecido=evento with { Txid=Guid.NewGuid().ToString("N") };
        await inbox.PersistirAsync(new[]{desconhecido},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,desconhecido,default);
        await dados.ExecutarAsync("UPDATE pagamentos_vistoria SET status=2 WHERE id=@id",("@id",pagamento.Id.ToString()));
        await inbox.PersistirAsync(new[]{evento},default);
        id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,evento,default);
        Assert.Equal(StatusPagamentoVistoria.Cancelado,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(pagamento.Id,default))!.Status);
        Assert.Equal(3,(await store.IndicadoresAsync(default)).Divergencias); // Duas inbox divergentes e uma cobrança sinalizada.
    }

    [MySqlIntegrationFact]
    public async Task CiphertextAdulteradoFalhaFechadoETokenExpiradoNaoDescriptografa()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (_,p,_)=await Ativar(store);
        var hash=RecebimentoPixValidacao.HashLink(new string('A',64))!;
        await store.RotacionarLinkAsync(p.Id,hash,DateTime.UtcNow.AddHours(1),default);
        await dados.ExecutarAsync("UPDATE cobrancas_pix_vistoria SET pix_tag=UNHEX('00000000000000000000000000000000') WHERE id=@id",("@id",p.Id.ToString()));
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(()=>store.ObterPublicaAsync(hash,default));
        await dados.ExecutarAsync("UPDATE cobrancas_pix_vistoria SET link_expira_em=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 SECOND) WHERE id=@id",("@id",p.Id.ToString()));
        Assert.Null(await store.ObterPublicaAsync(hash,default));
    }

    [MySqlIntegrationFact]
    public async Task ExpiracaoDuranteFinalizacaoReverteAuditoriaEAtualizacao()
    {
        var pagamento=await Pagamento(); using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector)
        { Interceptar=async(p,c,t)=>
          {
              if(p!="Auditoria") return;
              using var command=new MySqlConnector.MySqlCommand("UPDATE cobrancas_pix_vistoria SET lease_expira_em=DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 SECOND)",c,t);
              await command.ExecuteNonQueryAsync();
          } };
        var p=(await store.PrepararAsync(pagamento.Id,3600,default)).Preparacao!;
        var antes=await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.FinalizarAsync(p,new(SituacaoCobrancaProvider.Indeterminada,"ok"),default));
        Assert.Equal(antes,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria"));
        Assert.Null(Assert.Single(await store.AuditoriaAsync(p.Id,default)).FinishedAt);
    }

    [MySqlIntegrationFact]
    public async Task PersistenciaInboxFalhaSemAceitarParcialmenteLote()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (_,_,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory)
        { Interceptar=(_,_,_)=>Task.FromException(new InvalidOperationException("falha-controlada")) };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>inbox.PersistirAsync(new[]{evento},default));
        Assert.Equal("[]",await dados.SnapshotAsync("SELECT * FROM recebimentos_pix_inbox"));
    }

    [MySqlIntegrationFact]
    public async Task EvidenciaVinculadaAOutraCobrancaNaoConfirmaSegundoPagamento()
    {
        using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (_,_,evento)=await Ativar(store);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,evento,default);
        var usuario=IntegrationTestData.CriarUsuario();
        await new UsuarioMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(usuario,default);
        var vistoria=IntegrationTestData.CriarVistoria(usuario.Id);
        await new VistoriaMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(vistoria,default);
        var segundo=IntegrationTestData.CriarPagamentoVistoria(vistoria.Id,12.34m);
        var repository=new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        await repository.AdicionarAsync(segundo,default);
        var p=(await store.PrepararAsync(segundo.Id,3600,default)).Preparacao!;
        await store.FinalizarAsync(p,new(SituacaoCobrancaProvider.Ativa,"ok",p.Txid,p.Valor,evento.Horario.AddSeconds(-1),3600,0),default);
        var duplicado=evento with { Txid=p.Txid };
        await inbox.PersistirAsync(new[]{duplicado},default);
        id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,duplicado,default);
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await repository.ObterPorIdAsync(segundo.Id,default))!.Status);
        Assert.Single(await store.ListarAsync(default),x=>x.Status==StatusCobrancaPixVistoria.Confirmada);
        Assert.Single(await store.ListarAsync(default),x=>x.Status==StatusCobrancaPixVistoria.DivergenciaFinanceira);
    }

    [MySqlIntegrationFact]
    public async Task ConstraintsImpedemTxidELeaseIncompativeisSemMutacao()
    {
        using var protector=Protector(); using var dados=new ProcessamentoPixCenario(fixture);
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (_,p,_)=await Ativar(store);
        var antes=await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria");
        await Assert.ThrowsAsync<MySqlConnector.MySqlException>(()=>dados.ExecutarAsync("UPDATE cobrancas_pix_vistoria SET txid='invalido' WHERE id=@id",("@id",p.Id.ToString())));
        await Assert.ThrowsAsync<MySqlConnector.MySqlException>(()=>dados.ExecutarAsync("UPDATE cobrancas_pix_vistoria SET lease_id=@lease WHERE id=@id",("@id",p.Id.ToString()),("@lease",Guid.NewGuid().ToString())));
        await Assert.ThrowsAsync<MySqlConnector.MySqlException>(()=>dados.ExecutarAsync("INSERT INTO cobrancas_pix_vistoria(id,pagamento_vistoria_id,valor,txid,status,expiracao_segundos,proxima_consulta_em,created_at,updated_at) SELECT @novo,pagamento_vistoria_id,valor,txid,6,expiracao_segundos,proxima_consulta_em,created_at,updated_at FROM cobrancas_pix_vistoria WHERE id=@id",("@id",p.Id.ToString()),("@novo",Guid.NewGuid().ToString())));
        Assert.Equal(antes,await dados.SnapshotAsync("SELECT * FROM cobrancas_pix_vistoria"));
    }

    [MySqlIntegrationFact]
    public async Task CancelamentoIndeterminadoNaoCancelaEConfirmacaoTardiaAposRemocaoEhDivergencia()
    {
        using var protector=Protector();
        var store=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protector);
        var (pagamento,p,evento)=await Ativar(store);
        await store.SolicitarCancelamentoAsync(pagamento.Id,default);
        var remover=(await store.AdquirirAsync(p.Id,default))!;
        await store.FinalizarAsync(remover,new(SituacaoCobrancaProvider.Indeterminada,"transport"),default);
        var repository=new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory);
        Assert.Equal(StatusPagamentoVistoria.Pendente,(await repository.ObterPorIdAsync(pagamento.Id,default))!.Status);
        var consultar=(await store.AdquirirAsync(p.Id,default))!;
        Assert.Equal(OperacaoCobranca.Consultar,consultar.Operacao);
        await store.FinalizarAsync(consultar,new(SituacaoCobrancaProvider.Removida,"ok",p.Txid,p.Valor,evento.Horario.AddSeconds(-1),3600,1),default);
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);
        await inbox.PersistirAsync(new[]{evento},default);
        var id=Assert.Single(await store.EventosAsync(20,default));
        await inbox.FinalizarAsync((await inbox.AdquirirAsync(id,default))!,evento,default);
        Assert.Equal(StatusPagamentoVistoria.Cancelado,(await repository.ObterPorIdAsync(pagamento.Id,default))!.Status);
        Assert.True(Assert.Single(await store.ListarAsync(default)).Divergencia);
    }
    private sealed class ProviderFalso : ICobrancaPixVistoriaProvider
    {
        public Func<string,decimal,int,Task<ResultadoCobrancaProvider>> Criar { get; set; }=(_,_,_)=>throw new InvalidOperationException("Criar não esperado.");
        public Func<string,Task<ResultadoCobrancaProvider>> Consultar { get; set; }=_=>throw new InvalidOperationException("Consultar não esperado.");
        public Func<string,Task<EventoPix?>> Receber { get; set; }=_=>throw new InvalidOperationException("Recebimento não esperado.");
        private int _criacoes, _consultas, _recebimentos;
        public int Criacoes=>Volatile.Read(ref _criacoes);
        public int Consultas=>Volatile.Read(ref _consultas);
        public int ConsultasRecebimento=>Volatile.Read(ref _recebimentos);
        public Task<ResultadoCobrancaProvider> CriarAsync(string txid,decimal valor,int prazo,CancellationToken ct)
        { Interlocked.Increment(ref _criacoes); return Criar(txid,valor,prazo); }
        public Task<ResultadoCobrancaProvider> ConsultarAsync(string txid,CancellationToken ct)
        { Interlocked.Increment(ref _consultas); return Consultar(txid); }
        public Task<EventoPix?> ConsultarRecebimentoAsync(string id,CancellationToken ct)
        { Interlocked.Increment(ref _recebimentos); return Receber(id); }
        public Task<ResultadoCobrancaProvider> RemoverAsync(string txid,CancellationToken ct)=>throw new InvalidOperationException("Remoção não esperada.");
        public Task<bool> ConfigurarWebhookAsync(CancellationToken ct)=>throw new InvalidOperationException("Webhook não esperado.");
        public Task<bool> ConsultarWebhookAsync(CancellationToken ct)=>throw new InvalidOperationException("Webhook não esperado.");
    }
}
