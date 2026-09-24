# IndicA2 • Administração web

React + TypeScript + Vite, React Router, TanStack Query, React Hook Form e Zod. CSS próprio, sem biblioteca visual. Node 24 LTS recomendado (mínimo 22.12). Instale com `npm ci`.

## Execução local

1. Configure privadamente a API: `ConnectionStrings__DefaultConnection`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__Key`, `Jwt__ExpirationMinutes` e `DadosPixEncryption__Key` (Base64 de 32 bytes). Não use valores de produção.
2. Execute `dotnet run --project src/API --launch-profile http` na raiz. Porta padrão: 5209.
3. Em `src/Web`, execute `npm ci` e `npm run dev`. Abra http://localhost:5173.
4. O proxy Vite encaminha `/api` à API local. `.env.example` documenta `API_PROXY_TARGET`; arquivos `.env` reais não são versionados.
5. Use uma conta administrativa já provisionada pelo processo vigente. O painel cria somente usuários comuns; não promove roles e não provisiona senha administrativa.

O perfil HTTP local deve ser usado sem porta HTTPS configurada para que o proxy receba as respostas locais. Em produção, a implantação futura deverá servir Web/API sob a mesma origem HTTPS. Não há deployment neste PR.

## Sessão e segurança

Login em `POST /api/auth/login`. Token em memória e sessionStorage somente até logout/expiração; nenhum localStorage, refresh token ou cookie fictício. 401 limpa a sessão e o cache; 403 leva a acesso negado. A API valida a policy Administrador independentemente do frontend. sessionStorage exige prevenção contínua de XSS; não há HTML arbitrário ou renderização de mensagens externas. ProblemDetails é traduzido por status para mensagens controladas, sem reproduzir detail/payload sensível.

Dados Pix retornam somente `chaveMascarada`; o formulário de substituição sempre começa vazio. Usuário existente sem chave retorna 204; usuário inexistente, 404. A chave original nunca é recuperada ou incluída nas tabelas. O envio do novo valor ocorre somente na requisição de cadastro/substituição, sem logs.

## Rotas e fluxo

- `/login`, `/acesso-negado`: sessão e autorização.
- `/`: resumo operacional, valores em BRL, datas pt-BR.
- `/usuarios`: criar/editar cliente e gerenciar Dados Pix; código gerado pelo backend.
- `/indicacoes`: criar por código, selecionar usuário/vistoria, concluir vínculo após vistoria concluída.
- `/tipos-planta`: cadastrar, renomear e desativar tipos; sem exclusão física. Desative o preço antes do tipo.
- `/precos-vistoria`: tabela ativa, publicação de versões, histórico e simulação oficial do backend.
- `/vistorias`: cliente, catálogo ativo, área, pacote e agendamento; prévia oficial e criação com snapshot. Registros legados mantêm texto histórico. Realizar/concluir/cancelar não recalcula preço.
- `/pagamentos-vistoria`: selecionar vistoria calculada e conferir valor histórico; confirmar recebimento ou cancelar. O navegador envia somente `vistoriaId`, nunca valor.
- `/cashbacks`: gerar a partir de pagamento confirmado, aprovar/cancelar, visualizar snapshot de 20%.
- `/pagamentos-pix`: criar ordem por Cashback disponível e cancelar quando permitido.

Fluxo: usuário → indicação por código → vistoria e vínculo → realização/conclusão → pagamento confirmado → Cashback gerado/aprovado → ordem Pix. Seletores usam registros da API, sem digitação de GUID. A API continua sendo a autoridade das transições.

Não há portal cliente, cadastro público, reset de senha, promoção de role, envio manual, retry, webhook ou habilitação do worker. A ordem Pix depende do worker configurado no servidor; criar uma ordem não a paga. O worker permanece desabilitado por padrão.

## Precificação

Aplique a migration 014 pelo processo administrativo de schema antes de usar estas páginas. O sistema inicia sem tipos e sem preços: alguém da A2 precisa cadastrar o catálogo e informar tarifas comerciais. Exemplos da especificação externa não são preços reais nem seeds.

Base = preço/m² × área. Simples usa a base; Total adiciona o fixo ou a porcentagem configurada. Somente o backend calcula, em decimal, arredondando apenas o valor final para duas casas (AwayFromZero). Inputs decimais são enviados como texto decimal, sem cálculo financeiro em JavaScript. Área aceita duas casas; preço/acréscimo, quatro. Simulação não grava dados e pode mudar até a confirmação: a criação definitiva usa a versão ativa e devolve o snapshot persistido.

Novas vistorias enviam `tipoPlantaId`, nunca texto livre nem valor final. Tipos inativos não aparecem no seletor. Sem catálogo ou preço ativo, o formulário orienta e bloqueia a criação. Renomear/desativar não modifica histórico. Dashboard conta somente tipos ativos: cadastrados, com preço ativo e sem configuração; vazio retorna zero.

Recebimento Pix, webhook, notificações e produção permanecem fora desta entrega. Os testes usam dados fictícios e não fazem chamadas Efí/OAuth/Pix reais.

O pagamento deriva de `Precificacao.ValorFinal` persistido, mesmo se o preço for substituído/desativado ou o tipo renomeado depois. Vistorias legadas ficam indisponíveis no seletor e são rejeitadas pela API com 409; regularização futura está fora do escopo. Campos financeiros extras no payload retornam 400. A FK composta da migration 014 protege a identidade preço/tipo/versão do snapshot. Nenhum pagamento antigo é alterado.

## Validação

```sh
npm ci
npm run lint
npm run test -- --run
npm run build
```

Vitest/Testing Library exercitam os fluxos com mocks na fronteira fetch. Nenhum teste Web executa MySQL, Efí, OAuth ou Pix. CI separa backend rápido, frontend e MySQL 8 descartável. A listagem é integral com filtros locais neste MVP; paginação e observabilidade completa ficam para evolução. Não use o painel para produção sem a revisão e implantação específicas.
