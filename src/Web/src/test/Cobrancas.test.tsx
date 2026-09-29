import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, afterEach, expect, it, vi } from 'vitest'
import CobrancasPage from '../CobrancasPage'
import { MemoryRouter } from 'react-router-dom'

const cobranca = { id: 'cobranca-ficticia', pagamentoVistoriaId: 'pagamento-ficticio', valor: 12.34, status: 2, venceEm: null, confirmadoEm: null }
let requests: { path: string; method: string }[]
let rows: typeof cobranca[]
let historyError: boolean
beforeEach(() => {
  requests = []; rows = [cobranca]; historyError = false
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => {
    requests.push({ path, method: init.method || 'GET' })
    if (path.endsWith('/auditoria')) return new Response(JSON.stringify(historyError ? { detail: 'SEGREDO_FICTICIO' } : [{ id: 'audit', tipo: 0, codigo: 'ativa', startedAt: '2026-09-25T12:00:00Z', finishedAt: null }]), { status: historyError ? 500 : 200 })
    if (path.endsWith('/link')) return new Response(JSON.stringify({ link: 'https://example.invalid/pagar#' + 'A'.repeat(64) }))
    if (path.endsWith('/pagamentos-vistoria')) return new Response(JSON.stringify([{ id: 'pagamento-ficticio', vistoriaId: 'vistoria-ficticia', valor: 12.34, status: 0 }]))
    if (path.includes('/por-pagamento/')) return new Response(JSON.stringify(cobranca))
    return new Response(JSON.stringify(rows))
  }))
})
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })
async function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  await act(async () => { render(<MemoryRouter><QueryClientProvider client={client}><CobrancasPage /></QueryClientProvider></MemoryRouter>) })
  return client
}
it('lista estados e valor sem consultas auxiliares', async () => {
  await mount()
  expect(await screen.findByText('Ativa')).toBeInTheDocument()
  expect(screen.getByText(/12,34/)).toBeInTheDocument()
  expect(requests).toEqual([{ path: '/api/cobrancas-pix-vistoria', method: 'GET' }])
})
it('vazio orienta criar cobrança sem confirmação manual', async () => {
  rows = []; await mount()
  expect(await screen.findByText(/Nenhuma cobrança cadastrada/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Confirmar pagamento' })).not.toBeInTheDocument()
})
it('pagamentos são consultados somente ao abrir geração', async () => {
  await mount(); await screen.findByText('Ativa')
  expect(requests.some(x => x.path.endsWith('/pagamentos-vistoria'))).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: 'Gerar cobrança e link' }))
  expect(await screen.findByRole('option', { name: /vistoria-ficticia/ })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Confirmar geração' })).toBeDisabled()
})
it('geração usa somente ID e disponibiliza link sem persistir token', async () => {
  const storage = vi.spyOn(Storage.prototype, 'setItem')
  await mount(); fireEvent.click(screen.getByRole('button', { name: 'Gerar cobrança e link' }))
  await screen.findByRole('option', { name: /vistoria-ficticia/ })
  fireEvent.change(screen.getByLabelText('Pagamento pendente'), { target: { value: 'pagamento-ficticio' } })
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar geração' }))
  expect(await screen.findByRole('button', { name: 'Copiar link de pagamento' })).toBeInTheDocument()
  expect(requests.filter(x => x.method === 'POST')).toEqual([
    { path: '/api/cobrancas-pix-vistoria/por-pagamento/pagamento-ficticio', method: 'POST' },
    { path: '/api/cobrancas-pix-vistoria/cobranca-ficticia/link', method: 'POST' },
  ])
  expect(storage).not.toHaveBeenCalled()
  expect(document.body.textContent).not.toContain('A'.repeat(64))
})
it('auditoria é carregada somente por solicitação', async () => {
  await mount(); await screen.findByText('Ativa')
  expect(requests.some(x => x.path.endsWith('/auditoria'))).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: 'Ver auditoria' }))
  expect(await screen.findByText(/ativa —/)).toBeInTheDocument()
})
it('erro de auditoria não remove lista nem mostra erro interno', async () => {
  historyError = true; await mount(); await screen.findByText('Ativa')
  fireEvent.click(screen.getByRole('button', { name: 'Ver auditoria' }))
  await screen.findByRole('alert')
  expect(screen.getByText('Ativa')).toBeInTheDocument()
  expect(document.body.textContent).not.toContain('SEGREDO_FICTICIO')
})
it('rotação recusada não altera link', async () => {
  vi.mocked(window.confirm).mockReturnValue(false)
  await mount(); fireEvent.click(await screen.findByRole('button', { name: 'Rotacionar link' }))
  expect(requests.filter(x => x.method === 'POST')).toEqual([])
})
it('rotação invalida somente cache de cobranças', async () => {
  const client = await mount(); const spy = vi.spyOn(client, 'invalidateQueries')
  fireEvent.click(await screen.findByRole('button', { name: 'Rotacionar link' }))
  await screen.findByRole('button', { name: 'Copiar link de pagamento' })
  await waitFor(() => expect(spy).toHaveBeenCalledTimes(1))
  expect(spy.mock.calls[0][0]?.queryKey).toEqual(['cobrancas'])
})
it('somente terminal permitido oferece reemissão', async () => {
  rows = [{ ...cobranca, status: 6 }]; await mount()
  fireEvent.click(await screen.findByRole('button', { name: 'Reemitir cobrança' }))
  await waitFor(() => expect(requests.some(x => x.method === 'POST' && x.path.endsWith('/por-pagamento/pagamento-ficticio'))).toBe(true))
  expect(screen.getByRole('button', { name: 'Rotacionar link' })).toBeDisabled()
})
it('cobrança ativa não oferece reemissão', async () => {
  await mount(); await screen.findByText('Ativa')
  expect(screen.queryByRole('button', { name: 'Reemitir cobrança' })).not.toBeInTheDocument()
})
