namespace Domain.Exceptions
{
    public sealed class UsuarioNaoEncontradoException:DomainException
    {
        public UsuarioNaoEncontradoException()
        : base("Usuário não encontrado.")
        {
        }   
    }
}
