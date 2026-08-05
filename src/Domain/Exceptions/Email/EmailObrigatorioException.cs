namespace Domain.Exceptions.Email
{
    public class EmailObrigatorioException:DomainException
    {
        public EmailObrigatorioException() : base("O e-mail é obrigatório.")
        {

        }
    }
}
