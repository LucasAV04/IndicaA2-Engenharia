using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities
{
    public class Indicacao : BaseEntity
    {
        public Guid UsuarioIndicadorId { get; private set; }

        public Guid? UsuarioIndicadoId { get; private set; }

        public string NomeIndicada { get; private set; } = string.Empty;

        public string TelefoneIndicada { get; private set; } = string.Empty;

        public string CodigoIndicacaoUsado { get; private set; } = string.Empty;

        public Guid? VistoriaId { get; private set; }

        public StatusIndicacao Status { get; private set; }

        protected Indicacao()
        {
        }

        public Indicacao(
            Guid usuarioIndicadorId,
            string nomeIndicada,
            string telefoneIndicada,
            string codigoIndicacaoUsado)
        {
            if (usuarioIndicadorId == Guid.Empty)
                throw new DomainException("O usuário indicador é obrigatório.");
            if (string.IsNullOrWhiteSpace(nomeIndicada))
                throw new DomainException("O nome da pessoa indicada é obrigatório.");
            if (string.IsNullOrWhiteSpace(telefoneIndicada))
                throw new DomainException("O telefone da pessoa indicada é obrigatório.");
            if (string.IsNullOrWhiteSpace(codigoIndicacaoUsado))
                throw new DomainException("O código de indicação usado é obrigatório.");

            UsuarioIndicadorId = usuarioIndicadorId;
            NomeIndicada = nomeIndicada.Trim();
            TelefoneIndicada = telefoneIndicada.Trim();
            CodigoIndicacaoUsado = codigoIndicacaoUsado.Trim().ToUpperInvariant();
            Status = StatusIndicacao.Pendente;
        }

        internal static Indicacao Reidratar(
            Guid id,
            Guid usuarioIndicadorId,
            Guid? usuarioIndicadoId,
            string nomeIndicada,
            string telefoneIndicada,
            string codigoIndicacaoUsado,
            Guid? vistoriaId,
            StatusIndicacao status,
            DateTime createdAt,
            DateTime updatedAt)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("O identificador persistido é obrigatório.", nameof(id));
            if (usuarioIndicadorId == Guid.Empty)
                throw new ArgumentException("O usuário indicador persistido é obrigatório.", nameof(usuarioIndicadorId));
            if (usuarioIndicadoId == Guid.Empty)
                throw new ArgumentException("O usuário indicado persistido é inválido.", nameof(usuarioIndicadoId));
            if (vistoriaId == Guid.Empty)
                throw new ArgumentException("A vistoria persistida é inválida.", nameof(vistoriaId));
            if (usuarioIndicadoId == usuarioIndicadorId)
                throw new ArgumentException("Uma indicação persistida não pode conter autoindicação.", nameof(usuarioIndicadoId));
            if (string.IsNullOrWhiteSpace(nomeIndicada))
                throw new ArgumentException("O nome persistido é obrigatório.", nameof(nomeIndicada));
            if (string.IsNullOrWhiteSpace(telefoneIndicada))
                throw new ArgumentException("O telefone persistido é obrigatório.", nameof(telefoneIndicada));
            if (string.IsNullOrWhiteSpace(codigoIndicacaoUsado))
                throw new ArgumentException("O código persistido é obrigatório.", nameof(codigoIndicacaoUsado));
            if (!Enum.IsDefined(status))
                throw new ArgumentOutOfRangeException(nameof(status), "O status persistido é inválido.");
            if (status is (StatusIndicacao.VistoriaVinculada or StatusIndicacao.VistoriaConcluida) && vistoriaId is null)
                throw new ArgumentException("O status de vistoria exige uma vistoria vinculada.", nameof(vistoriaId));
            if (status is StatusIndicacao.Pendente && vistoriaId is not null)
                throw new ArgumentException("Uma indicação pendente não pode possuir vistoria vinculada.", nameof(vistoriaId));
            if (updatedAt < createdAt)
                throw new ArgumentException("A data de atualização não pode ser anterior à data de criação.", nameof(updatedAt));

            return new Indicacao
            {
                Id = id,
                UsuarioIndicadorId = usuarioIndicadorId,
                UsuarioIndicadoId = usuarioIndicadoId,
                NomeIndicada = nomeIndicada,
                TelefoneIndicada = telefoneIndicada,
                CodigoIndicacaoUsado = codigoIndicacaoUsado,
                VistoriaId = vistoriaId,
                Status = status,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            };
        }

        public void VincularUsuarioIndicado(Guid usuarioIndicadoId)
        {
            if (usuarioIndicadoId == Guid.Empty)
                throw new DomainException("O usuário indicado é obrigatório.");
            if (usuarioIndicadoId == UsuarioIndicadorId)
                throw new DomainException("Um usuário não pode indicar a si mesmo.");
            if (UsuarioIndicadoId is not null)
                throw new DomainException("Esta indicação já possui um usuário indicado vinculado.");

            UsuarioIndicadoId = usuarioIndicadoId;
            AtualizarDataAlteracao();
        }

        public void VincularVistoria(Guid vistoriaId)
        {
            if (vistoriaId == Guid.Empty)
                throw new DomainException("A vistoria é obrigatória.");
            if (Status is StatusIndicacao.Cancelada)
                throw new DomainException("Não é possível vincular uma vistoria a uma indicação cancelada.");
            if (VistoriaId is not null)
                throw new DomainException("Esta indicação já possui uma vistoria vinculada.");

            VistoriaId = vistoriaId;
            Status = StatusIndicacao.VistoriaVinculada;
            AtualizarDataAlteracao();
        }

        public void MarcarVistoriaConcluida()
        {
            GarantirTransicao(StatusIndicacao.VistoriaVinculada, "marcar a vistoria como concluída");
            Status = StatusIndicacao.VistoriaConcluida;
            AtualizarDataAlteracao();
        }

        public void Cancelar()
        {
            if (Status is StatusIndicacao.VistoriaConcluida)
                throw new DomainException("Não é possível cancelar uma indicação com vistoria concluída.");
            if (Status is StatusIndicacao.Cancelada)
                return;

            Status = StatusIndicacao.Cancelada;
            AtualizarDataAlteracao();
        }

        private void GarantirTransicao(StatusIndicacao statusEsperado, string acao)
        {
            if (Status != statusEsperado)
            {
                throw new DomainException(
                    $"Não é possível {acao}: status atual é '{Status}', esperado '{statusEsperado}'.");
            }
        }
    }
}
