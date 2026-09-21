export type Registro = {
  id: string; status: number; createdAt: string; updatedAt: string;
  nome?: string; email?: string; telefone?: string; codigoIndicacao?: string;
  tipoUsuario?: number; usuarioId?: string; usuarioIndicadorId?: string;
  usuarioIndicadoId?: string; usuarioBeneficiarioId?: string; nomeIndicada?: string;
  telefoneIndicada?: string; codigoIndicacaoUsado?: string; vistoriaId?: string;
  tipoPlanta?: string; areaM2?: number; pacote?: number; dataAgendada?: string;
  valor?: number; pagoEm?: string; pagamentoVistoriaId?: string; valorTotalPago?: number;
  percentual?: number; cashbackId?: string; tipoChavePix?: number; quantidadeTentativas?: number;
}
export type Sessao = { accessToken: string; expiresAtUtc: string; usuarioId: string; nome: string; email: string; tipoUsuario: number }
export type DadosPix = { id: string; usuarioId: string; tipoChavePix: number; chaveMascarada: string; createdAt: string; updatedAt: string }
export type Dashboard = {
  totalUsuarios: number; usuariosAtivos: number; receitaConfirmada: number;
  pagamentosPendentes: number; cashbackDisponivel: number; cashbackPago: number;
  pixPendenteProcessando: number; pixConcluido: number; falhasPix: number; falhasDefinitivasPix: number;
  calculadoEmUtc: string; indicacoes: Record<string, number>; vistorias: Record<string, number>;
  pagamentosVistoria: Record<string, number>; cashbacks: Record<string, number>; pagamentosPix: Record<string, number>;
}
