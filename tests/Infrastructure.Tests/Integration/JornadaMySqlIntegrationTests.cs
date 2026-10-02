using Application.Jornada;
using Application.Interfaces.Providers;
using Application.Recebimentos;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Infrastructure.Security;
using MySqlConnector;
using Xunit;

namespace Infrastructure.Tests.Integration;

[Collection(MySqlIntegrationCollection.Name)]
[Trait("Category", MySqlIntegrationCategory.Name)]
public sealed class JornadaMySqlIntegrationTests(MySqlIntegrationFixture fixture):IDisposable
{
    private readonly AesGcmDadosPixProtector _protector=new(Convert.ToBase64String(Enumerable.Range(1,32).Select(x=>(byte)x).ToArray()));
    private JornadaFinanceiraMySqlStore Financeiro=>new(fixture.ConnectionFactory,_protector);
    private JornadaPublicaMySqlStore Publico=>new(fixture.ConnectionFactory);
    private JornadaConsultaMySqlStore Consulta=>new(fixture.ConnectionFactory);
    public void Dispose()=>_protector.Dispose();

    private async Task<(Usuario Indicador,Vistoria Vistoria,Indicacao Indicacao,PagamentoVistoria Pagamento)> Preparar(bool dados=true,bool confirmado=true,bool indicacao=true,bool publica=false)
    {
        await fixture.LimparDadosAsync();
        var usuarios=new UsuarioMySqlRepository(fixture.ConnectionFactory);
        var indicador=IntegrationTestData.CriarUsuario();var indicada=IntegrationTestData.CriarUsuario();
        await usuarios.AdicionarAsync(indicador);await usuarios.AdicionarAsync(indicada);
        var agora=DateTime.UtcNow;var precos=new PrecificacaoMySqlStore(fixture.ConnectionFactory);
        var tipo=new TipoPlanta("Cenário fictício da jornada",agora);await precos.CriarTipoAsync(tipo,default);
        await precos.PublicarAsync(tipo.Id,new(5.0005m,ModalidadeAcrescimo.Fixo,0,0),agora,default);
        var vistoria=await precos.CriarVistoriaAsync(indicada.Id,tipo.Id,100,PacoteVistoria.Simples,agora,agora,default);
        vistoria.MarcarRealizada();await new VistoriaMySqlRepository(fixture.ConnectionFactory).AtualizarAsync(vistoria);
        var ind=new Indicacao(indicador.Id,"Pessoa fictícia","85999990000",indicador.CodigoIndicacao!);
        if(publica)
        {
            await Publico.CaptarAsync(new(indicador.CodigoIndicacao!,"Pessoa fictícia","85999990000",JornadaValidacao.VersaoTermo,true),Guid.NewGuid().ToString(),agora,default);
            ind=Assert.Single(await new IndicacaoMySqlRepository(fixture.ConnectionFactory).ObterTodasAsync());
        }
        ind.VincularUsuarioIndicado(indicada.Id);ind.VincularVistoria(vistoria.Id);
        if(publica)await new IndicacaoMySqlRepository(fixture.ConnectionFactory).AtualizarAsync(ind);
        else if(indicacao) await new IndicacaoMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(ind);
        var pagamento=new PagamentoVistoria(vistoria.Id,vistoria.Precificacao!.ValorFinal);
        if(confirmado) pagamento.ConfirmarRecebimento(Guid.NewGuid(),agora,DateTime.UtcNow);
        await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(pagamento);
        if(dados) await new DadosPixMySqlRepository(fixture.ConnectionFactory,_protector).AdicionarAsync(new(indicador.Id,TipoChavePix.Email,"ficticio@example.invalid"));
        return(indicador,vistoria,ind,pagamento);
    }
    private async Task<string> Snapshot()
    {
        using var c=new ProcessamentoPixCenario(fixture);
        return string.Join("\n",await c.SnapshotAsync("SELECT * FROM vistorias ORDER BY id"),await c.SnapshotAsync("SELECT * FROM indicacoes ORDER BY id"),
            await c.SnapshotAsync("SELECT * FROM pagamentos_vistoria ORDER BY id"),await c.SnapshotIntegralAsync(),await c.SnapshotAsync("SELECT * FROM notificacoes_internas ORDER BY id"));
    }
    private async Task<long> Quantidade(string tabela)
    {
        Assert.Contains(tabela,new[]{"cashbacks","pagamentos_pix","notificacoes_internas","indicacoes","usuarios"});
        await using var c=fixture.ConnectionFactory.Create();await c.OpenAsync();using var cmd=new MySqlCommand($"SELECT COUNT(*) FROM {tabela}",c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
    private async Task<Cashback> Cashback()=>Assert.Single(await new CashbackMySqlRepository(fixture.ConnectionFactory).ObterTodosAsync(default));

    [MySqlIntegrationFact]
    public async Task ConclusaoCalculaVintePorCentoCriaOrdemCriptografadaENotificaUmaVez()
    {
        var c=await Preparar();using var snapshots=new ProcessamentoPixCenario(fixture);
        var historico=await snapshots.SnapshotAsync("SELECT valor_final,preco_vistoria_id,preco_m2,created_at FROM vistorias");
        await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);
        var cashback=await Cashback();Assert.Equal(100.01m,cashback.Valor);Assert.Equal(.20m,cashback.Percentual);Assert.Equal(StatusCashback.Disponivel,cashback.Status);
        var pix=Assert.Single(await new PagamentoPixMySqlRepository(fixture.ConnectionFactory,_protector).ObterPorUsuarioBeneficiarioIdAsync(c.Indicador.Id,default));
        Assert.Equal(c.Indicador.Id,pix.UsuarioBeneficiarioId);Assert.Equal(cashback.Id,pix.CashbackId);Assert.Equal(StatusPagamentoPix.Pendente,pix.Status);Assert.Equal(0,pix.QuantidadeTentativas);
        Assert.Equal("ficticio@example.invalid",pix.ChavePix);
        Assert.DoesNotContain(pix.ChavePix,await snapshots.SnapshotImutavelAsync(pix.Id));
        Assert.Equal(StatusIndicacao.VistoriaConcluida,(await new IndicacaoMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(c.Indicacao.Id))!.Status);
        var antes=await Snapshot();await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);Assert.Equal(antes,await Snapshot());
        Assert.Equal(historico,await snapshots.SnapshotAsync("SELECT valor_final,preco_vistoria_id,preco_m2,created_at FROM vistorias"));
        Assert.Equal(1,await Quantidade("cashbacks"));Assert.Equal(1,await Quantidade("pagamentos_pix"));
    }
    [MySqlIntegrationFact]
    public async Task SemDadosPixConcluiENovoCadastroPermitePreparacaoIdempotente()
    {
        var c=await Preparar(dados:false);await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);
        var cb=await Cashback();Assert.Equal(StatusCashback.Disponivel,cb.Status);Assert.Equal(0,await Quantidade("pagamentos_pix"));
        Assert.Contains(await Consulta.NotificacoesAsync(c.Indicador.Id,false,default),n=>n.Tipo==TipoNotificacao.DadosPixNecessarios);
        Assert.Empty(await Financeiro.CandidatosAsync(20,default));
        await new DadosPixMySqlRepository(fixture.ConnectionFactory,_protector).AdicionarAsync(new(c.Indicador.Id,TipoChavePix.Email,"ficticio@example.invalid"));
        Assert.Equal(new[]{cb.Id},await Financeiro.CandidatosAsync(20,default));
        await Financeiro.PrepararCashbackAsync(cb.Id,default);var antes=await Snapshot();await Financeiro.PrepararCashbackAsync(cb.Id,default);
        Assert.Equal(antes,await Snapshot());Assert.Equal(1,await Quantidade("pagamentos_pix"));Assert.Empty(await Financeiro.CandidatosAsync(20,default));
    }
    [MySqlIntegrationFact]
    public async Task SemIndicacaoConcluiSemCashback()
    {var c=await Preparar(indicacao:false);await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);Assert.Equal(StatusVistoria.Concluida,(await new VistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(c.Vistoria.Id))!.Status);Assert.Equal(0,await Quantidade("cashbacks"));Assert.Equal(0,await Quantidade("pagamentos_pix"));}
    [MySqlIntegrationFact]
    public async Task IndicacaoCanceladaNaoProduzCashback()
    {var c=await Preparar();c.Indicacao.Cancelar();await new IndicacaoMySqlRepository(fixture.ConnectionFactory).AtualizarAsync(c.Indicacao);await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);Assert.Equal(0,await Quantidade("cashbacks"));Assert.Equal(0,await Quantidade("pagamentos_pix"));}
    [MySqlIntegrationFact]
    public async Task PagamentoNaoConfirmadoFalhaSemMutacao()
    {var c=await Preparar(confirmado:false);var antes=await Snapshot();await Assert.ThrowsAsync<DomainException>(()=>Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default));Assert.Equal(antes,await Snapshot());}
    [MySqlIntegrationFact]
    public async Task ValorDivergenteFalhaSemMutacao()
    {
        var c=await Preparar();using var dados=new ProcessamentoPixCenario(fixture);await dados.ExecutarAsync("UPDATE pagamentos_vistoria SET valor=valor+1 WHERE id=@id",("@id",c.Pagamento.Id.ToString()));
        var antes=await Snapshot();await Assert.ThrowsAsync<DomainException>(()=>Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default));Assert.Equal(antes,await Snapshot());
    }
    private async Task Rollback(string etapa,bool cancelar=false)
    {
        var c=await Preparar();var antes=await Snapshot();using var cancelamento=new CancellationTokenSource();
        var store=new JornadaFinanceiraMySqlStore(fixture.ConnectionFactory,_protector){Interceptar=(p,_,_)=>
        {if(p==etapa){if(cancelar)cancelamento.Cancel();else throw new InvalidOperationException("Falha fictícia controlada");}return Task.CompletedTask;}};
        if(cancelar)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.ConcluirVistoriaAsync(c.Vistoria.Id,cancelamento.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ConcluirVistoriaAsync(c.Vistoria.Id,default));
        Assert.Equal(antes,await Snapshot());
    }
    [MySqlIntegrationFact] public Task FalhaVistoriaReverteTudo()=>Rollback("Vistoria");
    [MySqlIntegrationFact] public Task FalhaIndicacaoReverteTudo()=>Rollback("Indicacao");
    [MySqlIntegrationFact] public Task FalhaCashbackReverteTudo()=>Rollback("Cashback");
    [MySqlIntegrationFact] public Task FalhaAprovacaoReverteTudo()=>Rollback("Aprovacao");
    [MySqlIntegrationFact] public Task FalhaOrdemReverteTudo()=>Rollback("OrdemPix");
    [MySqlIntegrationFact] public Task FalhaNotificacaoReverteTudo()=>Rollback("Notificacao");
    [MySqlIntegrationFact] public Task CancelamentoAntesCommitReverteTudo()=>Rollback("Notificacao",true);

    [MySqlIntegrationFact]
    public async Task ConclusoesConcorrentesCriamUmaCadeia()
    {
        var c=await Preparar();var entrou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var liberar=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primeiro=new JornadaFinanceiraMySqlStore(fixture.ConnectionFactory,_protector){Interceptar=async(p,_,_)=>{if(p=="Vistoria"){entrou.TrySetResult();await liberar.Task.WaitAsync(TimeSpan.FromSeconds(10));}}};
        var a=primeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);Task? b=null;
        try{await Task.WhenAny(entrou.Task,a).WaitAsync(TimeSpan.FromSeconds(10));if(a.IsCompleted)await a;b=Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);}
        finally{liberar.TrySetResult();await Task.WhenAll(a,b??Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(10));}
        Assert.Equal(1,await Quantidade("cashbacks"));Assert.Equal(1,await Quantidade("pagamentos_pix"));
        var notificacoes=await Consulta.NotificacoesAsync(c.Indicador.Id,false,default);Assert.Single(notificacoes,n=>n.Tipo==TipoNotificacao.CashbackCriado);
    }
    [MySqlIntegrationFact]
    public async Task CaptacaoConcorrenteRepeteProtocoloSemDuplicarOuCriarConta()
    {
        await fixture.LimparDadosAsync();var usuario=IntegrationTestData.CriarUsuario();await new UsuarioMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(usuario);
        Assert.True(await Publico.CodigoUtilizavelAsync(usuario.CodigoIndicacao!,default));
        var req=new IndicacaoPublicaRequest(usuario.CodigoIndicacao!,"Pessoa fictícia","85999990000",JornadaValidacao.VersaoTermo,true);var chave=Guid.NewGuid().ToString();
        var sinal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ProtocoloIndicacao> Captar(){await sinal.Task.WaitAsync(TimeSpan.FromSeconds(10));return await Publico.CaptarAsync(req,chave,DateTime.UtcNow,default);}
        var a=Captar();var b=Captar();ProtocoloIndicacao[] resultados;
        try{sinal.TrySetResult();resultados=await Task.WhenAll(a,b).WaitAsync(TimeSpan.FromSeconds(10));}
        finally{sinal.TrySetResult();await Task.WhenAll(a,b).WaitAsync(TimeSpan.FromSeconds(10));}
        Assert.Equal(resultados[0],resultados[1]);Assert.Matches("^[0-9a-fA-F]{64}$",resultados[0].Protocolo);
        Assert.Equal(1,await Quantidade("indicacoes"));Assert.Equal(1,await Quantidade("usuarios"));
        Assert.Equal(resultados[0],await Publico.CaptarAsync(req with {Nome="Outro nome fictício"},chave,DateTime.UtcNow,default));
        using var snapshot=new ProcessamentoPixCenario(fixture);Assert.DoesNotContain(chave,await snapshot.SnapshotAsync("SELECT idempotencia_hash FROM indicacoes"));
        await snapshot.ExecutarAsync("UPDATE usuarios SET status=2 WHERE id=@id",("@id",usuario.Id.ToString()));
        Assert.False(await Publico.CodigoUtilizavelAsync(usuario.CodigoIndicacao!,default));
        Assert.Equal(resultados[0],await Publico.CaptarAsync(req,chave,DateTime.UtcNow,default));
        Assert.Equal(1,await Quantidade("indicacoes"));
    }
    [MySqlIntegrationFact]
    public async Task PortalENotificacoesSaoRestritosAoDestinatario()
    {
        var c=await Preparar(dados:false);await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);
        var outro=IntegrationTestData.CriarUsuario();await new UsuarioMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(outro);
        Assert.Empty(await Consulta.IndicacoesAsync(outro.Id,default));Assert.Empty(await Consulta.CashbacksAsync(outro.Id,default));Assert.Empty(await Consulta.NotificacoesAsync(outro.Id,false,default));
        var notificacao=(await Consulta.NotificacoesAsync(c.Indicador.Id,false,default)).First();
        Assert.False(await Consulta.LerAsync(outro.Id,false,notificacao.Id,DateTime.UtcNow,default));
        Assert.True(await Consulta.LerAsync(c.Indicador.Id,false,notificacao.Id,DateTime.UtcNow,default));
        var indicada=Assert.Single(await Consulta.IndicacoesAsync(c.Indicador.Id,default));Assert.Equal("Pessoa",indicada.Nome);Assert.Equal("••••00",indicada.TelefoneMascarado);
    }

    [MySqlIntegrationFact]
    public async Task JornadaPublicaRecebimentoConfirmadoEEnvioFalsoFinalizamIndicacaoAtomicamente()
    {
        var c=await Preparar(confirmado:false,publica:true);
        using var protecaoCobranca=new CobrancaPixProtector(Convert.ToBase64String(Enumerable.Range(1,32).Select(x=>(byte)x).ToArray()));
        var cobrancas=new CobrancaPixVistoriaMySqlStore(fixture.ConnectionFactory,protecaoCobranca);
        var preparacao=(await cobrancas.PrepararAsync(c.Pagamento.Id,3600,default)).Preparacao!;
        var horario=DateTime.UtcNow.AddSeconds(-1);
        await cobrancas.FinalizarAsync(preparacao,new(SituacaoCobrancaProvider.Ativa,"falso",preparacao.Txid,preparacao.Valor,horario.AddSeconds(-1),3600,0,"CODIGO_FICTICIO"),default);
        var evento=RecebimentoPixValidacao.Canonicalizar(new("E00000000202609251200ABCDEFGHIJK",preparacao.Txid,preparacao.Valor,horario));
        var inbox=new RecebimentoPixWebhookMySqlStore(fixture.ConnectionFactory);await inbox.PersistirAsync([evento],default);
        var recebimento=(await inbox.AdquirirAsync(Assert.Single(await cobrancas.EventosAsync(20,default)),default))!;
        await inbox.FinalizarAsync(recebimento,evento,default);
        Assert.Equal(StatusPagamentoVistoria.Confirmado,(await new PagamentoVistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(c.Pagamento.Id))!.Status);
        await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);
        var cashback=await Cashback();var pix=(await new PagamentoPixMySqlRepository(fixture.ConnectionFactory,_protector).ObterPorCashbackIdAsync(cashback.Id))!;
        using var fluxo=new ProcessamentoPixCenario(fixture);var material=await fluxo.SnapshotImutavelAsync(pix.Id);var provider=new ProviderFalso();
        await fluxo.Processador(provider).ProcessarAsync(pix.Id,default);
        Assert.Equal(StatusCashback.Pago,(await Cashback()).Status);
        Assert.Equal(StatusIndicacao.CashbackPago,(await new IndicacaoMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(c.Indicacao.Id))!.Status);
        var depois=await Snapshot();await fluxo.Processador(provider).ProcessarAsync(pix.Id,default);
        await new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory,_protector).AplicarAsync(pix.Id);
        Assert.Equal(depois,await Snapshot());Assert.Equal(1,provider.Envios);Assert.Equal(0,provider.Consultas);
        Assert.Equal(material,await fluxo.SnapshotImutavelAsync(pix.Id));
        Assert.Single(await Consulta.NotificacoesAsync(c.Indicador.Id,false,default),n=>n.Tipo==TipoNotificacao.CashbackPago);
        Assert.Single(await Consulta.NotificacoesAsync(c.Indicador.Id,false,default),n=>n.Tipo==TipoNotificacao.PagamentoConfirmado);
    }
    [MySqlIntegrationFact]
    public async Task FalhaAoAtualizarIndicacaoPagaReverteCashbackPixENotificacoes()
    {
        var c=await Preparar();await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);var cb=await Cashback();
        var pix=(await new PagamentoPixMySqlRepository(fixture.ConnectionFactory,_protector).ObterPorCashbackIdAsync(cb.Id))!;
        using var fluxo=new ProcessamentoPixCenario(fixture);var provider=new ProviderFalso();
        await new Application.Services.PagamentoPixEnvioService(fluxo.Pagamentos,fluxo.Envios,provider).ProcessarEnvioAsync(pix.Id,default);
        var antes=await Snapshot();
        var store=new PagamentoPixAplicacaoResultadoMySqlStore(fixture.ConnectionFactory,_protector,(p,_,_,_)=>p==PontoTransacionalPix.JornadaAtualizadaAntesDoCommit?Task.FromException(new InvalidOperationException("Falha fictícia")):Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.AplicarAsync(pix.Id));Assert.Equal(antes,await Snapshot());
        Assert.Equal(StatusCashback.Disponivel,(await Cashback()).Status);
    }
    [MySqlIntegrationFact]
    public async Task FalhaConfirmadaNaoPagaCashbackENotificaAdministracao()
    {
        var c=await Preparar();await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);var cb=await Cashback();
        var pix=(await new PagamentoPixMySqlRepository(fixture.ConnectionFactory,_protector).ObterPorCashbackIdAsync(cb.Id))!;
        using var fluxo=new ProcessamentoPixCenario(fixture);await fluxo.Processador(new ProviderFalso(true)).ProcessarAsync(pix.Id,default);
        Assert.Equal(StatusCashback.Disponivel,(await Cashback()).Status);
        Assert.Equal(StatusIndicacao.VistoriaConcluida,(await new IndicacaoMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(c.Indicacao.Id))!.Status);
        Assert.Contains(await Consulta.NotificacoesAsync(c.Indicador.Id,true,default),n=>n.Tipo==TipoNotificacao.FalhaFinanceira);
        Assert.DoesNotContain(await Consulta.NotificacoesAsync(c.Indicador.Id,false,default),n=>n.Tipo==TipoNotificacao.CashbackPago);
    }
    [MySqlIntegrationFact]
    public async Task PreparacoesConcorrentesNaoDuplicamOrdem()
    {
        var c=await Preparar(dados:false);await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);var cb=await Cashback();
        await new DadosPixMySqlRepository(fixture.ConnectionFactory,_protector).AdicionarAsync(new(c.Indicador.Id,TipoChavePix.Email,"ficticio@example.invalid"));
        var sinal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Executar(){await sinal.Task.WaitAsync(TimeSpan.FromSeconds(10));await Financeiro.PrepararCashbackAsync(cb.Id,default);}
        var a=Executar();var b=Executar();
        try{sinal.TrySetResult();await Task.WhenAll(a,b).WaitAsync(TimeSpan.FromSeconds(10));}
        finally{sinal.TrySetResult();await Task.WhenAll(a,b).WaitAsync(TimeSpan.FromSeconds(10));}
        Assert.Equal(1,await Quantidade("pagamentos_pix"));Assert.Equal(StatusCashback.Disponivel,(await Cashback()).Status);
    }
    private sealed class ProviderFalso(bool falhar=false):IPixProvider
    {
        public int Envios;public int Consultas;
        public Task<PixProviderResult> EnviarAsync(PixEnvioRequest request,CancellationToken cancellationToken=default)
        {Interlocked.Increment(ref Envios);return Task.FromResult(falhar?PixProviderResult.FalhaConfirmada("falso","falso"):PixProviderResult.Confirmado("falso","falso"));}
        public Task<PixProviderResult> ConsultarAsync(PixConsultaRequest request,CancellationToken cancellationToken=default)
        {Interlocked.Increment(ref Consultas);throw new InvalidOperationException("Consulta inesperada no cenário conclusivo.");}
    }

    [MySqlIntegrationFact]
    public async Task Migration016PreservaFinanceiroEProtegeOrigemIndicesENotificacoes()
    {
        await fixture.LimparDadosAsync();using var dados=new ProcessamentoPixCenario(fixture);
        await using var c=fixture.ConnectionFactory.Create();await c.OpenAsync();
        async Task<string[]> Linhas(string sql)
        {
            using var cmd=new MySqlCommand(sql,c);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<string>();
            while(await r.ReadAsync())rows.Add(r.GetString(0));return rows.ToArray();
        }
        Assert.Equal(new[]{"usuario_indicador_id","created_at","id"},await Linhas("SELECT COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='indicacoes' AND INDEX_NAME='ix_indicacoes_portal' ORDER BY SEQ_IN_INDEX"));
        Assert.Equal(new[]{"escopo","usuario_id","lida_em","created_at","id"},await Linhas("SELECT COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='notificacoes_internas' AND INDEX_NAME='ix_notificacoes_destinatario' ORDER BY SEQ_IN_INDEX"));
        Assert.Equal(new[]{"uq_indicacoes_idempotencia","uq_indicacoes_protocolo"},await Linhas("SELECT DISTINCT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='indicacoes' AND NON_UNIQUE=0 AND INDEX_NAME IN ('uq_indicacoes_idempotencia','uq_indicacoes_protocolo') ORDER BY INDEX_NAME"));
        Assert.Equal(new[]{"YES","YES","YES","YES"},await Linhas("SELECT IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='indicacoes' AND COLUMN_NAME IN ('consentimento_em','consentimento_versao','idempotencia_hash','protocolo_publico') ORDER BY COLUMN_NAME"));
        Assert.Equal(new[]{"usuarios"},await Linhas("SELECT REFERENCED_TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='notificacoes_internas' AND CONSTRAINT_NAME='fk_notificacoes_usuario'"));
        Assert.Equal(new[]{"uq_notificacoes_evento"},await Linhas("SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='notificacoes_internas' AND INDEX_NAME='uq_notificacoes_evento' AND NON_UNIQUE=0"));
        Assert.Equal(new[]{"uq_cashbacks_pagamento_vistoria_id"},await Linhas("SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='cashbacks' AND INDEX_NAME='uq_cashbacks_pagamento_vistoria_id' AND NON_UNIQUE=0"));
        var usuario=IntegrationTestData.CriarUsuario();await new UsuarioMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(usuario);
        await Assert.ThrowsAsync<MySqlException>(()=>dados.ExecutarAsync("INSERT INTO indicacoes(id,usuario_indicador_id,nome_indicada,telefone_indicada,codigo_indicacao_usado,status,created_at,updated_at,origem) VALUES(@id,@usuario,'Ficticia','85999990000','ABCD1234',0,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),1)",("@id",Guid.NewGuid().ToString()),("@usuario",usuario.Id.ToString())));
        Assert.Equal(0,await Quantidade("indicacoes"));
    }
    [MySqlIntegrationFact]
    public async Task AtualizacoesObsoletasNaoRegridemVistoriaOuIndicacaoConcluidas()
    {
        var c=await Preparar();await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);var antes=await Snapshot();
        await Assert.ThrowsAsync<DomainException>(()=>new VistoriaMySqlRepository(fixture.ConnectionFactory).AtualizarAsync(c.Vistoria));
        await Assert.ThrowsAsync<DomainException>(()=>new IndicacaoMySqlRepository(fixture.ConnectionFactory).AtualizarAsync(c.Indicacao));
        Assert.Equal(antes,await Snapshot());
    }

    [MySqlIntegrationFact]
    public async Task LeituraAdministrativaDuranteConclusaoNaoVeCadeiaParcial()
    {
        var c=await Preparar();
        var entrou=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store=new JornadaFinanceiraMySqlStore(fixture.ConnectionFactory,_protector)
        {Interceptar=async(p,_,_)=>{if(p=="Notificacao"){entrou.TrySetResult();await liberar.Task.WaitAsync(TimeSpan.FromSeconds(10));}}};
        var antes=await Consulta.IndicadoresAsync(default);
        var tarefa=store.ConcluirVistoriaAsync(c.Vistoria.Id,default);
        try
        {
            await Task.WhenAny(entrou.Task,tarefa).WaitAsync(TimeSpan.FromSeconds(10));
            if(tarefa.IsCompleted)await tarefa;
            Assert.True(entrou.Task.IsCompletedSuccessfully);
            Assert.Equal(antes,await Consulta.IndicadoresAsync(default).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(StatusVistoria.Realizada,(await new VistoriaMySqlRepository(fixture.ConnectionFactory).ObterPorIdAsync(c.Vistoria.Id))!.Status);
            Assert.Equal(0,await Quantidade("cashbacks"));
        }
        finally{liberar.TrySetResult();await tarefa.WaitAsync(TimeSpan.FromSeconds(10));}
        Assert.Equal(1,(await Consulta.IndicadoresAsync(default)).PixPendente);
        Assert.Equal(1,await Quantidade("cashbacks"));
    }

    [MySqlIntegrationFact]
    public async Task CashbackPreexistenteCoerenteEReutilizadoSemAlterarSnapshot()
    {
        var c=await Preparar();
        var cashback=Domain.Entities.Cashback.Criar(c.Indicacao.Id,c.Pagamento.Id,c.Indicador.Id,c.Pagamento.Valor);
        await new CashbackMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(cashback);
        await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);
        var salvo=await Cashback();
        Assert.Equal(cashback.Id,salvo.Id);Assert.Equal(cashback.ValorTotalPago,salvo.ValorTotalPago);
        Assert.Equal(cashback.Percentual,salvo.Percentual);Assert.Equal(cashback.Valor,salvo.Valor);
        Assert.Equal(StatusCashback.Disponivel,salvo.Status);
        Assert.Equal(1,await Quantidade("cashbacks"));Assert.Equal(1,await Quantidade("pagamentos_pix"));
    }

    [MySqlIntegrationFact]
    public async Task OrdemPreexistenteCoerentePreservaMaterialCriptograficoETentativa()
    {
        var c=await Preparar();
        var cashback=Domain.Entities.Cashback.Criar(c.Indicacao.Id,c.Pagamento.Id,c.Indicador.Id,c.Pagamento.Valor);cashback.Aprovar();
        await new CashbackMySqlRepository(fixture.ConnectionFactory).AdicionarAsync(cashback);
        var pix=PagamentoPix.Criar(cashback.Id,c.Indicador.Id,cashback.Valor,TipoChavePix.Email,"historico@example.invalid");
        await new PagamentoPixMySqlRepository(fixture.ConnectionFactory,_protector).AdicionarAsync(pix);
        using var snapshot=new ProcessamentoPixCenario(fixture);
        var antes=await snapshot.SnapshotAsync("SELECT * FROM pagamentos_pix ORDER BY id");
        await Financeiro.ConcluirVistoriaAsync(c.Vistoria.Id,default);
        Assert.Equal(antes,await snapshot.SnapshotAsync("SELECT * FROM pagamentos_pix ORDER BY id"));
        Assert.Equal(1,await Quantidade("pagamentos_pix"));Assert.Equal(1,await Quantidade("cashbacks"));
    }
}
