namespace Domain.Exceptions
{
    public class CadastroAdministradorNaoPermitidoException:DomainException
    {
        public CadastroAdministradorNaoPermitidoException() : base("O cadastro de administradores não é permitido por esta operação.")
        {
        }
    }
}
