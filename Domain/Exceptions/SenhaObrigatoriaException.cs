namespace Domain.Exceptions
{
    public class SenhaObrigatoriaException:DomainException
    {
        public SenhaObrigatoriaException()
       : base("A senha é obrigatória.")
        {
        }
    }
}
