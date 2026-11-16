import { Link } from 'react-router-dom'
import type { Dashboard } from './types'

const possui = (contagens: Record<string, number> | undefined) =>
  Object.values(contagens ?? {}).some(total => total > 0)

export default function PrimeirosPassos({ dados, cliente }: { dados: Dashboard; cliente: boolean }) {
  const passos = [
    ['Cadastrar um tipo de planta', '/tipos-planta', (dados.tiposCadastrados ?? 0) > 0],
    ['Publicar uma tabela de preços', '/precos-vistoria', (dados.tiposComPrecoAtivo ?? 0) > 0],
    ['Cadastrar ou localizar uma cliente', '/usuarios', cliente],
    ['Cadastrar uma vistoria', '/vistorias', possui(dados.vistorias)],
    ['Gerar ou acompanhar o pagamento', '/pagamentos-vistoria', possui(dados.pagamentosVistoria)],
    ['Acompanhar a indicação e o cashback', '/indicacoes', possui(dados.indicacoes) && possui(dados.cashbacks)],
  ] as const
  const inicioConcluido = passos.slice(0, 4).every(([, , concluido]) => concluido)

  return <section className="panel primeiros-passos" aria-labelledby="primeiros-passos">
    <h2 id="primeiros-passos">Primeiros passos</h2>
    <details open={!inicioConcluido || undefined}>
      <summary>Orientação para começar</summary>
      <p>Cadastre o catálogo e use os preços definidos pela A2. Acompanhe aqui o que já tem registros no sistema.</p>
      <ol>{passos.map(([label, url, concluido]) => <li key={url}>
        <Link to={url}>{label}</Link>
        <span className="badge">{concluido ? 'Concluído' : 'Pendente'}</span>
      </li>)}</ol>
      <p className="muted">O envio e a confirmação financeiros dependem dos serviços configurados no ambiente.</p>
    </details>
  </section>
}
