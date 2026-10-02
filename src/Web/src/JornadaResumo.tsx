import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import { ErrorBox } from './components'
export default function JornadaResumo() {
  const query = useQuery({ queryKey: ['jornada', 'indicadores'], queryFn: () => api<Record<string, number>>('/admin/jornada/indicadores') })
  const labels = { semDadosPix: 'Cashbacks aguardando Dados Pix', disponiveisSemOrdem: 'Cashbacks disponíveis sem ordem', pixPendente: 'Ordens Pix pendentes', pixProcessando: 'Ordens Pix processando', pixConcluido: 'Ordens Pix concluídas', pixFalhou: 'Ordens com falha confirmada', pixFalhaDefinitiva: 'Ordens com falha definitiva', naoLidas: 'Alertas administrativos não lidos' }
  return <section aria-label="Jornada automática"><h2>Jornada automática</h2>{query.isPending ? <p role="status">Carregando jornada…</p> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : <div className="cards">{Object.entries(labels).map(([key, label]) => <article className="card" key={key}><h3>{label}</h3><strong>{query.data[key] ?? 0}</strong></article>)}</div>}</section>
}
