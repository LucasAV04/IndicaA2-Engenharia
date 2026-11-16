import { useRef } from 'react'

export function useEnvioUnico() {
  const enviando = useRef(false)
  return async (enviar: () => Promise<unknown>) => {
    if (enviando.current) return
    enviando.current = true
    try {
      await enviar()
    } catch {
      // O formulário apresenta o estado de erro da mutação.
    } finally {
      enviando.current = false
    }
  }
}
