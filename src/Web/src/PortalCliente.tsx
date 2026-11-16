import { useEffect, useRef, useState } from 'react'
import { saveSession } from './api'
import { money, utcDate } from './format'
import { MarcaCliente } from './PublicoCliente'

export type Perfil = { nome: string; codigo: string; link: string }
export type Indicacao = { nome: string; telefoneMascarado: string; status: number; criadaEm: string }
export type Cashback = { valor: number; status: number; situacaoPix: number | null; criadoEm?: string }

const secoes = [
  ['visao-geral', 'Visão geral'], ['indicacoes', 'Indicações'], ['cashbacks', 'Cashbacks'],
  ['dados-pix', 'Dados Pix'], ['notificacoes', 'Notificações'], ['seguranca', 'Segurança'],
]

export function PortalHeader({ nome }: { nome?: string }) {
  const [aberto, setAberto] = useState(false)
  const [ativo, setAtivo] = useState(() => window.location.hash.slice(1) || 'visao-geral')
  const botao = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    const atualizar = () => setAtivo(window.location.hash.slice(1) || 'visao-geral')
    const secao = window.location.hash.slice(1)
    if (secao) document.getElementById(secao)?.scrollIntoView?.()
    window.addEventListener('hashchange', atualizar)
    return () => window.removeEventListener('hashchange', atualizar)
  }, [])

  return <header className="cliente-header" onKeyDown={event => {
    if (event.key === 'Escape' && aberto) {
      setAberto(false)
      botao.current?.focus()
    }
  }}>
    <div className="cliente-header-interior">
      <div className="cliente-identidade"><MarcaCliente /><span>Área da cliente</span></div>
      <div className="cliente-acoes">
        {nome && <strong>{nome}</strong>}
        <button type="button" ref={botao} className="cliente-menu" aria-expanded={aberto} aria-controls="cliente-navegacao" onClick={() => setAberto(!aberto)}>Menu</button>
        <button type="button" onClick={() => saveSession(null)}>Sair</button>
      </div>
      <nav aria-label="Navegação da cliente" id="cliente-navegacao" className={aberto ? 'cliente-nav aberta' : 'cliente-nav'}>
        {secoes.map(([id, texto]) => <a key={id} href={'#' + id} aria-current={ativo === id ? 'location' : undefined} onClick={() => {
          setAberto(false)
          setAtivo(id)
          document.getElementById(id)?.focus()
        }}>{texto}</a>)}
      </nav>
    </div>
  </header>
}

type Estado = readonly [rotulo: string, texto: string, tom: 'andamento' | 'confirmado' | 'atencao' | 'neutro']
const desconhecido: Estado = ['Estado não reconhecido', 'Acompanhe as atualizações por aqui.', 'neutro']
const indicacao: readonly Estado[] = [
  ['Cadastrada', 'A indicação foi registrada e aguarda vínculo com uma vistoria.', 'andamento'],
  ['Vistoria vinculada', 'Uma vistoria foi vinculada. A conclusão ainda não foi registrada.', 'andamento'],
  ['Vistoria concluída', 'A vistoria foi concluída. Consulte Meus cashbacks para ver se há um valor registrado.', 'confirmado'],
  ['Cancelada', 'Esta indicação foi cancelada.', 'atencao'],
  ['Cashback pago', 'O pagamento de cashback desta indicação foi confirmado.', 'confirmado'],
]
const cashback: readonly Estado[] = [
  ['Pendente', 'O valor foi calculado e ainda está pendente.', 'andamento'],
  ['Disponível', 'O valor está disponível para pagamento. Acompanhe a situação da ordem Pix.', 'andamento'],
  ['Pago', 'O cashback consta como pago.', 'confirmado'],
  ['Cancelado', 'O cashback foi cancelado.', 'atencao'],
]
const pix: readonly Estado[] = [
  ['Em preparação', 'A ordem foi registrada e ainda não foi concluída.', 'andamento'],
  ['Em processamento', 'O Pix está em processamento. Aguarde a confirmação.', 'andamento'],
  ['Concluído', 'A ordem Pix consta como concluída.', 'confirmado'],
  ['Falhou', 'O Pix não foi confirmado. Confira seus Dados Pix e acompanhe os avisos.', 'atencao'],
  ['Falha definitiva', 'O Pix não foi concluído. Acompanhe as notificações sobre este registro.', 'atencao'],
  ['Cancelado', 'A ordem Pix foi cancelada.', 'atencao'],
]
const semOrdem: Estado = ['Sem ordem Pix', 'Nenhuma ordem Pix consta para este cashback.', 'neutro']

function Badge({ estado }: { estado: Estado }) {
  return <span className={'cliente-badge ' + estado[2]}>{estado[0]}</span>
}

export function ResumoPortal({ indicacoes, cashbacks }: { indicacoes?: Indicacao[]; cashbacks?: Cashback[] }) {
  const valores = cashbacks?.reduce((totais, item) => {
    // Use centavos para somar valores decimais já persistidos.
    const centavos = Math.round(item.valor * 100)
    if (item.status === 0) totais.pendente += centavos
    if (item.status === 1) totais.disponivel += centavos
    if (item.status === 2) totais.pago += centavos
    return totais
  }, { pendente: 0, disponivel: 0, pago: 0 })
  const metricas = [
    ['Indicações registradas', indicacoes?.length],
    ['Valor pendente', valores && money(valores.pendente / 100)],
    ['Valor disponível', valores && money(valores.disponivel / 100)],
    ['Valor pago', valores && money(valores.pago / 100)],
  ]
  return <section aria-labelledby="titulo-resumo" className="cliente-resumo">
    <h2 id="titulo-resumo">Seu resumo</h2>
    <dl>{metricas.map(([titulo, valor]) => <div key={titulo}>
      <dt>{titulo}</dt><dd>{valor === undefined ? <><span aria-hidden="true">—</span><span className="sr-only">Sem dados no momento</span></> : valor}</dd>
    </div>)}</dl>
  </section>
}

export function CartaoIndicacao({ perfil }: { perfil: Perfil }) {
  const link = useRef<HTMLInputElement>(null)
  const [mensagem, setMensagem] = useState('')
  async function copiar() {
    setMensagem('')
    try {
      await navigator.clipboard.writeText(perfil.link)
      setMensagem('Link copiado.')
    } catch {
      link.current?.focus()
      link.current?.select()
      setMensagem('Link selecionado. Use a opção Copiar do seu dispositivo.')
    }
  }
  async function compartilhar() {
    setMensagem('')
    try {
      await navigator.share({ title: 'Indicação · A2 Engenharia', url: perfil.link })
      setMensagem('Link compartilhado.')
    } catch (error) {
      if (!(error instanceof DOMException && error.name === 'AbortError')) void copiar()
    }
  }
  return <>
    <p className="cliente-codigo">Código: <strong>{perfil.codigo}</strong></p>
    <label htmlFor="cliente-link">Link público de indicação</label>
    <input id="cliente-link" ref={link} readOnly value={perfil.link} onFocus={event => event.currentTarget.select()} />
    <div className="cliente-acoes">
      <button type="button" className="primary" onClick={() => void copiar()}>Copiar link</button>
      {typeof navigator.share === 'function' && <button type="button" onClick={() => void compartilhar()}>Compartilhar</button>}
    </div>
    <p aria-live="polite" className="cliente-feedback">{mensagem}</p>
  </>
}

export function ListaIndicacoes({ itens }: { itens: Indicacao[] }) {
  if (!itens.length) return <div className="cliente-vazio">
    <strong>Nenhuma indicação por enquanto.</strong>
    <p>Quando alguém cadastrar uma indicação pelo seu link, ela aparecerá aqui.</p>
    <a href="#compartilhar">Ir para meu link</a>
  </div>
  return <ul className="cliente-registros">{itens.map((item, index) => {
    const estado = indicacao[item.status] ?? desconhecido
    return <li key={index}>
      <div className="cliente-linha"><strong>{item.nome}</strong><Badge estado={estado} /></div>
      <p><span aria-label="Telefone mascarado">{item.telefoneMascarado}</span> · <time dateTime={item.criadaEm}>{utcDate(item.criadaEm)}</time></p>
      <p>{estado[1]}</p>
    </li>
  })}</ul>
}

export function ListaCashbacks({ itens }: { itens: Cashback[] }) {
  if (!itens.length) return <div className="cliente-vazio">
    <strong>Nenhum cashback registrado.</strong>
    <p>Quando houver um registro associado às suas indicações, você verá aqui o valor e a situação do pagamento.</p>
  </div>
  return <ul className="cliente-registros">{itens.map((item, index) => {
    const estado = cashback[item.status] ?? desconhecido
    const ordem = item.situacaoPix === null ? semOrdem : pix[item.situacaoPix] ?? desconhecido
    return <li key={index}>
      <div className="cliente-linha"><strong>{money(item.valor)}</strong>{item.criadoEm && <time dateTime={item.criadoEm}>{utcDate(item.criadoEm)}</time>}</div>
      <dl className="cliente-estados">
        <div><dt>Cashback</dt><dd><Badge estado={estado} /><p>{estado[1]}</p></dd></div>
        <div><dt>Ordem Pix</dt><dd><Badge estado={ordem} /><p>{ordem[1]}</p></dd></div>
      </dl>
    </li>
  })}</ul>
}
