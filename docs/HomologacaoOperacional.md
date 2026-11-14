# Homologação operacional local

Use Docker Desktop com containers Linux, Compose v2 ou superior e PowerShell 7.4 ou superior. Reserve a porta 8443 do loopback. Os perfis persistente e descartável usam a mesma porta e devem ser executados em momentos distintos.

O Kestrel serve o React compilado e a API em https://localhost:8443. O build usa Node 24 e o SDK .NET 9. A imagem final contém o runtime ASP.NET Core 9, o aplicativo, os arquivos SQL históricos e o curl para a sonda. O MySQL 8.0 fica em rede interna, sem porta no host. Uma segunda rede bridge permite ao Docker Desktop publicar somente o HTTPS do Kestrel no loopback. O runtime da API usa usuário sem privilégios, raiz somente leitura e /tmp em memória. O limite padrão de corpo do Kestrel é preservado. Os containers usam UTC e o frontend conserva sua apresentação de horários.

## Preparação e subida

Execute a partir da raiz ou passe o caminho completo do script.

```powershell
pwsh -NoProfile -File ./scripts/Invoke-Homologation.ps1 -Action Init
pwsh -NoProfile -File ./scripts/Invoke-Homologation.ps1 -Action Build
pwsh -NoProfile -File ./scripts/Invoke-Homologation.ps1 -Action Preflight
pwsh -NoProfile -File ./scripts/Invoke-Homologation.ps1 -Action Migrate
pwsh -NoProfile -File ./scripts/Invoke-Homologation.ps1 -Action Start
pwsh -NoProfile -File ./scripts/Invoke-Homologation.ps1 -Action Bootstrap
```

`Init` gera chaves independentes, senhas aleatórias e certificado local de 30 dias em `.homologacao/`. Restrinja o acesso ao diretório à conta de operação. As montagens são somente leitura e as variáveis dos containers referenciam arquivos, sem valores privados. O script aponta Compose para um arquivo de ambiente vazio específico, sem carregar o .env genérico. No Windows, use `Init -TrustCertificate` para instalar explicitamente a confiança no repositório de certificados do usuário atual. Revogue manualmente essa confiança ao descartar o certificado. O smoke limita a exceção de confiança ao seu cliente HTTPS de localhost.

`Migrate` aguarda MySQL saudável, executa o processo administrativo e registra ordem, nome, SHA-256 do SQL com quebras LF e conclusão UTC. `Start` também passa por essa etapa administrativa antes de subir a API. Um journal igual é no-op. DDL no MySQL tem commits implícitos. Uma linha sem data indica execução interrompida e bloqueia novas aplicações. Preserve o volume e restaure de backup conhecido após investigar. Um banco legado sem journal exige avaliação manual.

`Bootstrap` solicita nome e e-mail fictícios e lê a senha com entrada mascarada. O comando separado cria somente o primeiro administrador usando BCrypt e um lock do banco. As mesmas credenciais são idempotentes. O arquivo temporário de credenciais é removido ao fim da operação. Não existem seeds comerciais.

## Diagnóstico, recuperação e persistência

- `GET /health/live` retorna 200 enquanto o processo responde.
- `GET /health/ready` retorna 200 após conexão com MySQL e conferência dos 16 registros completos do journal, ou 503 com resposta genérica. As duas respostas usam no-store.
- `-Action Status` mostra apenas serviços e estados. Para inspeção local use `docker compose --project-name indicaa2-homologacao --env-file .homologacao/compose.env logs --tail 100`. Trate a saída como privada.
- `-Action Stop` preserva containers e volume. `-Action Down` remove os containers e redes do projeto e preserva o volume. `-Action Start` recupera o ambiente e verifica o journal antes da API.
- `-Action Backup` exporta um snapshot do schema e dos dados para o diretório privado. O dump não carrega credenciais de conexão, mas inclui dados de aplicação e hashes de autenticação. Proteja e retenha de acordo com a finalidade do teste. Execute sem migração ou alterações de schema concorrentes.
- `-Action RestoreTest` gera um dump, restaura em um banco com nome aleatório validado, confere o journal e a contagem de usuários e remove o banco temporário no encerramento. O banco principal permanece disponível.
- Para trocar o certificado, pare o ambiente e substitua o PFX, o CRT e a senha associada em conjunto. Trocar a chave JWT encerra sessões. Preserve as chaves AES correspondentes a dados criptografados e backups. A troca de senhas MySQL de um volume existente requer alterar as contas no MySQL antes de atualizar os arquivos de configuração.

## Smoke e limpeza

```powershell
pwsh -NoProfile -File ./scripts/Invoke-Homologation.ps1 -Action Smoke -Disposable
```

O smoke usa `indicaa2-homologacao-smoke`, segredos temporários e administrador fictício. Ao terminar, remove os containers, redes, volume e arquivos privados criados para o teste. Após interrupção, execute `-Action Cleanup -Disposable` para terminar o descarte. O comando exige o marcador local e verifica nomes e labels. As imagens podem permanecer para inspeção. Nenhuma ação usa prune.

As flags de processamento Pix, recebimento e preparação de Cashback permanecem desligadas, e o preflight rejeita qualquer configuração do adapter Efí. O ambiente não deve receber material de produção, chamadas financeiras reais nem valores comerciais ilustrativos.


## Auditoria npm do PR #39

O `npm ci` local original reportou 15 alertas (12 moderados e 3 altos). O CI original, run 43530360123, reportou 14 (11 moderados e 3 altos). A nova consulta local ao mesmo lockfile retornou 14 no `npm audit` e 1 alto no `npm audit --omit=dev`. Os totais contam pacotes sinalizados, incluindo efeitos transitivos, e refletem os advisories disponíveis na consulta.

| Escopo | Pacote e caminho | Correção |
| --- | --- | --- |
| Produção, alto | Projeto → `zod@4.6.5`, também peer de `@hookform/resolvers`. O Zod e símbolos de validação de coleções estão no JS entregue ao navegador. Os schemas de formulário usam objetos e campos escalares, sem schemas de array, set ou map expostos às entradas. A rotina afetada não é alcançada pelos fluxos atuais. | `4.6.6` é a primeira versão corrigida e cabe na faixa `^4.1.0`. [Advisory do Zod](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5) |
| Desenvolvimento, alto | Projeto → `vite@7.3.6`. Os avisos são do servidor de desenvolvimento e da integração de editor. | A versão 7.3.7 está disponível e cabe na faixa declarada. |
| Build e testes, alto | `vite → postcss → source-map-js@1.2.1` e `jsdom → cssstyle ou @asamuzakjp/dom-selector → css-tree → source-map-js`. | `1.2.2` cabe na faixa `^1.2.1` dos consumidores. |
| Testes, moderado | Projeto → `vitest@5.0.1 → @vitest/mocker@5.0.1`. O audit sinaliza os dois pacotes. | `vitest@5.0.3` cabe na faixa declarada e traz o mocker corrigido. |
| Lint, moderado | `eslint → minimatch` diretamente e via `@eslint/config-array`. `typescript-eslint → @typescript-eslint/typescript-estree → minimatch`, com efeitos em `@typescript-eslint/parser`, `utils`, `type-utils` e `eslint-plugin`. | O `minimatch` corrigido começa em `11.0.1`. As faixas transitivas atuais são 10.x. A troca de `typescript-eslint` sugerida pelo audit foi sinalizada como potencialmente incompatível. |

O build entrega somente os arquivos estáticos do Vite. Os caminhos de lint, build e teste da tabela ficam fora do runtime publicado. Esta correção preserva o manifesto e o lockfile.
