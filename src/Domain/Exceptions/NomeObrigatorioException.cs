namespace Domain.Exceptions
{
    public class NomeObrigatorioException:DomainException
    {
        public NomeObrigatorioException()
        : base("O nome é obrigatório.")
        {
        }
    }
}
