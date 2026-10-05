import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from '../App'
import { saveSession } from '../api'
import type { Sessao } from '../types'

const sessao: Sessao = { usuarioId: 'usuario-a', nome: 'Cliente fictícia', email: 'ficticio@example.invalid', tipoUsuario: 1, accessToken: 'jwt-ficticio', expiresAtUtc: '2099-01-01T00:00:00Z' }
type Request = { path: string; method: string; body: Record<string, unknown> | null; headers: HeadersInit | undefined }
let requests: Request[]
let dados: Record<string, unknown>
let statusPost: number
const resposta = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
beforeEach(() => {
  saveSession(null); sessionStorage.clear(); requests = []; statusPost = 200
  dados = { '/api/public/indicacoes/codigos/ABCD1234': { utilizavel: true }, '/api/minha-conta': { nome: 'Cliente fictícia', codigo: 'ABCD1234', link: 'https://example.invalid/indicar/ABCD1234' }, '/api/minha-conta/indicacoes': [{ nome: 'Pessoa', telefoneMascarado: '••••00', status: 4, criadaEm: '2026-10-01T12:00:00Z' }], '/api/minha-conta/cashbacks': [{ valor: 100.01, status: 2, situacaoPix: 2 }], '/api/minha-conta/dados-pix': { tipoChavePix: 2, chaveMascarada: '••••••••' }, '/api/notificacoes': [{ id: 'aviso-1', tipo: 4, criadaEm: '2026-10-01T12:00:00Z', lidaEm: null }] }
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => {
    const method = init.method ?? 'GET'; requests.push({ path, method, body: init.body ? JSON.parse(String(init.body)) : null, headers: init.headers })
    if (path === '/api/public/indicacoes' && method === 'POST') return resposta(statusPost === 200 ? { protocolo: 'a'.repeat(64) } : { detail: 'SEGREDO_FICTICIO_NAO_EXIBIR' }, statusPost)
    if (method !== 'GET') return resposta(null, 204)
    return resposta(dados[path] ?? [])
  }))
})
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })
function montar(path: string, session?: Sessao) {
  if (session) saveSession(session)
  const cache = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const ui = render(<QueryClientProvider client={cache}><MemoryRouter initialEntries={[path]}><App /></MemoryRouter></QueryClientProvider>)
  return { ...ui, cache }
}
async function preencher() {
  await userEvent.type(await screen.findByLabelText('Nome da pessoa indicada'), 'Pessoa fictícia')
  await userEvent.type(screen.getByLabelText('Telefone'), '85999990000')
}
it('link válido não expõe proprietária e consentimento começa desmarcado', async () => {
  montar('/indicar/ABCD1234'); await screen.findByLabelText('Nome da pessoa indicada')
  expect(screen.getByRole('checkbox')).not.toBeChecked()
  expect(document.body).not.toHaveTextContent('Cliente fictícia')
  expect(requests).toHaveLength(1)
  expect(requests[0].headers).toBeUndefined()
})
it('link inválido não abre formulário ou dados privados', async () => {
  dados['/api/public/indicacoes/codigos/ABCD1234'] = { utilizavel: false }
  montar('/indicar/ABCD1234'); expect(await screen.findByRole('alert')).toHaveTextContent('Link indisponível')
  expect(screen.queryByLabelText('Telefone')).not.toBeInTheDocument()
})
it('sem consentimento não envia indicação', async () => {
  montar('/indicar/ABCD1234'); await preencher(); await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' }))
  expect(await screen.findByText('O consentimento é obrigatório.')).toBeInTheDocument()
  expect(requests.filter(r => r.method === 'POST')).toHaveLength(0)
})
it('submissão única retorna protocolo opaco sem guardar PII no armazenamento', async () => {
  montar('/indicar/ABCD1234'); await preencher(); await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.dblClick(screen.getByRole('button', { name: 'Enviar indicação' }))
  expect(await screen.findByText('a'.repeat(64))).toBeInTheDocument()
  const posts = requests.filter(r => r.method === 'POST'); expect(posts).toHaveLength(1)
  expect(posts[0].body).toMatchObject({ consentimento: true, versaoTermo: '2026-10-01', codigo: 'ABCD1234' })
  expect(new Headers(posts[0].headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/)
  expect(JSON.stringify(localStorage)).not.toContain('Pessoa fictícia')
  expect(JSON.stringify(sessionStorage)).not.toContain('Pessoa fictícia')
  expect(JSON.stringify(sessionStorage)).not.toContain('85999990000')
  expect(Object.keys(JSON.parse(sessionStorage.getItem('indicacao-publica:ABCD1234')!)).sort()).toEqual(['chave', 'protocolo'])
})
it('429 é sanitizado e tentativa posterior reutiliza a chave', async () => {
  statusPost = 429; montar('/indicar/ABCD1234'); await preencher(); await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Aguarde')
  expect(document.body).not.toHaveTextContent('SEGREDO_FICTICIO_NAO_EXIBIR')
  statusPost = 200; await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' })); await screen.findByText('a'.repeat(64))
  const posts = requests.filter(r => r.method === 'POST'); expect(posts).toHaveLength(2)
  expect(new Headers(posts[0].headers).get('Idempotency-Key')).toBe(new Headers(posts[1].headers).get('Idempotency-Key'))
})
it('remontagem após sucesso recupera protocolo e só ação explícita permite nova indicação', async () => {
  const a = montar('/indicar/ABCD1234'); await preencher(); await userEvent.click(screen.getByRole('checkbox')); await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' })); await screen.findByText('a'.repeat(64)); a.unmount()
  montar('/indicar/abcd1234'); await screen.findByText('a'.repeat(64))
  expect(screen.queryByLabelText('Nome da pessoa indicada')).not.toBeInTheDocument()
  expect(requests.filter(r => r.method === 'POST')).toHaveLength(1)
  await userEvent.click(screen.getByRole('button', { name: 'Cadastrar outra indicação' }))
  expect(screen.getByRole('checkbox')).not.toBeChecked()
  expect(screen.getByLabelText('Nome da pessoa indicada')).toHaveValue('')
  await preencher(); await userEvent.click(screen.getByRole('checkbox')); await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' }))
  await screen.findByText('a'.repeat(64))
  const posts = requests.filter(r => r.method === 'POST')
  expect(posts).toHaveLength(2)
  expect(new Headers(posts[0].headers).get('Idempotency-Key')).not.toBe(new Headers(posts[1].headers).get('Idempotency-Key'))
})
it('resposta perdida preserva chave antes do POST e retry após reload recupera protocolo sem duplicar', async () => {
  const servidor = new Map<string, string>()
  const chaves: string[] = []
  let perderResposta = true
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => {
    if (init.method !== 'POST') return resposta({ utilizavel: true })
    const chave = new Headers(init.headers).get('Idempotency-Key')!
    expect(JSON.parse(sessionStorage.getItem('indicacao-publica:ABCD1234')!).chave).toBe(chave)
    chaves.push(chave)
    if (!servidor.has(chave)) servidor.set(chave, 'b'.repeat(64))
    if (perderResposta) { perderResposta = false; throw new TypeError('Falha fictícia') }
    return resposta({ protocolo: servidor.get(chave) })
  }))
  const primeira = montar('/indicar/ABCD1234'); await preencher(); await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Conexão indisponível')
  primeira.unmount(); montar('/indicar/ABCD1234'); await preencher()
  expect(screen.getByRole('checkbox')).not.toBeChecked()
  await userEvent.click(screen.getByRole('checkbox')); await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' }))
  await screen.findByText('b'.repeat(64))
  expect(chaves).toHaveLength(2); expect(chaves[0]).toBe(chaves[1]); expect(servidor.size).toBe(1)
})
it('códigos diferentes não compartilham chave nem protocolo', async () => {
  dados['/api/public/indicacoes/codigos/EFGH5678'] = { utilizavel: true }
  const primeira = montar('/indicar/ABCD1234'); await preencher(); await userEvent.click(screen.getByRole('checkbox'))
  await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' })); await screen.findByText('a'.repeat(64)); primeira.unmount()
  montar('/indicar/EFGH5678'); await preencher(); expect(screen.queryByText('a'.repeat(64))).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('checkbox')); await userEvent.click(screen.getByRole('button', { name: 'Enviar indicação' })); await screen.findByText('a'.repeat(64))
  const a = JSON.parse(sessionStorage.getItem('indicacao-publica:ABCD1234')!)
  const b = JSON.parse(sessionStorage.getItem('indicacao-publica:EFGH5678')!)
  expect(a.chave).not.toBe(b.chave)
})
it('portal exibe apenas listas próprias e máscaras sem menu administrativo', async () => {
  montar('/minha-conta', sessao); await screen.findByText(/Pessoa • ••••00/)
  expect(screen.getByText('••••••••')).toBeInTheDocument()
  expect(screen.queryByRole('navigation', { name: 'Principal' })).not.toBeInTheDocument()
  expect(requests.every(r => r.path.startsWith('/api/minha-conta') || r.path === '/api/notificacoes')).toBe(true)
  expect(document.body).toHaveTextContent('Cashback pago')
})
it('cópia utiliza link retornado pelo servidor', async () => {
  const user = userEvent.setup(); montar('/minha-conta', sessao); await screen.findByText('Código: ABCD1234')
  const copy = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue()
  await user.click(screen.getByRole('button', { name: 'Copiar link' })); expect(copy).toHaveBeenCalledWith('https://example.invalid/indicar/ABCD1234')
  expect(await screen.findByText('Link copiado.')).toBeInTheDocument()
})
it('Dados Pix começam vazios e invalidam somente o próprio cache', async () => {
  montar('/minha-conta', sessao); await userEvent.click(await screen.findByRole('button', { name: 'Atualizar Dados Pix' }))
  expect(screen.getByLabelText('Nova chave Pix')).toHaveValue(''); expect(screen.getByLabelText('Nova chave Pix')).toHaveAttribute('type', 'password')
  await userEvent.selectOptions(screen.getByLabelText('Tipo de chave'), '2'); await userEvent.type(screen.getByLabelText('Nova chave Pix'), 'novo@example.invalid')
  await userEvent.click(screen.getByRole('button', { name: 'Salvar' }))
  await waitFor(() => expect(requests.filter(r => r.path === '/api/minha-conta/dados-pix' && r.method === 'GET')).toHaveLength(2))
  expect(requests.find(r => r.method === 'PUT')?.body).toEqual({ tipoChavePix: 2, chavePix: 'novo@example.invalid' })
  expect(requests.filter(r => r.path === '/api/minha-conta/indicacoes')).toHaveLength(1)
})
it('notificação marca somente a referência autenticada', async () => {
  montar('/minha-conta', sessao); await userEvent.click(await screen.findByRole('button', { name: 'Marcar como lida' }))
  await waitFor(() => expect(requests.some(r => r.path === '/api/notificacoes/aviso-1/lida' && r.method === 'PATCH')).toBe(true))
  expect(requests.filter(r => r.path === '/api/minha-conta/cashbacks')).toHaveLength(1)
})
it('troca de sessão remove cache e estado da pessoa anterior', async () => {
  const { cache } = montar('/minha-conta', sessao); await screen.findByText(/Pessoa • ••••00/)
  dados['/api/minha-conta/indicacoes'] = []; dados['/api/minha-conta'] = { nome: 'Outra cliente', codigo: 'EFGH5678', link: 'https://example.invalid/indicar/EFGH5678' }
  await act(async () => saveSession({ ...sessao, usuarioId: 'usuario-b' }))
  await screen.findByText('Olá, Outra cliente'); expect(screen.queryByText(/Pessoa • ••••00/)).not.toBeInTheDocument()
  expect(cache.getQueryCache().getAll().some(q => q.queryKey.includes('usuario-a'))).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: 'Sair' })); await screen.findByRole('button', { name: 'Entrar' }); expect(cache.getQueryCache().getAll()).toHaveLength(0)
})
