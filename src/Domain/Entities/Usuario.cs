using Domain.Enums;
using Domain.Exceptions;

using Domain.Exceptions.Email;
using Domain.Exceptions.Senha;

namespace Domain.Entities
{
    public class Usuario:BaseEntity
    {
        public string Nome { get; private set; }

        public string Email { get; private set; }

        public string SenhaHash { get; private set; }

        public string? Telefone { get; private set; }

        public StatusUsuario Status { get; private set; }

        public TipoUsuario TipoUsuario { get; private set; }

        public bool EmailConfirmado { get; private set; }

        public DateTime? UltimoLogin { get; private set; }

        protected Usuario()
        {
        }

        public Usuario(string nome ,string email,string senhaHash,string? telefone = null,TipoUsuario tipoUsuario = TipoUsuario.Usuario)
        {
            ValidarNome(nome);
            ValidarEmail(email);
            ValidarSenha(senhaHash);

            Nome = nome.Trim();
            Email = email.Trim().ToLower();
            SenhaHash = senhaHash;
            Telefone = telefone;

            TipoUsuario = tipoUsuario;
            Status = StatusUsuario.Ativo;
            EmailConfirmado = false;
        }

        internal static Usuario Reidratar(
            Guid id,
            string nome,
            string email,
            string senhaHash,
            string? telefone,
            StatusUsuario status,
            TipoUsuario tipoUsuario,
            bool emailConfirmado,
            DateTime? ultimoLogin,
            DateTime createdAt,
            DateTime updatedAt)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("O identificador persistido é obrigatório.", nameof(id));
            if (string.IsNullOrWhiteSpace(nome))
                throw new ArgumentException("O nome persistido é obrigatório.", nameof(nome));
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
                throw new ArgumentException("O e-mail persistido é inválido.", nameof(email));
            if (string.IsNullOrWhiteSpace(senhaHash))
                throw new ArgumentException("O hash de senha persistido é obrigatório.", nameof(senhaHash));
            if (!Enum.IsDefined(status))
                throw new ArgumentOutOfRangeException(nameof(status), "O status persistido é inválido.");
            if (!Enum.IsDefined(tipoUsuario))
                throw new ArgumentOutOfRangeException(nameof(tipoUsuario), "O tipo de usuário persistido é inválido.");
            if (updatedAt < createdAt)
                throw new ArgumentException("A data de atualização não pode ser anterior à data de criação.", nameof(updatedAt));

            return new Usuario
            {
                Id = id,
                Nome = nome,
                Email = email,
                SenhaHash = senhaHash,
                Telefone = telefone,
                Status = status,
                TipoUsuario = tipoUsuario,
                EmailConfirmado = emailConfirmado,
                UltimoLogin = ultimoLogin,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            };
        }

        public void AlterarNome(string novoNome)
        {
            if (string.IsNullOrWhiteSpace(novoNome))
                throw new NomeObrigatorioException();

            Nome = novoNome.Trim();

            AtualizarDataAlteracao();
        }

        public void AlterarEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new EmailObrigatorioException();

            if (!email.Contains("@"))
                throw new EmailInvalidoException();

            var novoEmail = email.Trim().ToLower();

            if (Email != novoEmail)
            {
                Email = novoEmail;
                EmailConfirmado = false;
            }

            AtualizarDataAlteracao();
        }
        public void AlterarSenha(string novaSenhaHash)
        {
            if (string.IsNullOrWhiteSpace(novaSenhaHash))
                throw new SenhaObrigatoriaException();

            SenhaHash = novaSenhaHash;

            AtualizarDataAlteracao();
        }

        public void AlterarTelefone(string novoTelefone)
        {
            Telefone = novoTelefone;
            AtualizarDataAlteracao();
        }

        public void ConfirmarEmail()
        {
            EmailConfirmado = true;

            AtualizarDataAlteracao();
        }
        public void RegistrarLogin()
        {
            UltimoLogin = DateTime.UtcNow;

            AtualizarDataAlteracao();
        }

        public void Bloquear()
        {
            Status = StatusUsuario.Bloqueado;

            AtualizarDataAlteracao();
        }

        public void Ativar()
        {
            Status = StatusUsuario.Ativo;

            AtualizarDataAlteracao();
        }

        public void Inativar()
        {
            Status = StatusUsuario.Inativo;

            AtualizarDataAlteracao();
        }

        private static void ValidarNome(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new NomeObrigatorioException();
        }

        private static void ValidarEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new DomainException("O e-mail é obrigatório.");

            if (!email.Contains("@"))
                throw new EmailInvalidoException();
        }

        private static void ValidarSenha(string senhaHash)
        {
            if (string.IsNullOrWhiteSpace(senhaHash))
                throw new SenhaObrigatoriaException();
        }
    }
}
