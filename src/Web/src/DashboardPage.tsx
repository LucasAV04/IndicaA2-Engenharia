import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import type { Dashboard } from './types'
import { date, labels, money } from './format'
import { ErrorBox } from './components'

export default function DashboardPage() {
  const query = useQuery({ queryKey: ['dashboard'], queryFn: () => api<Dashboard>('/admin/dashboard') })
  return <>
    <div className="page-heading"><div><p className="eyebrow">Visão geral</p><h1>Resumo operacional</h1><p>Da indicação ao pagamento, acompanhe os registros do sistema.</p></div><button onClick={() => void query.refetch()} disabled={query.isFetching}>Atualizar resumo</button></div>
    {query.isPending ? <p role="status">Carregando resumo…</p> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : query.data && <Resumo data={query.data} />}
  </>
}
function Resumo({ data: d }: { data: Dashboard }) {
  const cards: [string, string | number][] = [
    ['Clientes', d.totalUsuarios], ['Usuários ativos', d.usuariosAtivos], ['Indicações pendentes', d.indicacoes.Pendente || 0],
    ['Vistorias agendadas', d.vistorias.Agendada || 0], ['Receita confirmada', money(d.receitaConfirmada)],
    ['Pagamentos pendentes', money(d.pagamentosPendentes)], ['Cashback disponível', money(d.cashbackDisponivel)],
    ['Cashback pago', money(d.cashbackPago)], ['Pix pendente / processando', money(d.pixPendenteProcessando)],
    ['Pix concluído', money(d.pixConcluido)], ['Falhas Pix', d.falhasPix], ['Falhas definitivas', d.falhasDefinitivasPix],
  ]
  return <><div className="cards">{cards.map(([label, value]) => <article className="card" key={label}><h2>{label}</h2><strong>{value}</strong></article>)}</div>
    {d.totalUsuarios === 0 && <p className="notice">Ainda não há usuários cadastrados. Comece pela página Usuários.</p>}
    <div className="status-grid">{([['Indicações', d.indicacoes], ['Vistorias', d.vistorias], ['Pagamentos de vistoria', d.pagamentosVistoria], ['Cashback', d.cashbacks], ['Pix', d.pagamentosPix]] as [string, Record<string, number>][]).map(([title, counts]) => <section className="panel" key={title}><h2>{title} por status</h2>{Object.entries(counts).map(([s, count]) => <div className="status-line" key={s}><span>{labels[s] || s}</span><strong>{count}</strong></div>)}</section>)}</div>
    <p className="muted">Resumo calculado em {date(d.calculadoEmUtc)}. Valores pendentes não representam dinheiro recebido.</p>
  </>
}
