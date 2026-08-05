namespace Domain.Exceptions.Email
{
    public class EmailInvalidoException:DomainException
    {
        public EmailInvalidoException()
        : base("O e-mail informado é inválido.")
        {
        }
    }
}
