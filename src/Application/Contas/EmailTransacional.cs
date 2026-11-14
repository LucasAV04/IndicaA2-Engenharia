namespace Application.Contas;

public enum TemplateEmail : byte
{
    ConfirmacaoEmail,
    RedefinicaoSenha,
    Vistoria,
    PagamentoConfirmado,
    DadosPixNecessarios,
    CashbackDisponivel,
    CashbackPago
}

public enum EstadoEmail : byte
{
    Pendente,
    Processando,
    Enviado,
    FalhaPermanente,
    Indeterminado,
    Expirado
}

public sealed record EmailTransacional(string Destinatario, string Assunto, string Texto);

public interface IEmailSender
{
    Task EnviarAsync(EmailTransacional email, CancellationToken cancellationToken);
}

public static class TemplatesEmail
{
    public static EmailTransacional Criar(Uri origem, string email, TemplateEmail template, string? token)
    {
        var (titulo, caminho) = template switch
        {
            TemplateEmail.ConfirmacaoEmail => ("Confirme seu e-mail", "/confirmar-email"),
            TemplateEmail.RedefinicaoSenha => ("Redefina sua senha", "/redefinir-senha"),
            TemplateEmail.Vistoria => ("Há uma atualização sobre sua vistoria", "/minha-conta"),
            TemplateEmail.PagamentoConfirmado => ("Seu pagamento foi confirmado", "/minha-conta"),
            TemplateEmail.DadosPixNecessarios => ("Consulte as instruções para receber Cashback", "/minha-conta"),
            TemplateEmail.CashbackDisponivel => ("Seu Cashback está disponível", "/minha-conta"),
            TemplateEmail.CashbackPago => ("Seu Cashback foi pago", "/minha-conta"),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
        var link = new UriBuilder(new Uri(origem, caminho));
        if (template is TemplateEmail.ConfirmacaoEmail or TemplateEmail.RedefinicaoSenha)
        {
            if (SegurancaConta.HashToken(token) is null)
                throw new ArgumentException("Token de conta inválido.", nameof(token));
            link.Fragment = token;
        }

        return new EmailTransacional(email, titulo, $"{titulo}.\n\nAcesse: {link.Uri.AbsoluteUri}");
    }
}
