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
