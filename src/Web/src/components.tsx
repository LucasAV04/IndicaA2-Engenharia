import { useEffect, useRef, type ReactNode } from 'react'
export function ErrorBox({ message = 'Não foi possível carregar os dados.', retry }: { message?: string; retry?: () => void }) {
  const foco = useRef<HTMLDivElement>(null)
  useEffect(() => { foco.current?.focus() }, [message])
  return <div ref={foco} tabIndex={-1} role="alert" className="error">{message} {retry && <button onClick={retry}>Tentar novamente</button>}</div>
}
export function Modal({ title, children, close }: { title: string; children: ReactNode; close: () => void }) {
  const ref = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const dialog = ref.current
    const retorno = document.activeElement
    dialog?.showModal()
    dialog?.querySelector<HTMLElement>('input, select, button')?.focus()
    return () => {
      dialog?.close()
      if (retorno instanceof HTMLElement && retorno.isConnected) retorno.focus()
    }
  }, [])
  return <dialog ref={ref} aria-label={title} onCancel={e => { e.preventDefault(); close() }}>
    <header className="modal-header"><h2>{title}</h2><button aria-label="Fechar" onClick={close}>×</button></header>{children}
  </dialog>
}
