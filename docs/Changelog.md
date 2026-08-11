# Changelog

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
