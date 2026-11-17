import type { ReactNode } from 'react'
import MarcaA2 from './MarcaA2'
import './publico-cliente.css'

export function PublicoCliente({ title, categoria, children, variante = 'conta' }: {
  title: string; categoria: string; children: ReactNode; variante?: 'conta' | 'indicacao' | 'pagamento'
}) {
  return <main id="conteudo" tabIndex={-1} className={'publico-cliente publico-' + variante}>
    <header><MarcaA2 formato="horizontal" fundo="escuro" className="marca-a2-publica" /><span>{categoria}</span></header>
    <section className="publico-superficie"><h1>{title}</h1>{children}</section>
  </main>
}
