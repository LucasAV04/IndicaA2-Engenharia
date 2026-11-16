import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api, ApiError, readSession, saveSession } from './api'
import { ErrorBox } from './components'

const tipos = ['Pagamentos recebidos', 'Cashbacks disponíveis', 'Cashbacks pagos',
  'Ordens Pix em aberto', 'Falhas financeiras', 'Cobranças divergentes', 'E-mails']

type Filtros = { tipo: string; inicio: string; fim: string; status: string; tamanho: string }
type Resultado = { total: number; valorTotal: number | null; linhas: {
  id: string; dataUtc: string; status: number; valor: number | null
}[] }

const dinheiro = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const dataLocal = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })

function parametros(filtros: Filtros, pagina: number) {
  return new URLSearchParams({ ...Object.fromEntries(
    Object.entries(filtros).filter(([, valor]) => valor !== '')),
  pagina: String(pagina) }).toString()
}

export default function RelatoriosPage() {
  const [form, setForm] = useState<Filtros>({ tipo: '0', inicio: '', fim: '', status: '', tamanho: '30' })
  const [aplicados, setAplicados] = useState(form)
  const [pagina, setPagina] = useState(1)
  const [exportando, setExportando] = useState(false)
  const [erro, setErro] = useState('')
  const query = useQuery({
    queryKey: ['relatorios', aplicados, pagina],
    queryFn: ({ signal }) => api<Resultado>('/admin/relatorios?' + parametros(aplicados, pagina), { signal }),
  })

  async function exportar() {
    setErro('')
    setExportando(true)
    try {
      const sessao = readSession()
      if (!sessao) throw new Error()
      const resposta = await fetch('/api/admin/relatorios/csv?' + parametros(aplicados, 1), {
        headers: { Authorization: 'Bearer ' + sessao.accessToken },
      })
      if (resposta.status === 401) saveSession(null)
      if (!resposta.ok) throw new ApiError(resposta.status, '')
      const url = URL.createObjectURL(await resposta.blob())
      const link = document.createElement('a')
      link.href = url
      link.download = 'relatorio-indicaa2.csv'
      link.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 0)
    } catch (error) {
      setErro(error instanceof ApiError && error.status === 400
        ? 'Revise os filtros e limite a exportação a 5.000 registros.'
        : 'Não foi possível exportar o relatório.')
    } finally {
      setExportando(false)
    }
  }

  return <>
    <h1>Relatórios</h1>
    <form className="panel" onSubmit={evento => {
      evento.preventDefault()
      setAplicados(form)
      setPagina(1)
    }}>
      <div className="field"><label htmlFor="rel-tipo">Tipo de relatório</label>
        <select id="rel-tipo" value={form.tipo} onChange={e => setForm({ ...form, tipo: e.target.value })}>
          {tipos.map((tipo, indice) => <option key={tipo} value={indice}>{tipo}</option>)}
        </select>
      </div>
      <div className="field"><label htmlFor="rel-inicio">Data inicial (UTC)</label>
        <input id="rel-inicio" type="date" value={form.inicio} onChange={e => setForm({ ...form, inicio: e.target.value })} />
      </div>
      <div className="field"><label htmlFor="rel-fim">Data final (UTC)</label>
        <input id="rel-fim" type="date" value={form.fim} onChange={e => setForm({ ...form, fim: e.target.value })} />
      </div>
      <div className="field"><label htmlFor="rel-status">Código do status</label>
        <input id="rel-status" type="number" min="0" max="10" value={form.status} onChange={e => setForm({ ...form, status: e.target.value })} />
      </div>
      <div className="field"><label htmlFor="rel-tamanho">Registros por página</label>
        <input id="rel-tamanho" type="number" min="1" max="100" required value={form.tamanho} onChange={e => setForm({ ...form, tamanho: e.target.value })} />
      </div>
      <button type="submit">Aplicar filtros</button>
    </form>
    <section className="panel" aria-label="Resultado do relatório">
      <p>Filtros aplicados — {tipos[Number(aplicados.tipo)]}, {aplicados.inicio || 'início padrão'} a {aplicados.fim || 'hoje'}, status {aplicados.status || 'todos'}.</p>
      <button disabled={exportando || !query.data} onClick={() => void exportar()}>Exportar CSV</button>
      {erro && <p role="alert">{erro}</p>}
      {query.isPending && <p role="status">Carregando relatório…</p>}
      {query.isError && <ErrorBox message="Não foi possível consultar o relatório. Verifique os filtros." />}
      {query.data && <>
        <p role="status">{query.data.total} registros{query.data.valorTotal !== null && ' · Total ' + dinheiro.format(query.data.valorTotal)}</p>
        <div className="table-wrap"><table>
          <caption>{tipos[Number(aplicados.tipo)]}</caption>
          <thead><tr><th>Referência</th><th>Data e hora</th><th>Status</th><th>Valor</th></tr></thead>
          <tbody>{query.data.linhas.map(linha => <tr key={linha.id}>
            <td>{linha.id}</td><td>{dataLocal.format(new Date(linha.dataUtc))}</td>
            <td>{linha.status}</td><td>{linha.valor === null ? '—' : dinheiro.format(linha.valor)}</td>
          </tr>)}</tbody>
        </table></div>
        <button disabled={pagina === 1} onClick={() => setPagina(pagina - 1)}>Anterior</button>
        <span> Página {pagina} </span>
        <button disabled={pagina * Number(aplicados.tamanho) >= query.data.total}
          onClick={() => setPagina(pagina + 1)}>Próxima</button>
      </>}
    </section>
  </>
}
