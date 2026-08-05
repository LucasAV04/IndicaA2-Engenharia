namespace Domain.Exceptions.Senha
{
    public  class SenhaNaoConfereException:DomainException
    {
        public SenhaNaoConfereException():base("A confirmação da nova senha não confere.")
        {
        }
    }
}
