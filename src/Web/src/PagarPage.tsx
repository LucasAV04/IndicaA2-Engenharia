import { useEffect, useRef, useState } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { money, utcDate } from './format'

type Publica = { valor: number; status: number; venceEm: string | null; pixCopiaECola: string | null; confirmada: boolean }
const estados = ['Preparando cobrança', 'Preparando cobrança', 'Aguardando pagamento', 'Aguardando conciliação', 'Conferindo recebimento', 'Pagamento confirmado', 'Cobrança expirada', 'Remoção pendente', 'Cobrança removida', 'Cobrança indisponível', 'Conferência administrativa necessária']
export default function PagarPage() {
  const token = useRef<string | null>(null)
  const [data, setData] = useState<Publica | null>(null)
  const [error, setError] = useState<'transitorio' | 'terminal' | null>(null)
  const [copiado, setCopiado] = useState(false)
  useEffect(() => {
    if (token.current === null) {
      token.current = window.location.hash.slice(1)
      window.history.replaceState(null, '', window.location.pathname)
    }
    if (!/^[a-fA-F0-9]{64}$/.test(token.current)) return
    let disposed = false
    let timer: ReturnType<typeof setTimeout> | undefined
    let controller: AbortController | undefined
    let timeout: ReturnType<typeof setTimeout> | undefined
    let falhas = 0
    const agendar = (delay: number) => { timer = setTimeout(() => void consultar(), delay) }
    async function consultar() {
      if (disposed) return
      if (document.visibilityState === 'hidden') { agendar(10000); return }
      controller = new AbortController()
      timeout = setTimeout(() => controller?.abort(), 15000)
      let retryAfter = 0
      try {
        const response = await fetch('/api/public/cobranca-pix-vistoria', { headers: { Authorization: 'PaymentLink ' + token.current }, signal: controller.signal, cache: 'no-store', referrerPolicy: 'no-referrer' })
        if (disposed) return
        if (!response.ok && response.status !== 429 && response.status < 500) {
          setError('terminal'); setData(null); return
        }
        if (response.status === 429) {
          const header = response.headers.get('Retry-After')
          if (header) {
            const delay = /^\d+$/.test(header.trim()) ? Number(header) * 1000 : Date.parse(header) - Date.now()
            if (Number.isFinite(delay)) retryAfter = Math.max(0, delay)
          }
        }
        if (!response.ok) throw new Error('indisponivel')
        const value: Publica = await response.json()
        if (disposed) return
        setData(value); setError(null); falhas = 0
        if ([0, 1, 2, 3, 4, 7].includes(value.status)) agendar(10000)
      } catch {
        if (!disposed) {
          setError('transitorio')
          const backoff = Math.min(60000, 10000 * 2 ** Math.min(falhas++, 3))
          // Retry-After é um limite inferior do servidor, inclusive se superar o backoff local.
          agendar(Math.max(backoff, retryAfter))
        }
      } finally { clearTimeout(timeout) }
    }
    void consultar()
    return () => { disposed = true; controller?.abort(); clearTimeout(timer); clearTimeout(timeout) }
  }, [])
  async function copiar() {
    if (!data?.pixCopiaECola) return
    try { await navigator.clipboard.writeText(data.pixCopiaECola); setCopiado(true) } catch { setCopiado(false) }
  }
  return <main className="panel"><h1>Pagamento de vistoria</h1><p>A2 Engenharia & Diagnóstico</p>
    {error && <p role="alert">{error === 'transitorio' ? 'Atualização temporariamente indisponível. A consulta será retomada automaticamente.' : 'Não foi possível consultar este link. Solicite orientação à A2.'}</p>}
    {!data ? <p role="status">Consulte um link de pagamento válido fornecido pela A2.</p> : <>
      <h2>{money(data.valor)}</h2><p>Vencimento: {utcDate(data.venceEm)}</p><p role="status">{estados[data.status] || 'Conferência necessária'}</p>
      {data.status === 2 && data.pixCopiaECola && <><QRCodeSVG value={data.pixCopiaECola} title="QR Code Pix da vistoria" size={240} marginSize={4} />
        <label htmlFor="codigoPix">Pix copia e cola</label><textarea id="codigoPix" readOnly value={data.pixCopiaECola} />
        <button onClick={() => void copiar()}>Copiar código Pix</button><p aria-live="polite">{copiado ? 'Código copiado.' : ''}</p></>}
    </>}
  </main>
}
