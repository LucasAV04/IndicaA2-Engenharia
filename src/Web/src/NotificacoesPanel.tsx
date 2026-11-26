import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api, readSession, send } from './api'
import { ErrorBox } from './components'
import { businessDate, utcDate } from './format'
import { apresentarNotificacao, type Notificacao } from './notificacoes'
import './notificacoes.css'

export default function NotificacoesPanel({ admin = false }: { admin?: boolean }) {
  const usuario = readSession()?.usuarioId
  const path = admin ? '/admin/jornada/notificacoes' : '/notificacoes'
  const key = ['notificacoes', usuario, admin]
  const client = useQueryClient()
  const query = useQuery({ queryKey: key, queryFn: () => api<Notificacao[]>(path) })
  const total = useQuery({
    queryKey: [...key, 'total'],
    queryFn: async () => admin
      ? (await api<{ naoLidas: number }>('/admin/jornada/indicadores')).naoLidas
      : (await api<{ quantidade: number }>('/notificacoes/nao-lidas')).quantidade,
  })
  const mutation = useMutation({
    mutationFn: (id: string | null) => send(id ? path + '/' + id + '/lida' : path + '/lidas', 'PATCH'),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: key })
      if (admin) await client.invalidateQueries({ queryKey: ['jornada', 'indicadores'] })
    },
  })

  return <section className="panel notificacoes"><h2>{admin ? 'Alertas administrativos' : 'Minhas notificações'}</h2>
    {query.isPending ? <p role="status">Carregando notificações…</p> : query.isError ? <ErrorBox retry={() => void query.refetch()} /> : <>
      <p role="status">{query.data.filter(n => !n.lidaEm).length} não lidas nesta lista{typeof total.data === 'number' && ' · ' + total.data + ' no total'}</p>
      {(admin || query.data.length > 0 || !!total.data) && <>
        <button disabled={mutation.isPending || total.data === 0} onClick={() => mutation.mutate(null)}>Marcar todas como lidas</button>
        <p>Esta ação inclui notificações fora da lista exibida.</p>
      </>}
      {!query.data.length && <p>Nenhuma notificação.</p>}
      <ul>{query.data.map(n => {
        const item = apresentarNotificacao(n, admin)
        return <li key={n.id} className={n.lidaEm ? undefined : 'notificacao-nao-lida'}>
          <h3>{item.titulo}</h3>
          <p>{item.descricao}</p>
          {n.contexto?.primeiroNome && n.tipo !== 0 && <p>Indicação de {n.contexto.primeiroNome}</p>}
          {n.contexto?.agendamentoAtual && <p>Agendamento atual: {businessDate(n.contexto.agendamentoAtual)}</p>}
          <p><time dateTime={n.criadaEm}>{utcDate(n.criadaEm)}</time> · <strong>{n.lidaEm ? 'Lida' : 'Não lida'}</strong></p>
          {item.destino && <p>{admin ? <Link to={item.destino}>{item.acao}</Link>
            : <a href={item.destino.slice('/minha-conta'.length)}>{item.acao}</a>}</p>}
          {!n.lidaEm && <button disabled={mutation.isPending} onClick={() => mutation.mutate(n.id)}>Marcar como lida</button>}
        </li>
      })}</ul>
    </>}{mutation.isError && <ErrorBox />}
  </section>
}
