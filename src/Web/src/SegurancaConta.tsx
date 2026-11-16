import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, saveSession, send } from './api'

type Modo = 'solicitar' | 'redefinir' | 'confirmar'
const titulos = { solicitar: 'Esqueci minha senha', redefinir: 'Redefinir senha', confirmar: 'Confirmar e-mail' }

export function ContaPublica({ modo }: { modo: Modo }) {
  const [token, setToken] = useState(() => modo === 'solicitar' ? '' : window.location.hash.slice(1))
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const result = useRef<HTMLParagraphElement>(null)
  const navigate = useNavigate()

  useLayoutEffect(() => {
    if (modo !== 'solicitar' && window.location.hash)
      window.history.replaceState(window.history.state, '', window.location.pathname + window.location.search)
  }, [modo])
  useEffect(() => { if (message) result.current?.focus() }, [message])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setBusy(true)
    setMessage('')
    try {
      if (modo === 'solicitar') {
        await send('/auth/esqueci-senha', 'POST', { email: form.get('email') })
        setMessage('Se o endereço puder receber instruções, enviaremos um e-mail.')
      } else {
        await api('/auth/' + (modo === 'redefinir' ? 'redefinir-senha' : 'confirmar-email'), {
          method: 'POST', headers: { 'X-Conta-Token': token },
          ...(modo === 'redefinir' ? { body: JSON.stringify({ senha: form.get('senha') }) } : {}),
        })
        setToken('')
        if (modo === 'redefinir') {
          navigate('/login', { replace: true })
          saveSession(null, true)
        } else setMessage('E-mail confirmado.')
      }
    } catch {
      setMessage(modo === 'solicitar' ? 'Não foi possível enviar a solicitação agora.'
        : 'Não foi possível concluir. Confira os dados ou solicite outro link.')
    } finally { setBusy(false) }
  }

  const disponivel = modo === 'solicitar' || /^[0-9a-f]{64}$/.test(token)
  return <main className="portal"><section className="panel">
    <h1>{titulos[modo]}</h1>
    {disponivel && <form onSubmit={evento => void submit(evento)}>
      {modo === 'solicitar' && <div className="field"><label htmlFor="conta-email">E-mail</label>
        <input name="email" id="conta-email" type="email" autoComplete="email" maxLength={254} required /></div>}
      {modo === 'redefinir' && <div className="field"><label htmlFor="conta-senha">Nova senha</label>
        <input name="senha" id="conta-senha" type="password" autoComplete="new-password" minLength={8} maxLength={72} required /></div>}
      <button type="submit" disabled={busy}>{modo === 'solicitar' ? 'Solicitar instruções' : titulos[modo]}</button>
    </form>}
    <p ref={result} tabIndex={-1} aria-live="polite">{message || (!disponivel && 'Link inválido ou indisponível.')}</p>
    <Link to="/login">Voltar ao login</Link>
  </section></main>
}

export function SegurancaConta() {
  const navigate = useNavigate()
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [cooldown, setCooldown] = useState(false)
  const result = useRef<HTMLParagraphElement>(null)
  const perfil = useQuery({ queryKey: ['seguranca-conta'], queryFn: () =>
    api<{ emailConfirmado: boolean; emailHabilitado: boolean }>('/minha-conta/seguranca') })

  useEffect(() => { if (message) result.current?.focus() }, [message])
  useEffect(() => {
    if (!cooldown) return
    const timer = window.setTimeout(() => setCooldown(false), 60_000)
    return () => window.clearTimeout(timer)
  }, [cooldown])

  async function alterar(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setBusy(true)
    setMessage('')
    try {
      await send('/minha-conta/senha', 'PATCH', { senhaAtual: form.get('atual'), novaSenha: form.get('nova') })
      navigate('/login', { replace: true })
      saveSession(null, true)
    } catch { setMessage('Não foi possível alterar a senha. Confira os dados.') }
    finally { setBusy(false) }
  }

  async function confirmar() {
    setCooldown(true)
    try {
      await send('/minha-conta/email/confirmacao', 'POST')
      setMessage('Solicitação registrada. Confira sua caixa de entrada.')
    } catch { setMessage('Não foi possível solicitar a confirmação agora.'); setCooldown(false) }
  }

  return <section className="panel">
    <h2>Segurança</h2>
    {perfil.data && <>
      <p>{perfil.data.emailConfirmado ? 'E-mail confirmado' : 'E-mail ainda não confirmado'}</p>
      {!perfil.data.emailConfirmado && perfil.data.emailHabilitado &&
        <button disabled={cooldown} onClick={() => void confirmar()}>{cooldown ? 'Aguarde um minuto' : 'Enviar confirmação'}</button>}
    </>}
    <form onSubmit={evento => void alterar(evento)}>
      <div className="field"><label htmlFor="senha-atual">Senha atual</label>
        <input name="atual" id="senha-atual" type="password" autoComplete="current-password" required /></div>
      <div className="field"><label htmlFor="senha-nova">Nova senha</label>
        <input name="nova" id="senha-nova" type="password" autoComplete="new-password" minLength={8} maxLength={72} required /></div>
      <button type="submit" disabled={busy}>Alterar senha</button>
    </form>
    <p ref={result} tabIndex={-1} aria-live="polite">{message}</p>
  </section>
}
