import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from '../App'
import { saveSession } from '../api'
import type { Sessao } from '../types'

const sessao: Sessao = {
  usuarioId: 'ficticio', email: 'conta@example.invalid', nome: 'Conta fictícia',
  tipoUsuario: 2, accessToken: 'jwt-ficticio', expiresAtUtc: '2099-01-01T00:00:00Z',
}
const resposta = (dados: unknown, status = 200) =>
  new Response(status === 204 ? null : JSON.stringify(dados), { status })

beforeEach(() => { saveSession(null); sessionStorage.clear(); window.history.replaceState(null, '', '/') })
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

function montar(path: string, auth?: Sessao) {
  if (auth) saveSession(auth)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}><App /></MemoryRouter></QueryClientProvider>)
}

it('solicita instruções pelo login sem revelar se existe conta', async () => {
  const fetcher = vi.fn(async () => resposta({ mensagem: 'fictício' }, 202))
  vi.stubGlobal('fetch', fetcher)
  montar('/login')
  await userEvent.click(screen.getByRole('link', { name: 'Esqueci minha senha' }))
  await userEvent.type(screen.getByLabelText('E-mail'), 'ausente@example.invalid')
  await userEvent.click(screen.getByRole('button', { name: 'Solicitar instruções' }))
  expect(await screen.findByText(/se o endereço puder/i)).toBeVisible()
  expect(fetcher).toHaveBeenCalledWith('/api/auth/esqueci-senha', expect.objectContaining({
    body: JSON.stringify({ email: 'ausente@example.invalid' }),
  }))
})

it('limpa o fragmento e envia o token apenas por header ao redefinir', async () => {
  const token = 'a'.repeat(64)
  window.history.replaceState(null, '', '/redefinir-senha#' + token)
  const fetcher = vi.fn(async () => resposta(null, 204))
  vi.stubGlobal('fetch', fetcher)
  montar('/redefinir-senha', sessao)
  expect(window.location.hash).toBe('')
  await userEvent.type(screen.getByLabelText('Nova senha'), 'nova-ficticia')
  await userEvent.click(screen.getByRole('button', { name: 'Redefinir senha' }))
  expect(await screen.findByText('Senha alterada. Entre novamente.')).toBeVisible()
  expect(fetcher).toHaveBeenCalledWith('/api/auth/redefinir-senha', expect.objectContaining({
    body: JSON.stringify({ senha: 'nova-ficticia' }),
    headers: expect.objectContaining({ 'X-Conta-Token': token }),
  }))
  expect(sessionStorage.length).toBe(0)
  expect(localStorage.length).toBe(0)
})

it('confirma e-mail sem propagar o fragmento na URL da API', async () => {
  const token = 'b'.repeat(64)
  window.history.replaceState(null, '', '/confirmar-email#' + token)
  const fetcher = vi.fn(async () => resposta(null, 204))
  vi.stubGlobal('fetch', fetcher)
  montar('/confirmar-email')
  expect(window.location.hash).toBe('')
  await userEvent.click(screen.getByRole('button', { name: 'Confirmar e-mail' }))
  expect(await screen.findByText('E-mail confirmado.')).toBeVisible()
  expect(fetcher).toHaveBeenCalledWith('/api/auth/confirmar-email', expect.objectContaining({
    headers: expect.objectContaining({ 'X-Conta-Token': token }),
  }))
})

it('mostra confirmação, cooldown e exige novo login após alterar a senha', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith('/seguranca')) return resposta({ emailConfirmado: false, emailHabilitado: true })
    if (init?.method === 'PATCH') return resposta(null, 204)
    if (init?.method === 'POST') return resposta({}, 202)
    if (url.endsWith('/dados-pix')) return resposta(null, 204)
    if (url.endsWith('/minha-conta')) return resposta({ nome: 'Fictício', link: 'https://example.invalid', codigo: 'ABCDEFGH' })
    return resposta([])
  }))
  montar('/minha-conta', sessao)
  await userEvent.click(await screen.findByRole('button', { name: 'Enviar confirmação' }))
  expect(await screen.findByRole('button', { name: 'Aguarde um minuto' })).toBeDisabled()
  await userEvent.type(screen.getByLabelText('Senha atual'), 'atual-ficticia')
  await userEvent.type(screen.getByLabelText('Nova senha'), 'nova-ficticia')
  await userEvent.click(screen.getByRole('button', { name: 'Alterar senha' }))
  expect(await screen.findByText('Senha alterada. Entre novamente.')).toBeVisible()
  expect(sessionStorage.length).toBe(0)
})

it('envia filtros e paginação ao servidor no relatório administrativo', async () => {
  const fetcher = vi.fn(async () => resposta({ total: 50, valorTotal: 42.50, linhas: [] }))
  vi.stubGlobal('fetch', fetcher)
  montar('/relatorios', sessao)
  await userEvent.selectOptions(await screen.findByLabelText('Tipo de relatório'), '6')
  await userEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Próxima' }))
  await waitFor(() => expect(fetcher).toHaveBeenLastCalledWith(
    expect.stringMatching(/tipo=6.*pagina=2/), expect.any(Object)))
  expect(await screen.findByRole('button', { name: 'Exportar CSV' })).toBeEnabled()
})
