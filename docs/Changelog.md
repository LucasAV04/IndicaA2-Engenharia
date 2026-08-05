# Changelog

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
