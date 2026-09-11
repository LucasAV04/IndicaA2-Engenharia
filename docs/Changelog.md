# Changelog

## 2026-09-10 — Lease Persistente de Envio Pix — PR #32

### Adicionado

- Migration `012_add_envio_lease_pagamentos_pix.sql`, com `envio_lease_id` e `envio_lease_expira_em`; sem backfill, execução automática ou tabela genérica de locks.
- Lease de Envio de cinco minutos, medido por `UTC_TIMESTAMP(6)` do MySQL, com token opaco por aquisição.
- Preparação de Envio transacional: lock de PagamentoPix, token/expiração, mudança para `Processando`, incremento da tentativa e auditoria aberta antes do provider.
- Finalização condicionada por token, operação, pagamento, tentativa, referência e prazo: resultado, metadados e limpeza do lease ocorrem na mesma transação sem apagar metadados válidos com `null`/vazio.

### Alterado

- A Efí documenta `idEnvio` como idempotente; a recuperação de Envio expirado assume novo token e reutiliza a mesma operação, tentativa e `referencia_idempotente` persistida, sem criar segundo Envio.
- Reconciliação retorna `EnvioEmAndamento` para lease válido e `EnvioPendenteRecuperacao` para lease expirado com Envio aberto; nesses casos não limpa lease, não cria Consulta e não chama provider.
- Aplicação financeira bloqueia qualquer marcador de lease de Envio, inclusive expirado pendente de recuperação, retornando `RequerReconciliacao` sem mutar auditoria ou valores.
- Após resposta, exceção ou cancelamento do provider, a finalização usa `CancellationToken.None`; perda de autorização ou falha de persistência permanece explícita e não autoriza reenvio.
- Envio e Consulta simultaneamente abertos no mesmo ciclo agora falham fechados e explicitamente; nenhum lease, auditoria, PagamentoPix ou Cashback é alterado durante a detecção.
- Envio aberto com resultado ou metadados já preenchidos é inconsistência auditável: não sobrescreve, não apaga e não libera lease. O update de finalização exige metadados persistidos nulos e não usa `COALESCE` inalcançável.
- A referência usada pelo adapter é documentada pela Efí no endpoint idempotente `PUT /v3/gn/pix/:idEnvio`; o IndicA2 a envia diretamente como segmento `idEnvio`.
- O teste unitário de retomada artificial foi substituído por integração MySQL com expiração controlada, token antigo rejeitado e provider falso idempotente; testes adicionais cobrem os estados de lease de Envio na reconciliação.
- O teste concorrente agora libera o executor bloqueado em `finally`, aplica timeout apenas como proteção e observa todas as tarefas iniciadas. Estados de auditoria inválidos usam snapshots SQL brutos; a expiração provocada durante a finalização precisa reverter também `envio_lease_expira_em`.
- Os dois stores validam leases parciais ou simultâneos sem limpeza/reparação silenciosa; a reconciliação não cria Consulta nem chama provider nesses estados.

### Validação

- Build: sucesso, 0 erros, 0 warnings.
- Seleção sem MySQL de Envio, reconciliação, contrato do provider e adapter Efí: 70 aprovados, 0 falhos, 0 ignorados (46 em `Application.Tests` e 24 em `Infrastructure.Tests`).
- Suíte rápida sem MySQL/Efí: 467 aprovados, 0 falhos, 0 ignorados.
- A cobertura MySQL foi ampliada para 119 casos em 13 classes, incluindo interleaving de recuperação do Envio, bloqueio da aplicação por lease de Envio, preservação de lease na reconciliação, rollback da finalização e auditoria adulterada. Essas novas integrações permanecem pendentes de execução controlada; nenhuma migration foi executada e elas não são declaradas aprovadas nesta etapa.
- Sem `INDICA2_TEST_MYSQL_CONNECTION`, as integrações MySQL desta etapa não foram executadas e não são declaradas aprovadas. Não houve Efí real, OAuth real ou Pix real.

### Escopo

- Expiração não inicia novo Envio nem retry automático; Envio legado aberto sem lease exige regularização auditada.
- Worker, webhook, endpoint de disparo, produção e limpeza administrativa permanecem pendentes.
- PR #31 concluído por Squash and merge no commit `6c259642c0c95764ff1d73aa8640b6b946861ddf`.

## 2026-09-09 — Validação Definitiva do PR #31

### Validação

- Build limpo: sucesso, 0 erros e 0 warnings.
- Preflight MySQL: 6 aprovados, 0 falhos, 0 ignorados.
- Stores e reconciliação Pix: 33 aprovados, 0 falhos, 0 ignorados, em 15,7 segundos.
- Script oficial MySQL (`pwsh -NoProfile -File .\scripts\Invoke-MySqlIntegrationTests.ps1 -RequireMySql`): 105 executados, 105 aprovados, 0 falhos, 0 ignorados; testes em 16,0 segundos, comando em 17,0 segundos, um único preflight `SELECT 1`, banco temporário exclusivo e migrations 001–011 aplicadas. A migration 011 e o lease persistente foram validados contra MySQL real.
- Suíte rápida (`dotnet test IndicaA2.slnx --no-restore --filter "Category!=MySqlIntegration&FullyQualifiedName!~EfiPixSandboxIntegrationTests&FullyQualifiedName!~EfiPixTlsDiagnosticTests" --logger "console;verbosity=minimal"`): 463 executados, 463 aprovados, 0 falhos, 0 ignorados; testes em 44,1 segundos, comando em 69,9 segundos e exit code 0.

### Confirmado

- Materialização `CHAR(36)` como `Guid` pelo MySqlConnector tratada por `ObterGuid`/`ObterGuidOpcional`, inclusive no snapshot de integração.
- Timeout concorrente anterior confirmado como consequência da `InvalidCastException` anterior ao provider.
- Lease de cinco minutos pelo horário MySQL, recuperação da mesma Consulta, proteção contra executor antigo, preservação de `identificador_provider`/`codigo` e aplicação financeira atômica/idempotente validados.
- Preflight mantém bloqueio contra execução acidental das 105 integrações sem configuração explícita.

### Segurança e escopo

- Nenhuma chamada Efí real, OAuth real, Pix real ou dado financeiro de produção foi utilizado. MySQL foi usado exclusivamente na suíte de integração.
- O PR permanece draft e não há liberação para produção. Não foi fornecida confirmação independente sobre a inexistência posterior de bancos temporários; esta entrada não declara essa verificação.
- Os registros abaixo que mencionam pendência MySQL ou 29 falhas de API são históricos intermediários, superados por esta validação definitiva.

## 2026-09-09 — Compatibilidade de Materialização GUID no MySQL — PR #31

### Corrigido

- A primeira execução real MySQL revelou que `CHAR(36)` pode ser materializado pelo `MySqlConnector` como `Guid`, tornando incompatível o uso de `GetString` em identificadores.
- Os stores de aplicação de resultado e reconciliação usam `MySqlDataReaderExtensions.ObterGuid`/`ObterGuidOpcional`; o snapshot de `usuario_indicador_id` do teste de integração usa a mesma extensão.
- O timeout de duas reconciliações concorrentes era efeito da `InvalidCastException` anterior ao sinal do provider e não foi alterado.

### Pendente

- **Registro intermediário superado:** a validação completa posterior das 105 integrações MySQL foi concluída com 105 aprovados, conforme a entrada de validação definitiva.

## 2026-09-08 — Execução Controlada de Integrações MySQL — PR #31

### Alterado

- Os 105 testes de integração MySQL, em 13 classes, receberam a categoria única `MySqlIntegration`; nenhum teste ou asserção foi removido.
- As integrações podem ser descobertas pelo VSTest para aplicação do filtro, mas não são executadas nem contabilizadas como ignoradas na suíte rápida; isso impede conexões, migrations, escritas e retries MySQL.
- `scripts/Invoke-MySqlIntegrationTests.ps1` requer PowerShell 7.4+. Sem variável, o modo opcional retorna `0` com `SKIPPED`; `-RequireMySql` retorna `2`. Nenhum deles chama `dotnet test`. Com variável, há um único preflight `SELECT 1` e somente então a suíte MySQL; falha não inicia bootstrap/migrations e não há retries.
- Script e fixture calculam o marcador efêmero SHA-256 sobre o mesmo texto original da variável, sem normalização. Execução direta da categoria continua fazendo uma única sondagem por processo antes do bootstrap.

### Validação

- Build: sucesso, 0 erros e 4 avisos de nulabilidade preexistentes em `Usuario`/`UsuarioService`, fora deste escopo.
- A descoberta atual da suíte rápida é 463: 457 testes anteriores + 6 testes de preflight. O resultado histórico 461 corresponde a quando havia quatro testes; a investigação registrou 462 após o quinto; o sexto cobre os códigos de saída do script sem variável.
- Preflight específico: 6 aprovados, 0 falhos, 0 ignorados. **Registro intermediário superado:** essa execução apresentou 434 aprovados e 29 falhos. Uma execução limpa posterior da mesma suíte registrou 463 aprovados, 0 falhos e 0 ignorados. Nenhuma alteração de código da API faz parte dos commits corretivos desta etapa; portanto, esta documentação não atribui uma causa definitiva às falhas intermediárias.
- Sem `INDICA2_TEST_MYSQL_CONNECTION`, não houve MySQL, migration, Efí, OAuth ou Pix real. Integrações não foram declaradas aprovadas.

## 2026-09-08 — Recuperação de Reconciliação com Lease — PR #31

### Corrigido

- A coordenação anterior deixava uma Consulta aberta bloquear permanentemente o pagamento após queda do processo. A migration 011 adiciona token e expiração em `pagamentos_pix`, sem tabela nova nem alteração financeira.
- **Lease de reconciliação: 5 minutos, medidos pelo horário do MySQL.** Usa `UTC_TIMESTAMP(6)`, sem renovação automática. Após expiração, uma reconciliação explícita assume novo token e reutiliza a mesma Consulta aberta.
- Consulta ativa impede nova reconciliação; token antigo ou expirado não autoriza finalização, liberação ou sobrescrita. Consulta, eventual recuperação do Envio e liberação do lease são atômicas. Expiração durante a finalização provoca rollback.
- Exceção/cancelamento do provider finaliza `Indeterminado` e libera somente um lease válido; falha de persistência continua explícita e recuperável após expiração. Provider permanece fora da transação; nenhum retry automático foi criado.
- Recuperação do Envio preserva `identificador_provider` e `codigo` da evidência conclusiva persistida, sem apagar valores válidos com null/branco.
- Aplicação financeira bloqueia Consulta aberta ou qualquer lease pendente, inclusive expirado, sem recuperar auditoria. As transições de Domain existentes são executadas sobre entidades reidratadas sob lock antes dos updates atômicos de PagamentoPix/Cashback.

### Testes e implantação

- Nenhum teste do HEAD `f313f0c` foi removido. Cobertura anterior restaurada/substituída por testes de token, falhas, cancelamento, concorrência determinística, metadados, idempotência, rollback e tentativas 1–5. A tabela individual de substituições está em [Implementações](Implementacoes.md#cobertura-restauradasubstituída).
- UP/DOWN da migration documentados. Interromper executores antigos antes da implantação; não há backfill. Consulta legada aberta sem lease exige regularização auditada separada, sem presumir abandono.
- Build final: sucesso, 0 erros e 0 warnings; aviso preexistente de `UsuarioService` observado em execução anterior, sem alteração fora do escopo.
- Suíte final: **562 testes; 457 aprovados; 0 falhos; 105 integrações MySQL ignoradas** por ausência de `INDICA2_TEST_MYSQL_CONNECTION`. Comando/filtro exatos em [Implementações](Implementacoes.md#validação-desta-revisão).
- Integrações e migration não foram validadas contra MySQL nesta execução. `git diff --check` sem erros; zero chamadas Efí/OAuth/Pix real. Documentos binários preservados; sem push ou merge.

## 2026-09-04 — Coordenação de Reconciliação e Aplicação de Resultado Pix

### Corrigido

- Eliminado o intervalo entre a leitura da auditoria e a liquidação financeira: aplicação e preparação de reconciliação agora se serializam pelo mesmo registro persistido de `pagamentos_pix`.
- A aplicação relê o ciclo atual sob transação e bloqueio de linhas, bloqueando liquidação quando existir Consulta aberta; evidências conclusivas conflitantes falham fechadas antes de qualquer alteração financeira.
- A preparação de Consulta foi movida para store transacional próprio. Ela persiste a auditoria antes da chamada ao provider e não cria Consulta nem chama provider quando a aplicação financeira já tiver concluído a ordem.
- Reforçada a coerência de `FalhaConfirmada`: tentativas 1–4 resultam em `Falhou`; somente a quinta resulta em `FalhaDefinitiva`.

### Validação

- Build da solução: sucesso, 0 erros e um aviso preexistente de nulabilidade em `UsuarioService`.
- Suíte local sem Efí externo: 442 aprovados, 0 falhos e 89 integrações MySQL ignoradas por ausência de `INDICA2_TEST_MYSQL_CONNECTION` no processo.

## 2026-09-04 — Aplicação Segura do Resultado de PagamentoPix

### Adicionado

- Caso de uso interno para transformar somente evidência conclusiva e já auditada do ciclo atual em estado financeiro interno, sem provider, envio, consulta ou mutação da auditoria.
- Transição de domínio `Cashback.Disponivel → Pago`, com idempotência em `Pago` e rejeição de estados não elegíveis.
- Store transacional MySQL que coordena `PagamentoPix.Concluido + Cashback.Pago` de forma atômica e aplica `FalhaConfirmada` sem alterar o Cashback.
- Cobertura para idempotência, concorrência, rollback e preservação de snapshots financeiros e da auditoria.

### Decisões

- `Confirmado` e `FalhaConfirmada` são descobertos exclusivamente no histórico persistido do ciclo atual; resultados de tentativas anteriores e evidências conflitantes não são aplicados.
- `FalhaConfirmada` não inicia retry. Política de nova tentativa, worker, webhook, seleção automática e observabilidade permanecem pendentes.

## 2026-09-03 — Reconciliação Segura de PagamentoPix

### Adicionado

- Caso de uso interno para consultar o provider pela referência idempotente original e registrar uma OperacaoPagamentoPix de Consulta antes da chamada externa.
- Correção do escopo histórico: a reconciliação ancora o ciclo no Envio cuja tentativa é igual a QuantidadeTentativas; resultados e consultas anteriores não decidem a tentativa Processando, e evidências conclusivas conflitantes no ciclo atual falham fechadas.
- Resultado seguro que diferencia ordem não aplicável, histórico já conclusivo e consulta executada, sem expor Dados Pix ou detalhes técnicos.
- Recuperação auditável de um único Envio aberto quando a nova consulta é conclusiva, com tratamento da finalização concorrente.
- Recuperação do Envio atual aberto quando uma Consulta conclusiva do mesmo ciclo já estiver persistida, sem nova consulta ao provider.

### Decisões

- Reconciliação nunca envia Pix, não incrementa tentativa e não altera PagamentoPix ou Cashback.
- Consulta anterior aberta não bloqueia nova consulta; resultados Pendente e Indeterminado preservam a evidência aberta para reconciliação posterior.
- Endpoint, worker, webhook, migration, retentativa e mudança no adapter Efí continuam fora do escopo.

## 2026-09-02 — Orquestração Segura de Envio PagamentoPix

### Adicionado

- Porta transacional específica para adquirir uma tentativa de PagamentoPix e criar sua auditoria de envio na mesma transação MySQL.
- Caso de uso interno PagamentoPixEnvioService, que só chama o provider depois do commit, usa snapshots da ordem, mapeia o resultado provider-agnostic e finaliza a operação de auditoria.
- Cobertura de Application e integração MySQL condicional para rollback, concorrência, chamada única ao provider simulado e preservação de snapshots.

### Decisões

- Perder o claim é resultado esperado: não chama provider nem cria auditoria.
- Depois da preparação, cancelamento, exceção ambígua ou falha de persistência da finalização não autorizam novo envio. A auditoria pode permanecer aberta para reconciliação futura.
- O resultado do provider não muda PagamentoPix nem Cashback; ambos permanecem fora de coordenação financeira até uma feature futura.
- Não foram criados endpoint, worker, webhook, migration, retentativa automática ou alteração no adapter Efí.

## 2026-09-02 — Auditoria Persistente de Operações de PagamentoPix

### Adicionado

- Registro provider-agnostic de início e finalização única de envios e consultas Pix, com referência idempotente canônica, resultado normalizado e identificação opaca opcional do provider.
- Persistência MySQL em `operacoes_pagamento_pix`, FK restritiva, índices de histórico/operações abertas e proteção concorrente por atualização condicional.

### Decisões

- A auditoria não armazena chave Pix, payload, token, credenciais, certificado ou mensagens externas completas; tampouco altera `PagamentoPix` ou `Cashback`.
- Orquestração de provider, webhook, reconciliação, worker e produção permanecem fora do escopo.

## 2026-08-31 — Adapter Efí Pix em Sandbox/Homologação

### Adicionado

- Adapter `EfiPixProvider` na Infrastructure por `HttpClient` direto, com OAuth, mTLS P12/PFX externo, cache em memória por escopo e bloqueio explícito de produção.
- Envio oficial v3 por `PUT /v3/gn/pix/{idEnvio}` (`pix.send`) e consulta oficial por `GET /v2/gn/pix/enviados/id-envio/{idEnvio}` (`gn.pix.send.read`), sempre usando `ReferenciaIdempotente` como `idEnvio`.
- Tradução isolada do protocolo Efí para `PixProviderResult`, sem expor SDK, HTTP, tokens, certificado, payload ou tipos Efí à Application.
- Cobertura unitária de OAuth, HTTP, cache, concorrência, expiração, cancelamento, falhas ambíguas, resultados normalizados e bloqueio de produção; teste de consulta sandbox é opcional e não envia Pix.
- Testes sandbox condicionais exigem configuração completa e são marcados como ignorados quando ela está ausente; o envio usa variáveis distintas para as chaves Pix pagadora e favorecida, sem registrar seus valores.

### Validação de homologação

- OAuth e mTLS foram validados previamente contra a Efí, com certificado externo carregado por `DefaultKeySet` e validação TLS padrão.
- O envio de homologação de R$ 0,01, o callback POST da Efí em receptor temporário e a consulta posterior pelo mesmo `idEnvio` foram confirmados manualmente.

### Decisões

- A integração adotou HTTP direto. A SDK EfiPay não foi instalada porque a documentação oficial atual privilegia envio v3, enquanto a POC anterior observou rota v2.
- `EM_PROCESSAMENTO` não confirma pagamento; timeout, transporte, resposta inválida, `409`, `429` e `5xx` são `Indeterminado` e exigem reconciliação antes de qualquer nova tentativa.
- Webhook próprio, autenticação/validação de callback em produção, worker, auditoria persistida, reconciliação, recuperação de ordens `Processando` após crash, coordenação de `PagamentoPix`/`Cashback`, retentativas pós-reconciliação, observabilidade e produção permanecem fora do escopo.

## 2026-08-28 — Fronteira Provider-Agnostic de PagamentoPix

### Adicionado

- Contrato `IPixProvider` na Application para envio e consulta/reconciliação de Pix, sem implementação concreta.
- Requests internos imutáveis com referência idempotente determinística por `PagamentoPix.Id`, no formato canônico `Guid.ToString("N")`.
- Resultado provider-agnostic que separa confirmação, falha confirmada, pendência e indeterminação, sem transportar mensagem técnica ou dados Pix.
- Testes de determinismo, segurança da chave Pix, semântica dos resultados e independência da Efí.

### Decisões

- `Pendente` e `Indeterminado` não são falhas e não permitem retentativa automática; ambos exigem consulta futura ao provider usando a mesma referência idempotente.
- Timeout ou interrupção local não permite devolver uma ordem `Processando` para `Falhou` nem reenviar dinheiro sem reconciliação.
- Esta etapa não altera `PagamentoPix`, `Cashback`, claim atômico, schema, migrations, API ou Infrastructure.

## 2026-08-26 — Claim Atômico de Processamento de PagamentoPix

### Adicionado

- Aquisição atômica no MySQL para iniciar processamento da ordem, usando `UPDATE` condicional parametrizado e `affected rows` como resultado do claim.
- Atualização indivisível para `Processando`, incremento da tentativa e `updated_at`; apenas `Pendente` e `Falhou` podem adquirir uma nova tentativa.
- Contrato de Application que diferencia PagamentoPix inexistente de uma ordem existente que não adquiriu o claim.
- Testes reais condicionais de concorrência com dois, cinco e dez executores independentes, além de estados, limite de cinco, snapshots, material criptográfico, Cashback e cancelamento.

### Decisões

- Perder o claim é comportamento esperado e retorna `false`; não há lock em memória como garantia financeira.
- O claim não cria ordem, não altera `Cashback`, não recriptografa Chave Pix e não adiciona endpoint HTTP, schema ou migration.
- Concorrência de processamento deixa de ser bloqueio para a futura integração financeira, mas provider, Efí, Pix real, webhook e confirmação de pagamento continuam pendentes.

## 2026-08-26 — API Administrativa de PagamentoPix

### Adicionado

- `PagamentosPixController` protegido pela policy `Administrador`, com criação exclusivamente por `CashbackId`, consultas por ID/cashback/beneficiário e cancelamento da ordem.
- Registro de `IPagamentoPixService` no composition root e exposição controlada de `CancelarAsync`, reutilizando a transição de domínio existente.
- Resposta `201 Created` com `Location` para a consulta por ID, além de mapeamento `404` para `PagamentoPixNaoEncontradoException`.
- Cobertura unitária e HTTP real para contrato administrativo, Bearer, `401`, `403`, `404`, `422`, ausência de Chave Pix e documentação OpenAPI das rotas protegidas.

### Decisões

- A API não aceita valor, beneficiário, chave Pix, tipo de chave, status ou tentativas do cliente; todos os snapshots são derivados pelo caso de uso já existente.
- Não foram expostos endpoints de processamento, envio, pagamento, confirmação, retentativa ou webhook. Criar, cancelar ou consultar uma ordem não altera automaticamente o Cashback para `Pago`.
- Concorrência de processamento segue como requisito bloqueante antes da integração com provider financeiro. Efí e Pix real continuam fora do escopo.

## 2026-08-26 — Autenticação contextual do snapshot de PagamentoPix

### Corrigido

- `PagamentoPixMySqlRepository` passou a proteger e descriptografar a chave Pix com AAD `PagamentoPix:v1`, autenticando `Id`, `CashbackId`, `UsuarioBeneficiarioId`, `Valor` e `TipoChavePix`.
- A serialização do contexto é determinística: GUIDs canônicos, valor monetário invariável com duas casas e enum persistido como inteiro.
- A troca integral de material criptográfico entre ordens, ou a alteração direta de qualquer snapshot autenticado, passa a falhar na autenticação AES-GCM.
- Status, quantidade de tentativas e `updated_at` não fazem parte do AAD; atualizações normais mantêm o material criptográfico e continuam válidas.
- `DadosPix` mantém os métodos originais sem AAD contextual, preservando a compatibilidade dos registros existentes.
- Buffers temporários de plaintext são apagados após criptografar ou converter a chave descriptografada em string.

### Decisões

- `encryption_version = 1` permanece a versão do material AES-GCM. `PagamentoPix:v1` identifica somente o esquema de contexto autenticado.
- Migration 009 não foi alterada; AAD é reconstruído a partir dos snapshots imutáveis e não precisa ser persistido.
- Concorrência de processamento permanece uma evolução obrigatória antes do envio Pix real, sem solução escolhida nesta etapa.

## 2026-08-25 — Persistência MySQL Segura de PagamentoPix

### Adicionado

- Migration `009_create_pagamentos_pix.sql`, tabela `pagamentos_pix`, `UNIQUE uq_pagamentos_pix_cashback_id` e FKs restritivas para cashback e usuário beneficiário, sem cascade.
- Reidratação controlada de `PagamentoPix`, `PagamentoPixMySqlRepository` e registro scoped de `IPagamentoPixRepository`.
- Persistência criptografada do snapshot de `ChavePix` com o protector AES-256-GCM já existente: ciphertext, nonce, tag e `encryption_version`; não há coluna plaintext nem segredo versionado.
- Atualização limitada a status, quantidade de tentativas e timestamp. Snapshots, ciphertext, nonce, tag e versão de criptografia permanecem imutáveis após a criação.
- Testes de reidratação, DI, bootstrap, schema, integridade, roundtrip, ausência de plaintext, adulteração criptográfica, concorrência e imutabilidade dos snapshots.

### Decisões

- Somente a violação de `uq_pagamentos_pix_cashback_id` é convertida para `PagamentoPixJaExisteException`; FKs e demais constraints permanecem erros reais do MySQL.
- `PagamentoPix.Concluido` persistido não atualiza automaticamente o Cashback. API, provider, Efí, envio Pix real e confirmação financeira continuam fora do escopo.

## 2026-08-25 — Domain e Application de PagamentoPix

### Adicionado

- Ordem interna `PagamentoPix`, seus snapshots financeiros e de Dados Pix, `StatusPagamentoPix`, contratos de repository/service, DTO de resposta seguro, mapper manual e exceções específicas.
- Criação exclusiva por `CashbackId`, aceita somente Cashback `Disponivel`, deriva valor e beneficiário do snapshot de Cashback e usa os Dados Pix cadastrados do beneficiário.
- Máquina de estados de tentativa: máximo de cinco, contagem no início, quinta falha para `FalhaDefinitiva`, sem sexta tentativa automática e cancelamento idempotente apenas em estados permitidos.
- Testes de Domain e Application para snapshots, regras de tentativa, elegibilidade, duplicidade, ausência de Dados Pix, `CancellationToken` e ausência de alteração de Cashback.

### Decisões

- `PagamentoPix` não paga nem marca Cashback como `Pago`; a confirmação real futura deverá atualizar ambos de modo confiável.
- A garantia definitiva contra concorrência é aplicada pela Infrastructure por `UNIQUE(cashback_id)`. API, provider e integração Efí continuam fora do escopo.

## 2026-08-25 — Infrastructure MySQL Segura de Dados Pix

### Adicionado

- Migration `008_create_dados_pix.sql`, tabela `dados_pix`, `UNIQUE(usuario_id)` e FK restritiva para `usuarios`, sem cascade.
- `DadosPixMySqlRepository`, registro de `IDadosPixRepository` e reidratação controlada de `DadosPix`.
- AES-256-GCM com chave externa em Base64 de 32 bytes, nonce aleatório de 12 bytes, tag de 16 bytes e `encryption_version`.
- Persistência exclusiva de ciphertext, nonce e tag; `ChavePix` não é gravada em texto puro nem incluída em mensagens de falha criptográfica.
- Tradução específica de `uq_dados_pix_usuario_id` para `DadosPixJaExisteException`.
- Testes de criptografia, reidratação, integração MySQL, ausência de plaintext, alteração de material criptográfico e adulteração autenticada.

### Pendente

- API de Dados Pix, PagamentoPix, Efí, providers financeiros, Pix real, webhook, OAuth e mTLS.

## 2026-08-24 — Dados Pix do Usuário

### Adicionado

- `DadosPix` e `TipoChavePix` (`Cpf`, `Cnpj`, `Email`, `Telefone` e `Aleatoria`), com `IDadosPixRepository`, DTOs, mapper manual, `IDadosPixService` e `DadosPixService`.
- Validações determinísticas e normalizações: CPF/CNPJ com dígitos verificadores, e-mail com estrutura coerente, telefone Pix brasileiro em representação numérica com `55` e UUID canônico para chave aleatória.
- Cobertura de Domain e Application para criação, alteração, remoção idempotente, ausência opcional, validações e `CancellationToken`.

### Decisões

- Um usuário pode ter zero ou uma configuração ativa de Dados Pix. Não possuir chave é permitido e não bloqueia os fluxos atuais de usuário, indicação ou cashback.
- `Cnpj` foi formalizado como extensão dos tipos de chave Pix originalmente previstos, sem alteração de cardinalidade ou comportamento dos fluxos atuais.
- A futura Infrastructure deverá criptografar `ChavePix` em repouso; algoritmo, gestão de chaves e persistência concreta continuam pendentes.
- A futura ordem de `PagamentoPix` usará snapshot da chave e do tipo; alterações futuras do cadastro não mudam registros históricos.
- Para o fluxo futuro foi formalizado: `Cashback 1 → 0..1 PagamentoPix`, até cinco tentativas por ordem, `FalhaDefinitiva` após a quinta falha, sem sexta tentativa automática, Cashback mantido em `Disponivel` e intervenção administrativa necessária.
- `PagamentoPix`, tentativas, Infrastructure, migration, API, Efí e integrações financeiras continuam fora do escopo.
- Para a futura Infrastructure de Dados Pix, foram definidos: `UNIQUE(usuario_id)` para garantir 0..1 configuração por usuário; criptografia em repouso de `ChavePix`, sem texto puro ou logs completos; e reidratação controlada de `Id`, `UsuarioId`, `TipoChavePix`, `ChavePix` descriptografada e timestamps, sem invocar métodos de domínio.

## 2026-08-24 — API Administrativa de Cashback

### Adicionado

- Controller administrativo, geração por pagamento, consultas, aprovação, cancelamento, registro de `ICashbackService` e mapeamentos 404 específicos.
- Cobertura de controller, autorização JWT, OpenAPI e ausência de endpoints de pagamento/Pix.

### Decisões

- Todos os endpoints exigem `Administrador`; nenhum contrato HTTP recebe snapshots financeiros. `Pago`, PagamentoPix e Efí permanecem fora do escopo.

## 2026-08-21 — Persistência MySQL de Cashback

### Adicionado

- Migration `007_create_cashbacks.sql`, tabela `cashbacks`, reidratação controlada, `CashbackMySqlRepository` e registro de `ICashbackRepository` na Infrastructure.
- Constraint `uq_cashbacks_pagamento_vistoria_id`, FKs restritivas para indicação, pagamento e usuário indicador, e atualização restrita a `status` e `updated_at`.
- Testes de reidratação, DI, bootstrap de schema e integração MySQL condicional para snapshots, status, timestamps, consultas, atualização e concorrência.

### Decisões

- Snapshots financeiros históricos não são recalculados na leitura. A violação de unicidade de pagamento é traduzida somente quando corresponde à constraint específica; outras duplicate keys continuam sendo erros MySQL.
- Aprovação administrativa, `PagamentoPix`, Efí e demais integrações financeiras permanecem fora desta etapa.

## 2026-08-21 — Domain e Application de Cashback

### Adicionado

- `Cashback`, `ICashbackRepository`, `ICashbackService`, `CashbackService`, DTO de resposta, mapper manual e exceções específicas.
- Geração exclusivamente por `PagamentoVistoriaId`, resolvendo a indicação por `VistoriaId` e o beneficiário por `UsuarioIndicadorId`.
- Snapshot financeiro de valor total pago, percentual fixo de 20% e valor de cashback calculado internamente com `decimal` e arredondamento monetário.
- Fluxo inicial `Pendente → Disponivel` por aprovação manual e cancelamento de `Pendente` ou `Disponivel`; não existe operação para marcar cashback como `Pago`.
- Cobertura de Domain/Application para cálculo, arredondamento, rastreabilidade, duplicidade, elegibilidade, aprovação, cancelamento e propagação de `CancellationToken`.

### Decisões

- Somente `PagamentoVistoria` confirmado fornece `ValorTotalPago`; pagamentos pendentes ou cancelados não geram cashback.
- O beneficiário é o usuário indicador, nunca o usuário indicado. A futura persistência deverá garantir `UNIQUE(pagamento_vistoria_id)` contra concorrência.
- Cashback, PagamentoPix, Efí, providers, API, migration e Infrastructure concreta continuam fora do escopo.

## 2026-08-21 — Cardinalidade única entre Indicação e Vistoria

### Adicionado

- `ObterPorVistoriaIdAsync` em `IIndicacaoRepository` e em `IndicacaoMySqlRepository`, com consulta SQL parametrizada.
- Migration `006_add_unicidade_vistoria_indicacoes.sql`, que cria `uq_indicacoes_vistoria_id` e impede duas indicações para a mesma vistoria, preservando múltiplos `NULL`.
- `VistoriaJaVinculadaOutraIndicacaoException`, emitida apenas quando a violação é `DuplicateKeyEntry` da constraint específica.
- Testes de Application e integração MySQL condicional para navegação reversa, ausência de vínculo, concorrência e múltiplos valores nulos.

### Decisões

- `Indicacao.VistoriaId` permanece a única fonte de verdade do relacionamento; `Vistoria` não recebe `IndicacaoId`.
- Não foi adicionada FK em `indicacoes.vistoria_id`, pois a compatibilidade de dados históricos não foi auditada. Nenhuma correção automática ou saneamento de dados foi executado.
- O suporte prepara somente a cadeia futura `PagamentoVistoria → Vistoria → Indicacao → UsuarioIndicadorId`; Cashback continua não implementado.

## 2026-08-21 — Persistência MySQL de Pagamento de Vistoria

### Adicionado

- Migration `005_create_pagamentos_vistoria.sql`, tabela com FK restritiva para `vistorias`, `DECIMAL(12,2)`, enum persistido como `INT`, `DATETIME(6)` e `UNIQUE(vistoria_id)`.
- `PagamentoVistoriaMySqlRepository`, reidratação segura sem executar `Confirmar()`, registro de DI e testes de integração contra banco temporário.
- `PagamentoVistoriaDuplicadoException`, emitida somente para `DuplicateKeyEntry` da constraint `uq_pagamentos_vistoria_vistoria_id`; outras violações MySQL não são mascaradas.

### Decisões

- A Application previne duplicidade, mas o MySQL é a garantia definitiva contra concorrência para uma vistoria possuir no máximo um pagamento.
- Somente pagamento confirmado fornece futuramente `ValorTotalPago`; cashback de 20% para `UsuarioIndicadorId` permanece fora do escopo. Efí continua adiada.

## 2026-08-20 — Domínio e Application de Pagamento de Vistoria

### Adicionado

- `PagamentoVistoria` como pagamento recebido do cliente pela A2, com valor decimal normalizado, vínculo obrigatório à vistoria e estados `Pendente`, `Confirmado` e `Cancelado`.
- Contrato de repository, DTOs, mapper manual, service, exceção específica e testes para o módulo inicial.

### Decisões

- Há no máximo um pagamento por vistoria na versão inicial; parcelas, reembolsos, recebimento Pix, provider, API, MySQL e DI não foram implementados.
- `PagamentoVistoria.Valor` é o valor registrado/esperado: em `Pendente` não representa dinheiro efetivamente recebido, em `Confirmado` passa a ser a futura fonte de `ValorTotalPago`, e em `Cancelado` nunca é elegível. O futuro cashback pertencerá ao usuário indicador e será `ValorTotalPago * 0.20m`; nenhum cálculo ou atualização foi introduzido.
- A futura Infrastructure deverá garantir `UNIQUE(vistoria_id)`, traduzir somente a violação dessa constraint e reidratar todos os campos persistidos sem invocar transições de domínio. A futura API administrativa mapeará `PagamentoVistoriaNaoEncontradoException` para `404 Not Found`.
- `StatusCashback` histórico foi preservado sem alteração. `PagamentoPix` permanece um futuro pagamento de saída da A2 ao indicador.

## 2026-08-17 — Consistência entre indicador e código no fluxo legado

### Corrigido

- A criação legada de indicação passou a exigir que `UsuarioIndicadorId` e `CodigoIndicacaoUsado` representem o mesmo usuário comum.
- O código informado é normalizado antes da comparação e da persistência do snapshot. Combinações inconsistentes, administradores e usuários históricos sem código são rejeitados com `DomainException` (422).
- O fluxo de criação por código para administradores não foi alterado.

## 2026-08-17 — Integração de código de indicação em Indicações

### Adicionado

- Caso de uso administrativo para criar indicação a partir de `CodigoIndicacao`, com normalização, busca do usuário indicador e snapshot canônico em `CodigoIndicacaoUsado`.
- DTO restrito `CreateIndicacaoPorCodigoDto`, endpoint protegido `POST /api/indicacoes/por-codigo` e exceção semântica `CodigoIndicacaoNaoEncontradoException` mapeada para 404.
- Cobertura de Application, controller, pipeline de autorização e integração MySQL condicional para o fluxo por código.

### Corrigido

- A ação de consulta por identificador passou a declarar explicitamente o nome utilizado por `CreatedAtAction`, evitando falha de geração da rota `Location` nos fluxos de criação.

### Decisões

- O endpoint legado `POST /api/indicacoes` não foi alterado. O fluxo por código é exclusivamente administrativo; não foi criada consulta pública de código.
- Formato inválido de código continua como violação de domínio e retorna 422, conforme o handler global já adotado.

## 2026-08-17 — Correção de invariantes e colisão concorrente de código

### Corrigido

- A construção normal de `Usuario` comum agora rejeita código de indicação nulo ou vazio; somente a reidratação histórica permite ausência temporária do valor.
- `UsuarioService` passou a considerar também colisões reais no `INSERT`: a violação específica de `uq_usuarios_codigo_indicacao` gera retry, limitado a cinco tentativas, sem recalcular o hash da senha.
- `UsuarioMySqlRepository` traduz somente a violação de chave duplicada referente ao código de indicação para `CodigoIndicacaoDuplicadoException`. A violação de unicidade do e-mail preserva o comportamento anterior.

### Testes

- Adicionados casos para as invariantes de criação/reidratação, colisão concorrente, limite de tentativas, hash único, e-mail duplicado e tradução real da constraint MySQL.

## 2026-08-17 — Código de Indicação

### Adicionado

- `CodigoIndicacao` em `Usuario`, destinado exclusivamente a usuários comuns, com formato oficial de oito caracteres alfanuméricos em maiúsculo e sem alteração, regeneração ou expiração.
- Geração criptograficamente segura por `ICodigoIndicacaoGenerator`/`CodigoIndicacaoGenerator`, verificação de colisão com no máximo cinco tentativas e proteção final por `UNIQUE` no MySQL.
- Consulta de usuário por código no contrato e repositório MySQL, persistência/materialização do campo e script incremental `004_add_codigo_indicacao_usuarios.sql`.
- Cobertura para domínio, Application, gerador, DI, reidratação e integração MySQL condicional.

### Decisões

- Código de indicação não é uma entidade nem uma API pública. O valor usado em `Indicacao.CodigoIndicacaoUsado` continua sendo o retrato histórico da indicação.
- A migração mantém o campo nullable para dados existentes. Nenhum dado histórico é gerado por SQL e nenhuma alteração destrutiva foi executada.

## 2026-08-12 — Autorização por Roles e Ownership

### Adicionado

- A autorizacao administrativa agora tambem exige `sub` presente, conversivel em `Guid` e diferente de `Guid.Empty`; identidade ausente ou invalida retorna `403 Forbidden`.

- Corrigida a materializacao de GUIDs MySQL: repositories agora aceitam retorno direto `Guid` ou string GUID valida, rejeitando `DBNull` obrigatorio, `Guid.Empty` e valores invalidos sem alterar o schema.

- Ajustada a comparacao temporal do round-trip MySQL para tolerancia de um microssegundo, compativel com `DATETIME(6)`; nenhum schema ou codigo de producao foi alterado.

- Testes reais de integracao MySQL para repositories de Usuario, Indicacao e Vistoria, em database temporario com prefixo obrigatorio `indicaa2_test_`.
- Cobertura de schema do zero, reidratacao, filtros, updates, constraint UNIQUE de email, FK de Vistoria e decimal de area; configuracao externa por `INDICA2_TEST_MYSQL_CONNECTION`, sem credenciais versionadas.

- Policies centralizadas, `ICurrentUser` por request e handlers OwnerOrAdmin para Indicações e Vistorias.
- Proteção Bearer para controllers de negócio, acesso administrativo e ownership de recursos por `sub`.
- Requisitos Bearer por operação protegida no OpenAPI, sem proteger o login.
- Cobertura de handlers, controllers e pipeline HTTP para 401, 403, roles, ownership e mitigação de IDOR.

### Decisões

- Autorização é responsabilidade exclusiva da API; Domain e Application continuam independentes de JWT e ASP.NET Core Authorization.
- `401` representa ausência ou invalidez de autenticação; `403`, autenticação válida sem permissão.

## 2026-08-11 — Autenticação JWT

### Adicionado

- BCrypt, busca de usuário por e-mail, `AuthService`, JWT Bearer e endpoint de login.
- Transformer OpenAPI para documentar o esquema HTTP Bearer/JWT, inicialmente sem requisito global de autorização.
- Claims de identidade e role, atualização de `UltimoLogin` e tratamento HTTP 401/403 para falhas de autenticação.

### Decisões

- Configuração JWT é externa; nenhuma senha, token ou chave real foi versionada.
- `EmailConfirmado` ainda não bloqueia login. Refresh token permanece pendente; autorização por recurso foi implementada posteriormente.

## 2026-08-11 — API de Vistorias e integração real com Indicações

### Adicionado

- `VistoriasController`, endpoints HTTP do módulo e registro scoped de `IVistoriaService` na API.
- Mapeamento HTTP 404 para `VistoriaNaoEncontradaException` e `UsuarioNaoEncontradoException`.
- Validação de existência da vistoria, correspondência entre usuário indicado e contratante e conclusão real da vistoria no `IndicacaoService`.
- Exemplos em `API.http` e testes de Application/API para integração, DI, exceções e `CancellationToken`.

### Decisões

- Não há sincronização automática entre os módulos: concluir uma vistoria não altera uma indicação até a execução explícita de `MarcarVistoriaConcluidaAsync`.
- Nenhum schema, trigger, cascade ou regra financeira foi acrescentado.

### Pendente

- Testes reais com MySQL, JWT/autenticação, preços, cashback, Pix, pagamentos, código de indicação e estratégia de exclusão/inativação de usuários.

## 2026-08-11 — Persistência MySQL de Vistorias

### Adicionado

- `Vistoria.Reidratar`, `VistoriaMySqlRepository`, script `003_create_vistorias.sql` e registro scoped de `IVistoriaRepository`.
- Tabela `vistorias` com FK restritiva para `usuarios(id)`, enums como `INT`, área como `DECIMAL(10,2)` e índice em `usuario_id`.
- Testes unitários de reidratação de Vistoria e de resolução de DI, sem depender de MySQL externo.

### Decisões

- `AtualizarAsync` persiste somente `status` e `updated_at`; não foi introduzido `DELETE` nem `ON DELETE CASCADE`.
- `DataAgendada` mantém seu significado de data/hora de negócio e é materializada sem conversão arbitrária de timezone.

### Pendente

- Testes de integração reais contra MySQL, API de Vistorias, integração com `IndicacaoService` e validação real de `VistoriaId`.
- JWT, autenticação, preços, cashback, Pix e pagamentos.

## 2026-08-11 — Módulo inicial de Vistorias

### Adicionado

- Domain e Application de Vistorias: entidade, enums, contrato de repository, DTOs, mapper manual, service e exceção específica.
- Ciclo de Vistoria: Agendada → Realizada → Concluida, com cancelamento permitido apenas enquanto Agendada.
- Testes unitários de invariantes, transições, idempotência, casos de uso e `CancellationToken` onde o contrato suporta.

### Decisões

- `UsuarioId` identifica o usuário contratante da vistoria.
- `TipoPlanta` permanece textual e nenhum cálculo financeiro foi adicionado.
- Não foram implementados MySQL, DI, API de Vistorias nem a integração com `IndicacaoService`.

## 2026-08-11 — API HTTP de Indicações

### Adicionado

- Composition root com Controllers, `AddInfrastructure`, `IIndicacaoService`, ProblemDetails e handler global de exceções.
- `IndicacoesController` e exemplos de todos os endpoints em `API.http`.
- Projeto `API.Tests` com testes de controller, handler de exceções e resolução de Dependency Injection sem MySQL externo.

### Alterado

- `Microsoft.AspNetCore.OpenApi` foi alinhado de 10.0.10 para 9.0.10, mantendo a API em `net9.0`.
- O endpoint e os tipos auxiliares de `weatherforecast` foram removidos.

### Configuração

- `ConnectionStrings:DefaultConnection` deve ser fornecida por `ConnectionStrings__DefaultConnection` ou user-secrets; nenhuma credencial foi versionada.

### Pendente

- JWT, autenticação, Vistorias, validação real de `VistoriaId`, integração real com MySQL, código de indicação, estratégia de exclusão/inativação, cashback, Pix e pagamentos.

## 2026-08-11 — Persistência MySQL de Usuários

### Adicionado

- `UsuarioMySqlRepository`, script `002_create_usuarios.sql`, reidratação controlada de `Usuario` e registro scoped de `IUsuarioRepository`.
- Testes sem MySQL externo para reidratação de usuários e registro de Dependency Injection.

### Alterado

- `IUsuarioRepository` foi consolidado, removendo o overload redundante de `ExistePorEmailAsync` e `ObterPorCodigoIndicacaoAsync`, cujo módulo permanece pendente.
- `RemoverAsync` foi removido dos contratos de repository e service até a definição formal da estratégia de exclusão ou inativação.

### Observações

- Nenhuma coluna de código de indicação e nenhum `DELETE` físico foram introduzidos.

## 2026-08-10 — Correção de invariantes de reidratação de indicações

### Corrigido

- `Indicacao.Reidratar` passou a rejeitar valores `Guid.Empty` em `UsuarioIndicadoId` e `VistoriaId`.
- O status `Pendente` passou a ser incompatível com uma vistoria vinculada durante a reidratação.
- Adicionada cobertura automatizada para as novas invariantes e para a reidratação válida de uma indicação cancelada com vistoria vinculada.

## 2026-08-10 — Persistência MySQL de Indicações

### Adicionado

- MySqlConnector e Infrastructure para persistência de `Indicacao`.
- `MySqlConnectionFactory`, `IndicacaoMySqlRepository` e registros iniciais de Dependency Injection.
- Script idempotente `database/001_create_indicacoes.sql`.
- Reidratação interna e validada da entidade persistida.
- Projeto `Infrastructure.Tests` com testes unitários sem banco externo.

### Removido

- `src/Infrastructure/Class1.cs`, placeholder sem uso do template.

### Pendente

- Connection string real e composition root na API.
- Testes de integração MySQL, Vistorias, validação real de `VistoriaId`, cashback, Pix, pagamentos e código de indicação.

## 2026-08-07 — Testes automatizados do módulo de Indicações

### Adicionado

- Solução `IndicA2.slnx` com projetos Domain, Application, Infrastructure e testes.
- Projetos `Domain.Tests` e `Application.Tests` com xUnit e Moq.
- Cobertura dos comportamentos da entidade `Indicacao` e dos casos de uso de `IndicacaoService`.

### Corrigido

- Removida a validação duplicada de autoindicação no service, preservando a invariável no Domain.
- Padronizadas as assinaturas de `IIndicacaoRepository`.
- Corrigidos imports de exceções e uma chamada compatível com o contrato de `IUsuarioRepository`.

### Pendente

- Repository concreto, MySQL, Dependency Injection, API/controllers e validação real de `VistoriaId`.
- Vistorias, cashback, Pix, pagamentos e código de indicação.
- `docs/Readme.md` e `docs/Arquitetura.md` foram preservados sem alteração nesta tarefa devido à inconsistência entre a extensão `.md` e o formato Word binário interno.

## 2026-08-05 — Implementação controlada do módulo de Indicações

### Adicionado

- `IIndicacaoRepository` como contrato de persistência.
- DTOs, interface, mapper manual e service da camada Application.
- `IndicacaoNaoEncontradaException`.
- Casos de uso de criação, consultas, vínculo de usuário indicado, vínculo de vistoria, conclusão e cancelamento.

### Alterado

- `Indicacao` passou a bloquear autoindicação também na camada Domain.

### Pendente

- Implementação concreta do repository, banco MySQL, API e Dependency Injection.
- Integração com Vistorias para validar a existência da vistoria.
- Cashback, Pix, pagamentos e código de indicação.

## 2026-08-05 — Refatoração do início do módulo de Indicações

### Alterado

- Renomeado `ClienteIndicadoraId` para `UsuarioIndicadorId` em `Indicacao`.
- Adicionado vínculo opcional `UsuarioIndicadoId` e seu método de domínio.
- Simplificado `StatusIndicacao` para o ciclo de indicação.
- Removido o estado financeiro de cashback da entidade `Indicacao`.

### Observações

- `Cliente.cs` e `TipoChavePix.cs` não estavam presentes no projeto antes desta refatoração.
- O controle financeiro de cashback e os dados Pix permanecem pendentes de módulo próprio.
