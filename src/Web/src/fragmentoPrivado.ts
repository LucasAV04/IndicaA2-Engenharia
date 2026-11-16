const rotas = new Set(['/pagar', '/redefinir-senha', '/confirmar-email'])
let inicial: { rota: string; token: string } | undefined

export function capturarFragmentoInicial() {
  const { pathname, hash } = window.location
  if (!rotas.has(pathname) || !hash) return
  inicial = { rota: pathname, token: hash.slice(1) }
  window.history.replaceState(window.history.state, '', pathname)
}

export function lerFragmentoPrivado() {
  return inicial?.rota === window.location.pathname ? inicial.token : window.location.hash.slice(1)
}

export function limparFragmentoPrivado() {
  inicial = undefined
  if (window.location.hash) window.history.replaceState(window.history.state, '', window.location.pathname)
}
