import { useQuery } from '@tanstack/react-query'
import { api, readSession } from './api'
import { ErrorBox } from './components'
import { PortalHeader, ResumoPortal, CartaoIndicacao, ListaIndicacoes, ListaCashbacks } from './PortalCliente'
import type { Perfil, Indicacao, Cashback } from './PortalCliente'
import { DadosPixPortal } from './DadosPixPortal'
import MarcaA2 from './MarcaA2'
import NotificacoesPanel from './NotificacoesPanel'
import { SegurancaConta } from './SegurancaConta'
import './portal-cliente.css'

export default function MinhaContaPage() {
  const sessao = readSession()
  const usuario = sessao?.usuarioId ?? ''
  const perfil = useQuery({ queryKey: ['portal', usuario, 'perfil'], queryFn: () => api<Perfil>('/minha-conta') })
  const indicacoes = useQuery({ queryKey: ['portal', usuario, 'indicacoes'], queryFn: () => api<Indicacao[]>('/minha-conta/indicacoes') })
  const cashbacks = useQuery({ queryKey: ['portal', usuario, 'cashbacks'], queryFn: () => api<Cashback[]>('/minha-conta/cashbacks') })

  return <div className="cliente">
    <PortalHeader nome={perfil.data?.nome ?? sessao?.nome} />
    <main id="conteudo" tabIndex={-1} className="cliente-conteudo">
      <section id="visao-geral" className="cliente-hero" tabIndex={-1} aria-labelledby="cliente-titulo">
        <p className="cliente-selo">Programa de indicações</p>
        <h1 id="cliente-titulo">{perfil.data ? 'Olá, ' + perfil.data.nome : 'Minha conta'}</h1>
        <p>Compartilhe seu link com quem precisa de uma vistoria e acompanhe as indicações por aqui. O cashback só é pago após as confirmações do programa e do Pix.</p>
        <a href="#compartilhar">Compartilhar minha indicação <span aria-hidden="true">↘</span></a>
      </section>
      <ResumoPortal indicacoes={indicacoes.data} cashbacks={cashbacks.data} />
      <section id="compartilhar" className="cliente-card cliente-compartilhar" tabIndex={-1} aria-labelledby="titulo-link">
        <h2 id="titulo-link">Meu link de indicação</h2>
        <p>Quem abrir o link poderá cadastrar uma indicação vinculada ao seu código.</p>
        {perfil.isPending ? <p role="status">Carregando perfil…</p> : perfil.isError
          ? <ErrorBox retry={() => void perfil.refetch()} /> : <CartaoIndicacao perfil={perfil.data} />}
      </section>
      <section id="indicacoes" className="cliente-card" tabIndex={-1} aria-labelledby="titulo-indicacoes">
        <h2 id="titulo-indicacoes">Minhas indicações</h2>
        <p>Acompanhe os registros associados ao seu link.</p>
        {indicacoes.isPending ? <p role="status">Carregando indicações…</p> : indicacoes.isError
          ? <ErrorBox retry={() => void indicacoes.refetch()} /> : <ListaIndicacoes itens={indicacoes.data} />}
      </section>
      <section id="cashbacks" className="cliente-card" tabIndex={-1} aria-labelledby="titulo-cashbacks">
        <h2 id="titulo-cashbacks">Meus cashbacks</h2>
        <p>Confira o valor registrado e, separadamente, a situação da ordem Pix.</p>
        {cashbacks.isPending ? <p role="status">Carregando cashbacks…</p> : cashbacks.isError
          ? <ErrorBox retry={() => void cashbacks.refetch()} /> : <ListaCashbacks itens={cashbacks.data} />}
      </section>
      <div className="cliente-servicos">
        <DadosPixPortal usuario={usuario} />
        <div id="notificacoes" tabIndex={-1}><NotificacoesPanel /></div>
      </div>
      <div id="seguranca" tabIndex={-1}><SegurancaConta /></div>
    </main>
    <footer className="cliente-rodape"><MarcaA2 formato="horizontal" fundo="claro" className="marca-a2-rodape" decorativa /><span>IndicA2</span></footer>
  </div>
}
