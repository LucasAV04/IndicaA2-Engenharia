import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import { ErrorBox } from './components'
import { utcDate } from './format'

export function CopiarLinkIndicadora({ id }: { id: string }) {
  const [solicitado, setSolicitado] = useState(false)
  const [copiado, setCopiado] = useState(false)
  const [falhaCopia, setFalhaCopia] = useState(false)
  const query = useQuery({ queryKey: ['link-indicadora', id], queryFn: () => api<{ link: string }>(`/admin/jornada/usuarios/${id}/link`), enabled: solicitado })
  return <div><button onClick={() => setSolicitado(true)}>Link de indicação</button>{solicitado && (query.isPending ? <span role="status">Carregando link…</span> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : <><p className="opaque-protocol">{query.data.link}</p><button onClick={() => { void navigator.clipboard.writeText(query.data.link).then(() => { setCopiado(true); setFalhaCopia(false) }).catch(() => setFalhaCopia(true)) }}>Copiar link</button>{copiado && <span role="status">Link copiado.</span>}{falhaCopia && <p role="alert">Não foi possível copiar. Selecione o link acima.</p>}</>)}</div>
}

type Origem = { id: string; origem: number; versaoTermo: string | null; consentimentoEm: string | null }
export function OrigensIndicacoes() {
  const [aberto, setAberto] = useState(false)
  const query = useQuery({ queryKey: ['indicacoes', 'origens'], queryFn: () => api<Origem[]>('/admin/jornada/origens'), enabled: aberto })
  return <section className="panel"><button onClick={() => setAberto(!aberto)} aria-expanded={aberto}>Origem e consentimento das indicações</button>{aberto && (query.isPending ? <p role="status">Carregando consentimentos…</p> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : !query.data.length ? <p>Nenhuma indicação.</p> : <div className="table-wrap"><table><caption>Registro de origem e consentimento</caption><thead><tr><th>Referência administrativa</th><th>Origem</th><th>Termo</th><th>Consentimento</th></tr></thead><tbody>{query.data.map(o => <tr key={o.id}><td>{o.id}</td><td>{o.origem === 1 ? 'Pública' : 'Administrativa / histórica'}</td><td>{o.versaoTermo ?? 'Não registrado'}</td><td>{utcDate(o.consentimentoEm)}</td></tr>)}</tbody></table></div>)}</section>
}
