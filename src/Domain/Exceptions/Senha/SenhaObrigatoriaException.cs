namespace Domain.Exceptions.Senha
{
    public class SenhaObrigatoriaException:DomainException
    {
        public SenhaObrigatoriaException()
       : base("A senha é obrigatória.")
        {
        }
    }
}
