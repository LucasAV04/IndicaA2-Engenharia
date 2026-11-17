import type { ReactNode } from 'react'
import './publico-cliente.css'

export function MarcaCliente() {
  return <div className="cliente-marca"><strong>A2</strong><span>Engenharia</span></div>
}

export function PublicoCliente({ title, categoria, children, variante = 'conta' }: {
  title: string; categoria: string; children: ReactNode; variante?: 'conta' | 'indicacao' | 'pagamento'
}) {
  return <main id="conteudo" tabIndex={-1} className={'publico-cliente publico-' + variante}>
    <header><MarcaCliente /><span>{categoria}</span></header>
    <section className="publico-superficie"><h1>{title}</h1>{children}</section>
  </main>
}
