import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { Link, Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { useIsFetching, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { api, readSession, saveSession } from './api'
import type { Sessao } from './types'
import { resources, type ResourceKey } from './resources'
import { ErrorBox } from './components'
import MarcaA2 from './MarcaA2'
import './login.css'

const ResourcePage = lazy(() => import('./ResourcePage'))
const DashboardPage = lazy(() => import('./DashboardPage'))
const RelatoriosPage = lazy(() => import('./RelatoriosPage'))
const PrecosPage = lazy(() => import('./PrecosPage'))
const TiposPlantaPage = lazy(() => import('./TiposPlantaPage'))
const PagarPage = lazy(() => import('./PagarPage'))
const CobrancasPage = lazy(() => import('./CobrancasPage'))
const IndicacaoPublicaPage = lazy(() => import('./IndicacaoPublicaPage'))
const MinhaContaPage = lazy(() => import('./MinhaContaPage'))
const NotificacoesPanel = lazy(() => import('./NotificacoesPanel'))
const ContaPublica = lazy(() => import('./SegurancaConta').then(m => ({ default: m.ContaPublica })))

const loading = <p className="notice" role="status">Carregando tela…</p>

export default function App() {
  const [session, setSession] = useState(readSession)
  const [senhaAlterada, setSenhaAlterada] = useState(false)
  const navigate = useNavigate()
  const client = useQueryClient()
  useEffect(() => {
    const update = (event: Event) => {
      client.clear(); setSession(readSession())
      if (event instanceof CustomEvent) setSenhaAlterada(event.detail?.senhaAlterada === true)
    }
    const denied = () => navigate('/acesso-negado')
    window.addEventListener('session-change', update); window.addEventListener('access-denied', denied)
    return () => { window.removeEventListener('session-change', update); window.removeEventListener('access-denied', denied) }
  }, [client, navigate])
  useEffect(() => {
    if (!session) return
    const timer = window.setTimeout(() => saveSession(null), Math.min(Date.parse(session.expiresAtUtc) - Date.now(), 2147483647))
    return () => window.clearTimeout(timer)
  }, [session])
  return <><a className="skip-link" href="#conteudo" onClick={event => {
    event.preventDefault(); document.querySelector<HTMLElement>('main')?.focus()
  }}>Pular para o conteúdo</a><Suspense fallback={loading}><Routes>
    <Route path="/esqueci-senha" element={<ContaPublica key="solicitar" modo="solicitar" />} />
    <Route path="/redefinir-senha" element={<ContaPublica key="redefinir" modo="redefinir" />} />
    <Route path="/confirmar-email" element={<ContaPublica key="confirmar" modo="confirmar" />} />
    <Route path="/pagar" element={<PagarPage />} />
    <Route path="/indicar/:codigo" element={<IndicacaoPublicaPage />} />
    <Route path="/minha-conta" element={session ? <MinhaContaPage key={session.usuarioId} /> : <Navigate to="/login" replace />} />
    <Route path="/login" element={session ? <Navigate to={session.tipoUsuario === 2 ? '/' : '/minha-conta'} replace /> : <Login senhaAlterada={senhaAlterada} /> } />
    <Route path="/acesso-negado" element={session ? <div className="access-denied"><MarcaA2 formato="horizontal" fundo="claro" /><h1>Acesso negado</h1><p>Este painel é exclusivo de administradores.</p><button onClick={() => saveSession(null)}>Sair</button></div> : <Navigate to="/login" replace />} />
    <Route element={!session ? <Navigate to="/login" replace /> : session.tipoUsuario !== 2 ? <Navigate to="/minha-conta" replace /> : <Layout session={session} />}>
      <Route index element={<DashboardPage />} />
      <Route path="/relatorios" element={<RelatoriosPage />} />
      <Route path="/tipos-planta" element={<TiposPlantaPage />} />
      <Route path="/precos-vistoria" element={<PrecosPage />} />
      <Route path="/cobrancas-pix-vistoria" element={<CobrancasPage />} />
      <Route path="/alertas" element={<NotificacoesPanel admin />} />
      {(Object.keys(resources) as ResourceKey[]).map(key => <Route key={key} path={'/' + key} element={<ResourcePage key={key} resourceKey={key} />} />)}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Route>
  </Routes></Suspense></>
}
function Layout({ session }: { session: Sessao }) {
  const [open, setOpen] = useState(false)
  const fetching = useIsFetching()
  const { pathname } = useLocation()
  const main = useRef<HTMLElement>(null)
  const toggle = useRef<HTMLButtonElement>(null)
  const previousPath = useRef(pathname)
  useEffect(() => {
    if (previousPath.current !== pathname) main.current?.focus()
    previousPath.current = pathname
  }, [pathname])
  return <div className="shell"><aside id="menu-principal" onKeyDown={e => { if (e.key === 'Escape') { setOpen(false); toggle.current?.focus() } }} className={open ? 'sidebar open' : 'sidebar'}>
    <div className="admin-identidade"><MarcaA2 formato="vertical" fundo="escuro" compacto="(max-width: 780px)" className="marca-a2-admin" /><span>Administração</span></div><nav aria-label="Principal"><NavLink to="/" end onClick={() => setOpen(false)}>Visão geral</NavLink>{Object.entries(resources).map(([key, value]) => <NavLink key={key} to={'/' + key} onClick={() => setOpen(false)}>{value.title}</NavLink>)}</nav>
    <nav aria-label="Configuração de preços"><NavLink to="/tipos-planta" onClick={() => setOpen(false)}>Tipos de planta</NavLink><NavLink to="/precos-vistoria" onClick={() => setOpen(false)}>Tabela de preços</NavLink></nav>
    <nav aria-label="Recebimentos"><NavLink to="/cobrancas-pix-vistoria" onClick={() => setOpen(false)}>Cobranças Pix de vistoria</NavLink></nav>
    <nav aria-label="Jornada"><NavLink to="/alertas" onClick={() => setOpen(false)}>Alertas internos</NavLink><NavLink to="/relatorios" onClick={() => setOpen(false)}>Relatórios</NavLink></nav>
    <div className="sidebar-note">Painel operacional</div></aside>
    <div className="workspace"><header className="topbar"><button ref={toggle} className="menu" aria-controls="menu-principal" aria-expanded={open} aria-label="Alternar navegação" onClick={() => setOpen(!open)}>☰ Menu</button><span className="muted">Área administrativa</span><div className="identity"><strong>{session.nome}</strong><small>{session.email}</small></div><button onClick={() => saveSession(null)}>Sair</button></header>
    {fetching > 0 && <div role="status" className="global-loading" aria-label="Atualizando dados" />}
    <main id="conteudo" tabIndex={-1} ref={main}><Suspense fallback={loading}><Outlet /></Suspense></main><footer>IndicA2 • Gestão de indicações e vistorias</footer></div></div>
}
const loginSchema = z.object({ email: z.string().email('Informe um e-mail válido.'), senha: z.string().min(1, 'Informe sua senha.') })
function Login({ senhaAlterada }: { senhaAlterada: boolean }) {
  const [error, setError] = useState(false)
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof loginSchema>>({ resolver: zodResolver(loginSchema) })
  async function login(values: z.infer<typeof loginSchema>) {
    setError(false)
    try { saveSession(await api<Sessao>('/auth/login', { method: 'POST', body: JSON.stringify(values) })) }
    catch { setError(true) }
  }
  return <main id="conteudo" tabIndex={-1} className="login-page"><div className="login-card"><section className="login-intro"><MarcaA2 formato="horizontal" fundo="escuro" className="marca-a2-login" /><h1>Suas indicações em um só lugar</h1><p>Acompanhe indicações, cashbacks e os dados da sua conta no IndicA2.</p></section><section className="login-form"><h2>Acesse sua conta</h2><p>Entre para acompanhar suas indicações ou administrar a operação.</p><form onSubmit={handleSubmit(login)}>
    <div className="field"><label htmlFor="email">E-mail</label><input id="email" type="email" autoComplete="username" aria-invalid={!!errors.email} aria-describedby={errors.email ? 'login-email-erro' : undefined} {...register('email')} />{errors.email && <span id="login-email-erro" role="alert">{errors.email.message}</span>}</div>
    <div className="field"><label htmlFor="senha">Senha</label><input id="senha" type="password" autoComplete="current-password" aria-invalid={!!errors.senha} aria-describedby={errors.senha ? 'login-senha-erro' : undefined} {...register('senha')} />{errors.senha && <span id="login-senha-erro" role="alert">{errors.senha.message}</span>}</div>
    {error && <ErrorBox message="Não foi possível entrar. Confira suas credenciais e tente novamente." />}
    <button className="primary" disabled={isSubmitting}>{isSubmitting ? 'Entrando…' : 'Entrar'}</button>
  </form><p><Link to="/esqueci-senha">Esqueci minha senha</Link></p>{senhaAlterada && <p role="status">Senha alterada. Entre novamente.</p>}<p className="muted">A sessão é encerrada ao sair ou expirar.</p></section></div></main>
}
