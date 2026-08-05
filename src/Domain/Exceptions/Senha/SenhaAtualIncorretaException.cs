namespace Domain.Exceptions.Senha
{
    public class SenhaAtualIncorretaException:DomainException
    {
        public SenhaAtualIncorretaException():base("A senha atual informada está incorreta.")
        {
        }
    }
}
