# Changelog

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
