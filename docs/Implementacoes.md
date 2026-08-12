# Implementações

## Autorização por Roles e Ownership

**Data:** 2026-08-12

### Implementado

- A autorizacao exige `sub` presente, conversivel em `Guid` e diferente de `Guid.Empty` para qualquer acesso, inclusive administrativo. A role `Administrador` sem identidade valida retorna `403 Forbidden`.

- Suite de integracao MySQL no projeto `Infrastructure.Tests`, cobrindo `UsuarioMySqlRepository`, `IndicacaoMySqlRepository` e `VistoriaMySqlRepository` contra banco real.
- A fixture cria por execucao o database `indicaa2_test_<guid>`, aplica os scripts reais na ordem `002_create_usuarios.sql`, `003_create_vistorias.sql` e `001_create_indicacoes.sql`, limpa dados entre testes e remove o database ao final.
- A configuracao obrigatoria e `INDICA2_TEST_MYSQL_CONNECTION`: uma conexao administrativa sem `Database`. A fixture valida o prefixo seguro antes de qualquer limpeza ou remocao, portanto nao usa automaticamente banco de desenvolvimento ou producao.
- A cobertura inclui insert, select, update, filtros, reidratacao, `email` UNIQUE, FK de `vistorias.usuario_id`, `DECIMAL(10,2)` de `AreaM2`, timestamps UTC e `DataAgendada` preservada como valor de negocio. O schema atual de `indicacoes` nao declara FKs e os testes nao assumem integridade inexistente.
- Para executar: `dotnet test tests/Infrastructure.Tests/Infrastructure.Tests.csproj --filter "Category=Integration"`. Para testes sem MySQL: `dotnet test IndicaA2.slnx --filter "Category!=Integration"`. Sem a variavel, os testes de integracao sao ignorados explicitamente com instrucao de configuracao, sem serem contabilizados como aprovados.

- `IndicacoesController` e `VistoriasController` exigem autenticação Bearer; `POST /api/auth/login` permanece público.
- `ICurrentUser` interpreta exclusivamente `sub` como `Guid` do usuário atual e `role` como papel. Claims ausentes ou inválidas não concedem acesso.
- A policy centralizada `Administrador` protege as consultas e comandos operacionais globais.
- Handlers sem I/O aplicam ownership por `Indicacao.UsuarioIndicadorId` e `Vistoria.UsuarioId`, mitigando IDOR.
- Usuário comum cria indicação apenas para si, consulta/cancela suas indicações e consulta suas vistorias. Administrador possui o acesso operacional global definido.
- O OpenAPI aplica Bearer somente às operações protegidas; login não recebe requisito de segurança.
- Testes unitários de handlers, testes de controllers e smoke tests HTTP cobrem 401, 403, roles, ownership e OpenAPI.

### Semântica HTTP

- `401 Unauthorized`: token ausente, inválido ou expirado.
- `403 Forbidden`: usuário autenticado sem permissão para a operação ou recurso.

### Pendente

- Testes de integração reais contra MySQL, confirmação de e-mail, refresh token, código de indicação, estratégia de exclusão/inativação, preços, cashback, Pix e pagamentos.

## Autenticação JWT

**Data:** 2026-08-11

### Implementado

- `BCryptPasswordHasher` como implementação concreta de `IPasswordHasher`.
- Busca normalizada de usuário por e-mail, `AuthService` e endpoint público `POST /api/auth/login`.
- JWT Bearer configurado externamente por `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Key` e `Jwt:ExpirationMinutes`, com HMAC-SHA256 e claims `sub`, `email`, `name` e `role`.
- OpenAPI nativo registra o esquema `Bearer` (`http`, `bearer`, `JWT`) e os requisitos por operação protegida.
- Login atualiza `UltimoLogin` apenas após credenciais válidas e persiste o usuário.
- Credenciais inválidas retornam 401 sem revelar existência do e-mail; usuário inativo ou bloqueado retorna 403.

### Decisões registradas

- `EmailConfirmado` não bloqueia login enquanto o fluxo de confirmação de e-mail não existir.
- Chaves JWT, senhas e tokens não são versionados; devem ser fornecidos por variáveis de ambiente ou user-secrets.
- Refresh token permanece fora deste escopo.

## API HTTP de Vistorias e integração com Indicações

**Data:** 2026-08-11

### Implementado

- `VistoriasController` com criação, consultas, consulta por usuário, realização, conclusão e cancelamento, sempre delegando à `IVistoriaService`.
- Registro scoped de `IVistoriaService` e `VistoriaService` na composition root; repositories continuam registrados exclusivamente por `AddInfrastructure`.
- `IndicacaoService` passou a consultar `IVistoriaRepository` para validar a existência de `VistoriaId`, a correspondência entre `Vistoria.UsuarioId` e `Indicacao.UsuarioIndicadoId` e o status `Concluida` antes de concluir uma indicação.
- `GlobalExceptionHandler` mapeia `VistoriaNaoEncontradaException` e `UsuarioNaoEncontradoException` para 404, preservando 422 para `DomainException` e 400 para `ArgumentException`.
- Exemplos HTTP e testes diretos de controller, DI, exceções e integração de Application atualizados.

### Decisões registradas

- Não há sincronização automática entre `VistoriaService.ConcluirAsync` e `IndicacaoService`; a indicação só muda mediante seu caso de uso explícito.
- A validação do vínculo é feita na Application; não foram criadas FKs, triggers, cascades ou alterações de schema entre `indicacoes` e `vistorias`.

### Pendente

- Testes reais de integração contra MySQL.
- Preços, tabela comercial, cashback, Pix, pagamentos, código de indicação e estratégia de exclusão/inativação de usuários.

## Infrastructure — Persistência MySQL de Vistorias

**Data:** 2026-08-11

### Implementado

- Reidratação interna e controlada de `Vistoria`, preservando o estado persistido sem reproduzir transições de domínio.
- Script incremental `database/003_create_vistorias.sql` para MySQL 8, limitado aos campos atuais da entidade e com FK restritiva para `usuarios(id)`.
- `VistoriaMySqlRepository` com MySqlConnector, SQL parametrizado, colunas explícitas e persistência dos enums `PacoteVistoria` e `StatusVistoria` como `INT`.
- Registro scoped de `IVistoriaRepository` em `AddInfrastructure`.
- `AtualizarAsync` restrito a `status` e `updated_at`; não há operação de `DELETE`.
- Testes sem MySQL externo para reidratação e resolução do repository pelo container de DI.

### Decisões registradas

- `area_m2` é persistida como `DECIMAL(10,2)`.
- `created_at` e `updated_at` são materializados como UTC. `DataAgendada` é data/hora de negócio e é preservada como lida, sem conversão arbitrária de timezone.
- A reidratação rejeita IDs, enums, datas e valores estruturais inválidos, mas permite reconstituir diretamente os status válidos `Realizada`, `Concluida` e `Cancelada`.

### Pendente

- Testes reais de integração contra MySQL.
- JWT, autenticação, preços, cashback, Pix e pagamentos.

## Módulo de Vistorias — Domain e Application

**Data:** 2026-08-11

### Implementado

- Entidade `Vistoria`, pertencente obrigatoriamente ao `Usuario` que contratou o serviço por meio de `UsuarioId`.
- Dados iniciais: `TipoPlanta` textual, `AreaM2`, `PacoteVistoria` e `DataAgendada`; nenhum valor ou regra financeira é calculado pelo módulo.
- `StatusVistoria` com ciclo mínimo: `Agendada`, `Realizada`, `Concluida` e `Cancelada`.
- Transições: Agendada → Realizada, Agendada → Cancelada e Realizada → Concluida. Os estados finais não aceitam novas transições incompatíveis.
- Idempotência para repetições de marcar realizada, concluir e cancelar quando a vistoria já está no respectivo estado.
- `IVistoriaRepository`, DTOs específicos, mapper manual, `IVistoriaService`, `VistoriaService` e `VistoriaNaoEncontradaException`.
- Validação da existência do usuário contratante antes da criação e testes de Domain/Application.

### Decisões registradas

- `TipoPlanta` permanece texto até que a tabela comercial e suas categorias sejam formalizadas.
- A futura integração com Indicações deverá validar a correspondência entre `Vistoria.UsuarioId` e `Indicacao.UsuarioIndicadoId`, mas não foi implementada nesta etapa.
- `IUsuarioRepository.ExistePorIdAsync` não recebe `CancellationToken`; a limitação foi preservada para não ampliar o contrato nesta tarefa.

### Pendente

- Preços, tabela comercial, pagamentos, cashback, Pix, JWT e autenticação.

## API HTTP — Módulo de Indicações

**Data:** 2026-08-11

### Implementado

- Composition root da API com Controllers, OpenAPI nativo, ProblemDetails, handler global de exceções, `AddInfrastructure` e `IIndicacaoService` scoped.
- `IndicacoesController` com criação, consultas, vínculos, conclusão de vistoria e cancelamento, delegando todos os casos de uso à Application.
- Respostas HTTP semânticas: 201 para criação, 200 para consultas, 204 para comandos concluídos, 400 para entradas inválidas, 404 para indicação não encontrada e 422 para violações de domínio.
- OpenAPI alinhado ao `net9.0` com `Microsoft.AspNetCore.OpenApi` 9.0.10; o endpoint de template `weatherforecast` foi removido.
- Projeto `API.Tests` com cobertura dos endpoints, CancellationToken, ProblemDetails e resolução de DI sem MySQL externo.
- Exemplos fictícios de chamadas HTTP em `src/API/API.http`.

### Configuração

- A API espera `ConnectionStrings:DefaultConnection` em variável de ambiente (`ConnectionStrings__DefaultConnection`) ou user-secrets.
- A connection string não é versionada e a inicialização não abre conexão com MySQL nem executa scripts de banco.

### Pendente

- Autenticação e autorização foram implementadas em etapas posteriores; este registro permanece como histórico.
- Testes de integração reais contra MySQL.
- Código de indicação, estratégia de exclusão/inativação de usuários, cashback, Pix e pagamentos.

## Infrastructure — Persistência MySQL de Usuários

**Data:** 2026-08-11

### Implementado

- `UsuarioMySqlRepository` com MySqlConnector, consultas parametrizadas e propagação de `CancellationToken` nos métodos cujo contrato o recebe.
- Script incremental `database/002_create_usuarios.sql`, limitado aos campos atuais da entidade `Usuario`, com unicidade de e-mail.
- Reidratação interna e validada de `Usuario`, preservando estado, enums, datas, hash de senha e informações de autenticação sem executar métodos de negócio.
- Registro scoped de `IUsuarioRepository` em `AddInfrastructure`.
- Testes unitários para reidratação e registro de Dependency Injection, sem conexão MySQL externa.
- Consolidação de `IUsuarioRepository` para as capacidades suportadas pelo domínio atual.

### Decisões registradas

- `ObterPorCodigoIndicacaoAsync` foi removido do contrato: o código de indicação pertence a módulo ainda pendente e não gerou coluna, query ou comportamento especulativo.
- `RemoverAsync` foi removido dos contratos de repository e service: a estratégia entre exclusão e inativação ainda não foi definida. Nenhum `DELETE` físico foi introduzido.

### Pendente

- API/controllers, JWT, Vistorias e validação real de `VistoriaId`.
- Testes de integração contra MySQL.
- Definição formal da estratégia de exclusão ou inativação de usuários.
- Código de indicação, cashback, Pix e pagamentos.

## Módulo de Indicações — Refatoração de Modelo

**Data:** 2026-08-05

- Consolidada a representação de pessoas cadastradas na entidade `Usuario`.
- A entidade `Cliente` não faz parte do estado atual do projeto.
- A pessoa indicada sem cadastro permanece representada em `Indicacao` por nome, telefone e código de indicação utilizado.
- `Indicacao` possui vínculo opcional futuro por `UsuarioIndicadoId`.
- `StatusIndicacao` passou a representar apenas o ciclo da indicação.
- Cashback e dados Pix permanecem pendentes de módulo próprio.

## Módulo de Indicações — Domain e Application

**Data:** 2026-08-05

### Implementado

- Contrato `IIndicacaoRepository` no Domain, sem implementação concreta.
- DTOs específicos para criação, consulta, vínculo de usuário indicado e vínculo de vistoria.
- `IIndicacaoService`, `IndicacaoMapper` manual e `IndicacaoService`.
- Caso de uso para criar, consultar, vincular usuário indicado, vincular vistoria, concluir vistoria e cancelar indicação.
- Exceção `IndicacaoNaoEncontradaException` para buscas sem resultado.
- Validação de autoindicação no service e na entidade `Indicacao`.

### Pendente

- Repository concreto, MySQL, registro de Dependency Injection, API e controller foram implementados em etapas posteriores; este registro permanece como histórico de 2026-08-05.
- Validação da existência real da vistoria na futura integração com o módulo de Vistorias.
- Cashback, Pix, pagamentos e o módulo próprio de código de indicação.

## Testes automatizados do módulo de Indicações

**Data:** 2026-08-07

### Implementado

- Projetos `Domain.Tests` e `Application.Tests` integrados à solução.
- Testes xUnit para construção, vínculos, transições de status, cancelamento e idempotência de `Indicacao`.
- Testes xUnit com Moq para os casos de uso de `IndicacaoService`, incluindo consultas, persistência, exceções e `CancellationToken`.
- Correção técnica: a invariável de autoindicação passou a ser validada somente pela entidade; o service apenas orquestra o caso de uso.

### Pendente

- Repository concreto, MySQL, Dependency Injection, API/controllers e validação real de `VistoriaId`.
- Módulos de Vistorias, cashback, Pix, pagamentos e código de indicação.
- `docs/Readme.md` e `docs/Arquitetura.md` não foram atualizados nesta tarefa, pois possuem extensão Markdown com conteúdo Word binário interno; os arquivos foram preservados sem conversão ou substituição.

## Infrastructure — Persistência MySQL de Indicações

**Data:** 2026-08-10

### Implementado

- `MySqlConnector` como acesso direto ao MySQL, sem Entity Framework Core ou Dapper.
- `MySqlConnectionFactory`, que recebe a connection string por injeção e cria conexões isoladas sob demanda.
- `IndicacaoMySqlRepository`, com consultas parametrizadas, persistência, atualização e propagação de `CancellationToken`.
- Reidratação controlada de `Indicacao`, interna ao Domain e acessível apenas à Infrastructure e aos testes de Infrastructure.
- Script `database/001_create_indicacoes.sql`, compatível com MySQL 8 e sem colunas financeiras.
- Extensão `AddInfrastructure`, que registra a factory e `IIndicacaoRepository` a partir de `ConnectionStrings:DefaultConnection`.
- Projeto `Infrastructure.Tests`, com testes sem servidor MySQL para a factory e a reidratação.

### Pendente

- A composition root/API foi implementada em etapa posterior; o valor real da connection string continua configurado externamente, sem credenciais versionadas.
- Testes de integração reais contra MySQL.
- Vistorias e validação real de `VistoriaId`.
- Cashback, Pix, pagamentos e módulo de código de indicação.
