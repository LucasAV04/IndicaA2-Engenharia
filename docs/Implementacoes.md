# Implementações

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

- Repository concreto, MySQL, API, controller e registro de Dependency Injection.
- Validação da existência real da vistoria na futura integração com o módulo de Vistorias.
- Cashback, Pix, pagamentos e o módulo próprio de código de indicação.
