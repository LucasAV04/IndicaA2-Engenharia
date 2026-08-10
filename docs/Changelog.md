# Changelog

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
