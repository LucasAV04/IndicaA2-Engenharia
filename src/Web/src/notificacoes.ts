type Contexto = { primeiroNome: string | null; agendamentoAtual: string | null; vistoriaPropria: boolean }
export type Notificacao = { id: string; tipo: number; criadaEm: string; lidaEm: string | null; contexto?: Contexto | null }

export function apresentarNotificacao(notificacao: Notificacao, admin: boolean) {
  const { tipo, contexto } = notificacao
  const indicacao = contexto?.primeiroNome ? 'A indicação de ' + contexto.primeiroNome : 'A indicação'
  const indicacoes = admin
    ? { destino: '/indicacoes', acao: 'Ver indicações' }
    : { destino: '/minha-conta#indicacoes', acao: 'Ver minhas indicações' }
  const cashbacks = admin
    ? { destino: '/cashbacks', acao: 'Ver cashbacks' }
    : { destino: '/minha-conta#cashbacks', acao: 'Ver meus cashbacks' }

  switch (tipo) {
    case 0:
      if (contexto?.vistoriaPropria && !admin) return { titulo: 'Vistoria cadastrada', descricao: 'Uma vistoria foi registrada para você.' }
      if (!contexto) return { titulo: 'Atualização de vistoria', descricao: 'Uma vistoria foi registrada ou vinculada. O registro relacionado está indisponível.' }
      return { titulo: 'Vistoria vinculada', descricao: indicacao + ' foi vinculada a uma vistoria.', ...indicacoes }
    case 1:
      return { titulo: 'Pagamento confirmado', descricao: 'O pagamento da vistoria foi confirmado.', ...(!contexto?.vistoriaPropria && contexto?.primeiroNome ? indicacoes : {}) }
    case 2:
      return { titulo: 'Dados Pix necessários', descricao: admin ? 'Faltam Dados Pix para preparar o pagamento.' : 'Cadastre seus Dados Pix para preparar o recebimento do cashback.',
        ...(admin ? cashbacks : { destino: '/minha-conta#dados-pix', acao: 'Ver meus Dados Pix' }) }
    case 3:
      return { titulo: 'Cashback disponível', descricao: 'O cashback foi disponibilizado e aguarda pagamento.', ...cashbacks }
    case 4:
      return { titulo: 'Cashback pago', descricao: 'O pagamento de cashback da indicação foi confirmado.', ...(admin || contexto?.primeiroNome ? cashbacks : {}) }
    case 5:
      return { titulo: 'Falha financeira', descricao: 'A tentativa de pagamento do cashback falhou e precisa de revisão.', ...(admin ? cashbacks : {}) }
    case 6:
      return { titulo: 'Cobrança Pix em revisão', descricao: 'Uma cobrança Pix precisa de revisão administrativa.',
        ...(admin ? { destino: '/cobrancas-pix-vistoria', acao: 'Ver cobranças Pix' } : {}) }
    default:
      return { titulo: 'Notificação', descricao: 'Os detalhes deste evento estão indisponíveis.' }
  }
}
