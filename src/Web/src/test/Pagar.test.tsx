import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import PagarPage from '../PagarPage'

const token = 'A'.repeat(64)
const ativa = { valor: 12.34, status: 2, venceEm: '2026-09-25T13:00:00Z', pixCopiaECola: 'CODIGO_FICTICIO', confirmada: false }
let fetchMock: ReturnType<typeof vi.fn>
beforeEach(() => {
  vi.useFakeTimers()
  window.history.replaceState(null, '', '/pagar#' + token)
  fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(ativa)))
  vi.stubGlobal('fetch', fetchMock)
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
})
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals() })
async function montar() { let result: ReturnType<typeof render>; await act(async () => { result = render(<PagarPage />) }); return result! }

it('remove fragmento e envia token somente no header sem armazenamento', async () => {
  const storage = vi.spyOn(Storage.prototype, 'setItem')
  await montar()
  expect(window.location.hash).toBe('')
  expect(fetchMock).toHaveBeenCalledWith('/api/public/cobranca-pix-vistoria', expect.objectContaining({ headers: { Authorization: 'PaymentLink ' + token }, cache: 'no-store', referrerPolicy: 'no-referrer' }))
  expect(storage).not.toHaveBeenCalled()
  expect(document.body.textContent).not.toContain(token)
})
it('QR é SVG local e valor é exibido em BRL', async () => {
  const { container } = await montar()
  expect(container.querySelector('svg')).not.toBeNull()
  expect(container.querySelector('img')).toBeNull()
  expect(screen.getByRole('heading', { level: 2 }).textContent).toMatch(/R\$\s*12,34/)
  expect(screen.getByLabelText('Pix copia e cola')).toHaveAttribute('readonly')
  expect(fetchMock).toHaveBeenCalledTimes(1)
})
it('não consulta token inválido', async () => {
  window.history.replaceState(null, '', '/pagar#invalido')
  await montar()
  expect(fetchMock).not.toHaveBeenCalled()
  expect(window.location.hash).toBe('')
})
it('faz polling controlado e encerra ao confirmar', async () => {
  fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(ativa))).mockResolvedValueOnce(new Response(JSON.stringify({ ...ativa, status: 5, pixCopiaECola: null, confirmada: true })))
  await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(10000) })
  expect(fetchMock).toHaveBeenCalledTimes(2)
  expect(screen.getByText('Pagamento confirmado')).toBeInTheDocument()
  expect(screen.queryByLabelText('Pix copia e cola')).not.toBeInTheDocument()
  await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
  expect(fetchMock).toHaveBeenCalledTimes(2)
})
it('não sobrepõe consultas enquanto resposta está pendente', async () => {
  let resolver!: (r: Response) => void
  fetchMock.mockReturnValue(new Promise<Response>(resolve => { resolver = resolve }))
  const mounted = await montar()
  try {
    await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  } finally {
    mounted.unmount()
    await act(async () => { resolver(new Response(JSON.stringify(ativa))) })
  }
})
it('ocultar página suspende consulta e desmontar cancela requisição', async () => {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
  const mounted = await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(20000) })
  expect(fetchMock).not.toHaveBeenCalled()
  mounted.unmount()
  expect(vi.getTimerCount()).toBe(0)
})
it('erro não expõe body e usa backoff limitado sem loop apertado', async () => {
  fetchMock.mockResolvedValue(new Response('SEGREDO_FICTICIO', { status: 500 }))
  await montar()
  expect(screen.getByRole('alert')).not.toHaveTextContent('SEGREDO_FICTICIO')
  await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
  expect(fetchMock).toHaveBeenCalledTimes(3) // t=0,10,30; próxima em 70.
  await act(async () => { await vi.advanceTimersByTimeAsync(10000) })
  expect(fetchMock).toHaveBeenCalledTimes(4)
  await act(async () => { await vi.advanceTimersByTimeAsync(59999) })
  expect(fetchMock).toHaveBeenCalledTimes(4)
  await act(async () => { await vi.advanceTimersByTimeAsync(1) })
  expect(fetchMock).toHaveBeenCalledTimes(5)
})
it.each(['30', 'data', null])('429 respeita Retry-After %s sem revelar erro', async header => {
  vi.setSystemTime(new Date('2026-09-29T12:00:00Z'))
  const headers = header ? { 'Retry-After': header === 'data' ? 'Tue, 29 Sep 2026 12:00:30 GMT' : header } : undefined
  fetchMock.mockResolvedValueOnce(new Response('SEGREDO_FICTICIO', { status: 429, headers }))
  const log = vi.spyOn(console, 'error')
  await montar()
  expect(screen.getByRole('alert')).toHaveTextContent('retomada automaticamente')
  await act(async () => { await vi.advanceTimersByTimeAsync(header ? 29999 : 9999) })
  expect(fetchMock).toHaveBeenCalledTimes(1)
  await act(async () => { await vi.advanceTimersByTimeAsync(1) })
  expect(fetchMock).toHaveBeenCalledTimes(2)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  expect(document.body.textContent).not.toContain('SEGREDO_FICTICIO')
  expect(document.body.textContent).not.toContain(token)
  expect(log).not.toHaveBeenCalled()
})
it.each(['rede', '503'])('preserva dados durante %s e recupera polling', async tipo => {
  fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(ativa)))
  if (tipo === 'rede') fetchMock.mockRejectedValueOnce(new TypeError('SEGREDO_FICTICIO'))
  else fetchMock.mockResolvedValueOnce(new Response('SEGREDO_FICTICIO', { status: 503 }))
  await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(10000) })
  expect(screen.getByLabelText('Pix copia e cola')).toHaveValue('CODIGO_FICTICIO')
  expect(screen.getByRole('alert')).toHaveTextContent('retomada')
  expect(document.body.textContent).not.toContain('SEGREDO_FICTICIO')
  await act(async () => { await vi.advanceTimersByTimeAsync(10000) })
  expect(fetchMock).toHaveBeenCalledTimes(3)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})
it('timeout cancela a requisição antes de tentar novamente', async () => {
  fetchMock.mockImplementationOnce((_path, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
    init.signal!.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
  }))
  await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(14999) })
  expect(fetchMock).toHaveBeenCalledTimes(1)
  await act(async () => { await vi.advanceTimersByTimeAsync(1) })
  expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(true)
  await act(async () => { await vi.advanceTimersByTimeAsync(10000) })
  expect(fetchMock).toHaveBeenCalledTimes(2)
})
it('404 terminal não repete nem lê o corpo sensível', async () => {
  fetchMock.mockResolvedValue(new Response('SEGREDO_FICTICIO', { status: 404 }))
  await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(120000) })
  expect(fetchMock).toHaveBeenCalledTimes(1)
  expect(screen.getByRole('alert')).not.toHaveTextContent('SEGREDO_FICTICIO')
})
it.each([5, 6, 8, 9])('estado terminal %s encerra polling', async status => {
  fetchMock.mockResolvedValue(new Response(JSON.stringify({ ...ativa, status, pixCopiaECola: null })))
  await montar()
  await act(async () => { await vi.advanceTimersByTimeAsync(120000) })
  expect(fetchMock).toHaveBeenCalledTimes(1)
})
it('desmontar aborta request pendente sem reagendar', async () => {
  fetchMock.mockImplementation((_path, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
    init.signal!.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
  }))
  const mounted = await montar()
  await act(async () => { mounted.unmount() })
  expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(true)
  expect(vi.getTimerCount()).toBe(0)
})
it('copia código por ação explícita', async () => {
  const copy = vi.fn().mockResolvedValue(undefined)
  vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: copy } })
  await montar()
  expect(copy).not.toHaveBeenCalled()
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Copiar código Pix' })) })
  expect(copy).toHaveBeenCalledWith('CODIGO_FICTICIO')
  expect(screen.getByText('Código copiado.')).toBeInTheDocument()
})
