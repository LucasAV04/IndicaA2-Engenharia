import { useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { ErrorBox } from './components'

const schema = z.object({ nome: z.string().trim().min(2).max(150), telefone: z.string().trim().min(10).max(30), consentimento: z.boolean().refine(v => v, 'O consentimento é obrigatório.') })
const termo = '2026-10-01'
export default function IndicacaoPublicaPage() {
  const { codigo = '' } = useParams()
  const normalizado = codigo.trim().toUpperCase()
  return <FormularioIndicacao key={normalizado} codigo={normalizado} />
}

function FormularioIndicacao({ codigo }: { codigo: string }) {
  const armazenamento = `indicacao-publica:${codigo}`
  const [estado] = useState<{ chave?: string; protocolo?: string }>(() => {
    try {
      const salvo = JSON.parse(sessionStorage.getItem(armazenamento) ?? '{}')
      return { chave: typeof salvo?.chave === 'string' ? salvo.chave : undefined,
        protocolo: typeof salvo?.protocolo === 'string' ? salvo.protocolo : undefined }
    } catch { return {} }
  })
  const chave = useRef<string | null>(estado.chave ?? null)
  const enviando = useRef(false)
  const [protocolo, setProtocolo] = useState(estado.protocolo ?? '')
  const [erro, setErro] = useState('')
  const query = useQuery({ queryKey: ['public', 'indicacao', codigo], retry: false, queryFn: async ({ signal }) => {
    const response = await fetch('/api/public/indicacoes/codigos/' + encodeURIComponent(codigo), { signal, cache: 'no-store', referrerPolicy: 'no-referrer' })
    if (!response.ok) throw new Error('Link indisponível.')
    return response.json() as Promise<{ utilizavel: boolean }>
  } })
  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { consentimento: false } })
  async function enviar(values: z.infer<typeof schema>) {
    if (enviando.current || protocolo) return
    enviando.current = true; setErro(''); chave.current ??= crypto.randomUUID()
    try {
      // Persistir antes do POST: uma resposta perdida não autoriza outra chave.
      sessionStorage.setItem(armazenamento, JSON.stringify({ chave: chave.current }))
      const response = await fetch('/api/public/indicacoes', { method: 'POST', cache: 'no-store', referrerPolicy: 'no-referrer', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': chave.current }, body: JSON.stringify({ ...values, codigo, versaoTermo: termo }) })
      if (!response.ok) {
        setErro(response.status === 429 ? 'Limite de solicitações atingido. Aguarde antes de tentar novamente.' : 'Não foi possível enviar. Confira os dados e tente novamente.')
        return
      }
      const result = await response.json() as { protocolo: string }
      sessionStorage.setItem(armazenamento, JSON.stringify({ chave: chave.current, protocolo: result.protocolo }))
      setProtocolo(result.protocolo)
    } catch { setErro('Conexão indisponível. Tente novamente; seu envio não será duplicado.') }
    finally { enviando.current = false }
  }
  function novaIndicacao() {
    try {
      sessionStorage.removeItem(armazenamento)
      chave.current = null; setProtocolo(''); setErro('')
      reset({ nome: '', telefone: '', consentimento: false })
    } catch { setErro('Não foi possível iniciar outra indicação nesta sessão.') }
  }
  return <main className="public-referral panel"><h1>Indicar uma pessoa</h1>
    {protocolo ? <section role="status"><h2>Indicação recebida</h2><p>Guarde o protocolo: <strong className="opaque-protocol">{protocolo}</strong></p><p>Nenhuma conta foi criada automaticamente.</p><button type="button" onClick={novaIndicacao}>Cadastrar outra indicação</button>{erro && <ErrorBox message={erro} />}</section>
      : query.isPending ? <p role="status">Verificando link…</p> : query.isError || !query.data?.utilizavel ? <ErrorBox message="Link indisponível." />
        : <form onSubmit={handleSubmit(enviar)}>
          <label htmlFor="indicada-nome">Nome da pessoa indicada</label><input id="indicada-nome" autoComplete="off" maxLength={150} {...register('nome')} />{errors.nome && <p role="alert">Informe um nome válido.</p>}
          <label htmlFor="indicada-telefone">Telefone</label><input id="indicada-telefone" type="tel" autoComplete="off" maxLength={30} {...register('telefone')} />{errors.telefone && <p role="alert">Informe um telefone válido.</p>}
          <section aria-label="Consentimento e privacidade"><h2>Consentimento e privacidade</h2><p>O nome e telefone informados serão usados pela A2 para atender esta indicação e acompanhar a vistoria. Não cadastre dados sem autorização da pessoa indicada. Este formulário não cria conta nem dispara mensagens externas.</p><p>Versão do termo: {termo}</p>
            <label htmlFor="consentimento"><input id="consentimento" type="checkbox" {...register('consentimento')} />Confirmo a autorização para compartilhar estes dados e aceito seu uso para atender a indicação.</label>{errors.consentimento && <p role="alert">{errors.consentimento.message}</p>}
          </section>{erro && <ErrorBox message={erro} />}<button className="primary" disabled={isSubmitting}>{isSubmitting ? 'Enviando…' : 'Enviar indicação'}</button>
        </form>}
  </main>
}
