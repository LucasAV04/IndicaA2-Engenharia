import { useEffect, useRef, type ReactNode } from 'react'
export function ErrorBox({ message = 'Não foi possível carregar os dados.', retry }: { message?: string; retry?: () => void }) {
  return <div role="alert" className="error">{message} {retry && <button onClick={retry}>Tentar novamente</button>}</div>
}
export function Modal({ title, children, close }: { title: string; children: ReactNode; close: () => void }) {
  const ref = useRef<HTMLDialogElement>(null)
  useEffect(() => { const dialog = ref.current; dialog?.showModal(); return () => dialog?.close() }, [])
  return <dialog ref={ref} aria-label={title} onCancel={e => { e.preventDefault(); close() }}>
    <header className="modal-header"><h2>{title}</h2><button aria-label="Fechar" onClick={close}>×</button></header>{children}
  </dialog>
}
