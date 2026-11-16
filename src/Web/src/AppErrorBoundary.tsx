import { Component, type ReactNode } from 'react'

export default class AppErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    if (!this.state.failed) return this.props.children
    return <main className="portal">
      <section className="panel" role="alert">
        <h1>Não foi possível exibir esta tela</h1>
        <p>Reabra a página para continuar.</p>
        <p><a href="/">Voltar ao início</a></p>
        <button onClick={() => window.location.reload()}>Recarregar página</button>
      </section>
    </main>
  }
}
