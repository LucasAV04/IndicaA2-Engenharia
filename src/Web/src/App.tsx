import { useEffect, useState } from 'react'
import { Navigate, NavLink, Outlet, Route, Routes, useNavigate } from 'react-router-dom'
import { useIsFetching, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { api, readSession, saveSession } from './api'
import type { Sessao } from './types'
import { resources, type ResourceKey } from './resources'
import ResourcePage from './ResourcePage'
import DashboardPage from './DashboardPage'
import { ErrorBox } from './components'

export default function App() {
  const [session, setSession] = useState(readSession)
  const navigate = useNavigate()
  const client = useQueryClient()
  useEffect(() => {
    const update = () => { client.clear(); setSession(readSession()) }
    const denied = () => navigate('/acesso-negado')
    window.addEventListener('session-change', update); window.addEventListener('access-denied', denied)
    return () => { window.removeEventListener('session-change', update); window.removeEventListener('access-denied', denied) }
  }, [client, navigate])
  useEffect(() => {
    if (!session) return
    const timer = window.setTimeout(() => saveSession(null), Math.min(Date.parse(session.expiresAtUtc) - Date.now(), 2147483647))
    return () => window.clearTimeout(timer)
  }, [session])
  return <Routes>
    <Route path="/login" element={session ? <Navigate to={session.tipoUsuario === 2 ? '/' : '/acesso-negado'} replace /> : <Login />} />
    <Route path="/acesso-negado" element={session ? <div className="access-denied"><h1>Acesso negado</h1><p>Este painel é exclusivo de administradores.</p><button onClick={() => saveSession(null)}>Sair</button></div> : <Navigate to="/login" replace />} />
    <Route element={!session ? <Navigate to="/login" replace /> : session.tipoUsuario !== 2 ? <Navigate to="/acesso-negado" replace /> : <Layout session={session} />}>
      <Route index element={<DashboardPage />} />
      {(Object.keys(resources) as ResourceKey[]).map(key => <Route key={key} path={'/' + key} element={<ResourcePage key={key} resourceKey={key} />} />)}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Route>
  </Routes>
}
function Layout({ session }: { session: Sessao }) {
  const [open, setOpen] = useState(false)
  const fetching = useIsFetching()
  return <div className="shell"><aside className={open ? 'sidebar open' : 'sidebar'}>
    <div className="brand">IndicA2<span>ADMINISTRAÇÃO</span></div><nav aria-label="Principal"><NavLink to="/" end onClick={() => setOpen(false)}>Visão geral</NavLink>{Object.entries(resources).map(([key, value]) => <NavLink key={key} to={'/' + key} onClick={() => setOpen(false)}>{value.title}</NavLink>)}</nav>
    <div className="sidebar-note">Painel operacional<br />A2 Engenharia & Diagnóstico</div></aside>
    <div className="workspace"><header className="topbar"><button className="menu" aria-expanded={open} aria-label="Alternar navegação" onClick={() => setOpen(!open)}>☰ Menu</button><span className="muted">Área administrativa</span><div className="identity"><strong>{session.nome}</strong><small>{session.email}</small></div><button onClick={() => saveSession(null)}>Sair</button></header>
    {fetching > 0 && <div role="status" className="global-loading" aria-label="Atualizando dados" />}
    <main><Outlet /></main><footer>IndicA2 • Gestão de indicações e vistorias</footer></div></div>
}
const loginSchema = z.object({ email: z.string().email('Informe um e-mail válido.'), senha: z.string().min(1, 'Informe sua senha.') })
function Login() {
  const [error, setError] = useState(false)
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof loginSchema>>({ resolver: zodResolver(loginSchema) })
  async function login(values: z.infer<typeof loginSchema>) {
    setError(false)
    try { saveSession(await api<Sessao>('/auth/login', { method: 'POST', body: JSON.stringify(values) })) }
    catch { setError(true) }
  }
  return <div className="login"><section className="login-intro"><div className="brand">IndicA2</div><p className="eyebrow">Gestão operacional</p><h1>Indicações conectadas.<br />Operação organizada.</h1><p>Um espaço para acompanhar clientes, vistorias e pagamentos com clareza.</p></section><section className="login-form"><h2>Acesse a administração</h2><p>Use sua conta de administrador.</p><form onSubmit={handleSubmit(login)}>
    <div className="field"><label htmlFor="email">E-mail</label><input id="email" type="email" autoComplete="username" {...register('email')} />{errors.email && <span role="alert">{errors.email.message}</span>}</div>
    <div className="field"><label htmlFor="senha">Senha</label><input id="senha" type="password" autoComplete="current-password" {...register('senha')} />{errors.senha && <span role="alert">{errors.senha.message}</span>}</div>
    {error && <ErrorBox message="Não foi possível entrar. Confira suas credenciais e tente novamente." />}
    <button className="primary" disabled={isSubmitting}>{isSubmitting ? 'Entrando…' : 'Entrar'}</button>
  </form><p className="muted">A sessão é encerrada ao sair ou expirar.</p></section></div>
}
