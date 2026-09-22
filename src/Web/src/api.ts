import type { Sessao } from './types'
const SESSION_KEY = 'indicaa2.session'
let memory: Sessao | null = null
export function readSession(): Sessao | null {
  try {
    const candidate = memory ?? JSON.parse(sessionStorage.getItem(SESSION_KEY) || 'null')
    if (!candidate || typeof candidate.accessToken !== 'string' || !candidate.accessToken ||
        ![1, 2].includes(candidate.tipoUsuario) || Date.parse(candidate.expiresAtUtc) <= Date.now() ||
        !Number.isFinite(Date.parse(candidate.expiresAtUtc))) {
      memory = null; sessionStorage.removeItem(SESSION_KEY); return null
    }
    return candidate
  } catch { sessionStorage.removeItem(SESSION_KEY); return null }
}
export function saveSession(session: Sessao | null) {
  memory = session
  if (session) sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
  else sessionStorage.removeItem(SESSION_KEY)
  window.dispatchEvent(new Event('session-change'))
}
export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}
// ProblemDetails é interpretado por status. Detail/errors arbitrários nunca entram na UI.
export function problemMessage(status: number): string {
  return ({ 400: 'Verifique os campos informados.', 401: 'Sessão expirada ou credenciais inválidas.',
    403: 'Você não tem permissão para esta operação.', 404: 'Registro não encontrado.',
    409: 'O registro já existe ou foi alterado.', 422: 'A operação não é permitida para os dados ou o estado atual.' } as Record<number, string>)[status]
    || 'Não foi possível concluir a operação. Tente novamente.'
}
export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const session = readSession()
  const response = await fetch('/api' + path, { ...options, headers: {
    ...(options.body ? { 'Content-Type': 'application/json' } : {}),
    ...(session ? { Authorization: 'Bearer ' + session.accessToken } : {}),
    ...options.headers,
  } })
  if (!response.ok) {
    if (response.status === 401 && path !== '/auth/login') saveSession(null)
    if (response.status === 403) window.dispatchEvent(new Event('access-denied'))
    throw new ApiError(response.status, problemMessage(response.status))
  }
  return response.status === 204 ? null as T : response.json() as Promise<T>
}
export const send = <T,>(path: string, method: string, body?: unknown) =>
  api<T>(path, { method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
