# IndicA2

Backend .NET 9 / MySQL 8 e painel administrativo React + TypeScript em `src/Web`.

- [Execução do painel](src/Web/README.md)
- [Implementações e contratos vigentes](docs/Implementacoes.md)
- [Histórico de mudanças](docs/Changelog.md)
- [Exemplos HTTP seguros](src/API/API.http)

## Catálogo e precificação

O administrador cadastra Tipos de planta e publica versões na Tabela de preços. Não existem seeds de tipos ou tarifas: alguém da A2 deve fornecer os valores comerciais. Novas vistorias exigem catálogo/preço ativos e têm cálculo decimal no backend, com snapshot histórico atômico. Registros legados não são convertidos ou recalculados. A migration 014 deve ser aplicada pelo processo de administração do schema antes de usar o módulo.

A especificação técnica citada nos documentos históricos é um anexo externo fornecido pelo proprietário, não um arquivo em `sources/`. Decisões efetivamente implementadas estão em `docs/Implementacoes.md`. Alguns `.md` históricos têm formato Word e não devem ser convertidos automaticamente.

## Validação

```powershell
dotnet build IndicaA2.slnx
dotnet test IndicaA2.slnx --no-build --no-restore --filter "Category!=MySqlIntegration&FullyQualifiedName!~EfiPixSandboxIntegrationTests&FullyQualifiedName!~EfiPixTlsDiagnosticTests"
pwsh -NoProfile -File ./scripts/Invoke-MySqlIntegrationTests.ps1 -RequireMySql
```

MySQL exige conexão privada de testes em `INDICA2_TEST_MYSQL_CONNECTION`, sem Database, autorizada somente para bancos descartáveis `indicaa2_test_`. Nunca use banco de produção nem registre credenciais. O script falha fechado sem configuração/preflight. Testes externos Efí não pertencem à suíte rápida. Frontend: `npm ci`, `npm run lint`, `npm test -- --run`, `npm run build`, com TZ America/Sao_Paulo.

Worker Pix continua desabilitado por padrão. Esta entrega não executa Efí/OAuth/Pix real, não implanta produção nem implementa recebimento, webhook ou notificações.
