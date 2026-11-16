import { randomBytes } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { test, expect, type BrowserContext, type Page } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

const origem = process.env.INDICAA2_UI_ORIGIN ?? 'https://localhost:18443'
const cliente = 'Cliente fictícia do aceite'
const tipo = 'Planta fictícia do aceite'

async function admin() {
  const file = process.env.INDICAA2_UI_ADMIN_FILE
  if (!file) throw new Error('Execute UiSmoke com um perfil descartável.')
  const { Email, Password } = JSON.parse(await readFile(file, 'utf8')) as Record<string, string>
  if (!Email?.endsWith('@example.invalid') || !Password) throw new Error('Fixture inválida.')
  return { email: Email, senha: Password }
}

async function login(page: Page, email: string, senha: string) {
  await page.getByLabel('E-mail', { exact: true }).fill(email)
  await page.getByLabel('Senha', { exact: true }).fill(senha)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
}

async function axe(page: Page, tela: string) {
  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(violations.filter(v => v.impact === 'serious' || v.impact === 'critical').map(v => v.id), tela).toEqual([])
}

const requestsExternas = new WeakMap<BrowserContext, string[]>()
test.beforeEach(async ({ context }) => {
  const externas: string[] = []
  requestsExternas.set(context, externas)
  await context.route('**/*', route => {
    if (new URL(route.request().url()).origin === origem) return route.continue()
    externas.push('externa')
    return route.abort()
  })

})

test.afterEach(({ context }) => expect(requestsExternas.get(context)).toHaveLength(0))

test('jornada operacional real e acessibilidade', async ({ page }, info) => {
  test.skip(process.env.INDICAA2_UI_PHASE === 'restart')
  const { email, senha } = await admin()
  const clienteEmail = 'cliente-aceite@example.invalid'
  const clienteSenha = randomBytes(24).toString('base64url')
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Acesse sua conta' })).toBeVisible()
  await axe(page, 'Login')
  await login(page, email, randomBytes(24).toString('base64url'))
  await expect(page.getByText('Não foi possível entrar. Confira suas credenciais e tente novamente.')).toBeVisible()
  await login(page, email, senha)
  await expect(page.getByRole('heading', { name: 'Primeiros passos' })).toBeVisible()
  const passos = page.getByRole('region', { name: 'Primeiros passos' })
  await expect(passos.getByText('Pendente', { exact: true })).toHaveCount(6)
  await axe(page, 'Visão geral')
  const documentos = await page.evaluate(() => performance.timeOrigin)

  await passos.getByRole('link', { name: 'Cadastrar ou localizar uma cliente' }).click()
  await page.getByRole('button', { name: 'Novo usuário' }).click()
  const novo = page.getByRole('dialog')
  await novo.getByLabel('Nome', { exact: true }).fill(cliente)
  await novo.getByLabel('E-mail', { exact: true }).fill(clienteEmail)
  await novo.getByLabel('Senha inicial', { exact: true }).fill(clienteSenha)
  await novo.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: clienteEmail })).toBeVisible()
  const codigo = await page.getByRole('row').filter({ hasText: clienteEmail }).getByRole('cell').nth(3).innerText()
  expect(codigo).toMatch(/^[A-Z0-9]{8}$/)

  await page.getByRole('link', { name: 'Tipos de planta', exact: true }).click()
  await page.getByRole('button', { name: 'Novo tipo' }).click()
  await page.getByLabel('Nome do tipo').fill(tipo)
  await page.getByRole('button', { name: 'Salvar tipo' }).click()
  await expect(page.getByRole('row').filter({ hasText: tipo })).toBeVisible()
  await page.getByRole('link', { name: 'Tabela de preços', exact: true }).click()
  await page.getByRole('button', { name: 'Histórico e preços de ' + tipo }).click()
  await page.getByLabel('Preço por m²').fill('10')
  await page.getByRole('button', { name: 'Publicar preço' }).click()
  await expect(page.getByRole('dialog').getByText(/versão 1/)).toBeVisible()
  await page.keyboard.press('Escape')

  await page.getByRole('link', { name: 'Vistorias', exact: true }).click()
  await page.getByRole('button', { name: 'Nova vistoria' }).click()
  const vistoria = page.getByRole('dialog')
  await vistoria.getByLabel('Cliente', { exact: true }).selectOption({ label: cliente })
  await vistoria.getByLabel('Tipo de planta', { exact: true }).selectOption({ label: tipo })
  await vistoria.getByLabel('Área (m²)').fill('25')
  await vistoria.getByLabel('Data agendada').fill('2099-01-01T10:00')
  await expect(vistoria.getByText(/250,00/).first()).toBeVisible()
  await vistoria.getByRole('button', { name: 'Salvar', exact: true }).click()
  const registro = page.getByRole('row').filter({ hasText: cliente })
  await expect(registro).toContainText(/250,00/)
  await expect(registro).toContainText('Agendada')

  await page.getByRole('link', { name: 'Relatórios', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Relatórios' })).toBeVisible()
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Exportar CSV' }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toBe('relatorio-indicaa2.csv')
  const stream = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream!) chunks.push(Buffer.from(chunk))
  expect(Buffer.concat(chunks).toString('utf8').trim()).toBe('id,dataUtc,status,valor')
  await download.delete()
  await axe(page, 'Relatórios')
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(documentos)

  const publica = await page.context().newPage()
  await publica.setViewportSize({ width: 360, height: 780 })
  await publica.goto('/indicar/' + codigo)
  await expect(publica.getByLabel('Nome da pessoa indicada')).toBeVisible()
  await axe(publica, 'Indicação pública móvel')
  await publica.close()

  for (const largura of [360, 768, 1280]) {
    await page.setViewportSize({ width: largura, height: 820 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(largura)
  }
  await page.setViewportSize({ width: 360, height: 780 })
  const menu = page.getByRole('button', { name: 'Alternar navegação' })
  await menu.focus()
  await page.keyboard.press('Enter')
  await page.getByRole('link', { name: 'Visão geral', exact: true }).click()
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  await expect(page.getByRole('main')).toBeFocused()
  await page.getByRole('button', { name: 'Sair', exact: true }).click()
  await page.reload()
  await expect(page).toHaveURL(/\/login$/)
  await page.goto('/relatorios')
  await expect(page).toHaveURL(/\/login$/)
  await login(page, clienteEmail, clienteSenha)
  await expect(page).toHaveURL(/\/minha-conta$/)
  await axe(page, 'Minha conta móvel')
  await page.goto('/relatorios')
  await expect(page).toHaveURL(/\/minha-conta$/)
  info.annotations.push({ type: 'axe', description: 'Nenhuma violação serious ou critical nas páginas verificadas.' })
})

test('dados fictícios após reiniciar a API', async ({ page }) => {
  test.skip(process.env.INDICAA2_UI_PHASE !== 'restart')
  const { email, senha } = await admin()
  await page.goto('/login')
  await login(page, email, senha)
  await page.getByRole('link', { name: 'Vistorias', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: cliente })).toContainText(/250,00/)
})

test('as páginas cabem no viewport sem rolagem horizontal da página', async ({ page }) => {
  test.skip(process.env.INDICAA2_UI_PHASE === 'restart')
  test.setTimeout(95_000)
  const { email, senha } = await admin()
  await page.goto('/login')
  await login(page, email, senha)
  await expect(page.getByRole('heading', { name: 'Resumo operacional' })).toBeVisible()
  const telas = ['/', '/usuarios', '/tipos-planta', '/precos-vistoria', '/vistorias', '/indicacoes',
    '/pagamentos-vistoria', '/cobrancas-pix-vistoria', '/cashbacks', '/pagamentos-pix', '/alertas',
    '/relatorios', '/minha-conta', '/esqueci-senha', '/confirmar-email', '/redefinir-senha', '/pagar']
  for (const width of [360, 768, 1280]) {
    await page.setViewportSize({ width, height: 820 })
    for (const tela of telas) {
      await page.goto(tela, { waitUntil: 'domcontentloaded' })
      await expect(page.getByRole('main').getByRole('heading').first()).toBeVisible()
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth), {
        message: "Viewport de "+width+" px na rota "+tela,
      }).toBeLessThanOrEqual(width)
    }
  }
  const fragmento = randomBytes(32).toString('hex')
  await page.goto('/pagar#' + fragmento)
  await expect(page).toHaveURL(/\/pagar$/)
  await expect(page.getByRole('main')).not.toContainText(fragmento)
})
