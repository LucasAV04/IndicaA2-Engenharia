import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api, readSession, saveSession } from './api'
import { ErrorBox } from './components'
import { money, utcDate } from './format'
import OperationForm from './OperationForm'
import NotificacoesPanel from './NotificacoesPanel'

type Perfil = { nome: string; codigo: string; link: string }
type Indicacao = { nome: string; telefoneMascarado: string; status: number; criadaEm: string }
type Cashback = { valor: number; status: number; situacaoPix: number | null; criadoEm: string }
export default function MinhaContaPage() {
  const usuario = readSession()?.usuarioId ?? ''
  const [copiado, setCopiado] = useState(false)
  const [editar, setEditar] = useState(false)
  const perfil = useQuery({ queryKey: ['portal', usuario, 'perfil'], queryFn: () => api<Perfil>('/minha-conta') })
  const indicacoes = useQuery({ queryKey: ['portal', usuario, 'indicacoes'], queryFn: () => api<Indicacao[]>('/minha-conta/indicacoes') })
  const cashbacks = useQuery({ queryKey: ['portal', usuario, 'cashbacks'], queryFn: () => api<Cashback[]>('/minha-conta/cashbacks') })
  const dados = useQuery({ queryKey: ['portal', usuario, 'dados-pix'], queryFn: () => api<{ tipoChavePix: number; chaveMascarada: string } | null>('/minha-conta/dados-pix') })
  return <main className="portal"><header className="page-heading"><h1>Minha conta</h1><button onClick={() => saveSession(null)}>Sair</button></header>
    <section className="panel"><h2>Meu link de indicação</h2>{perfil.isPending ? <p role="status">Carregando perfil…</p> : perfil.isError ? <ErrorBox retry={() => void perfil.refetch()} /> : <><p>Olá, {perfil.data.nome}</p><p>Código: {perfil.data.codigo}</p><p className="opaque-protocol">{perfil.data.link}</p><button onClick={() => { void navigator.clipboard.writeText(perfil.data.link).then(() => setCopiado(true)).catch(() => setCopiado(false)) }}>Copiar link</button>{copiado && <p role="status">Link copiado.</p>}</>}</section>
    <section className="panel"><h2>Minhas indicações</h2>{indicacoes.isPending ? <p role="status">Carregando indicações…</p> : indicacoes.isError ? <ErrorBox retry={() => void indicacoes.refetch()} /> : !indicacoes.data.length ? <p>Nenhuma indicação.</p> : <ul>{indicacoes.data.map((i, index) => <li key={index}>{i.nome} • {i.telefoneMascarado} • {['Pendente', 'Vistoria vinculada', 'Vistoria concluída', 'Cancelada', 'Cashback pago'][i.status]} • {utcDate(i.criadaEm)}</li>)}</ul>}</section>
    <section className="panel"><h2>Meus cashbacks</h2>{cashbacks.isPending ? <p role="status">Carregando cashbacks…</p> : cashbacks.isError ? <ErrorBox retry={() => void cashbacks.refetch()} /> : !cashbacks.data.length ? <p>Nenhum cashback.</p> : <ul>{cashbacks.data.map((c, index) => <li key={index}>{money(c.valor)} • {['Pendente', 'Disponível', 'Pago', 'Cancelado'][c.status]} • Pix: {c.situacaoPix === null ? 'Aguardando preparação' : ['Pendente', 'Processando', 'Concluído', 'Falhou', 'Falha definitiva', 'Cancelado'][c.situacaoPix]}</li>)}</ul>}</section>
    <section className="panel"><h2>Meus Dados Pix</h2>{dados.isPending ? <p role="status">Carregando Dados Pix…</p> : dados.isError ? <ErrorBox retry={() => void dados.refetch()} /> : <><p>{dados.data?.chaveMascarada ?? 'Cadastre seus Dados Pix para receber cashback.'}</p><button onClick={() => setEditar(true)}>{dados.data ? 'Atualizar Dados Pix' : 'Cadastrar Dados Pix'}</button></>}
      {editar && <OperationForm lists={{}} invalidateKeys={[[ 'portal', usuario, 'dados-pix' ]]} done={() => setEditar(false)} operation={{ label: 'Salvar Dados Pix', method: 'PUT', path: () => '/minha-conta/dados-pix', fields: [{ name: 'tipoChavePix', label: 'Tipo de chave', options: ['CPF', 'CNPJ', 'E-mail', 'Telefone', 'Aleatória'] }, { name: 'chavePix', label: 'Nova chave Pix', type: 'password' }] }} />}
    </section><NotificacoesPanel />
  </main>
}
