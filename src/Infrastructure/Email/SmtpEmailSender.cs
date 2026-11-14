using System.Net;
using System.Net.Mail;
using Application.Contas;

namespace Infrastructure.Email;

public sealed class EmailOptions
{
    public bool Habilitado { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Usuario { get; init; } = "";
    public string Senha { get; init; } = "";
    public string Remetente { get; init; } = "";
    public bool UsarTls { get; init; } = true;
    public EmailWorkerOptions Worker { get; init; } = new();
}
public sealed class EmailWorkerOptions
{
    public bool Habilitado { get; init; }
    public int IntervaloSegundos { get; init; } = 30;
    public int TamanhoLote { get; init; } = 20;
}
public sealed class SmtpEmailSender(EmailOptions options, bool apenasLocal) : IEmailSender
{
    public async Task EnviarAsync(EmailTransacional email, CancellationToken ct)
    {
        var destinatario = new MailAddress(email.Destinatario);
        if (apenasLocal && destinatario.Host != "example.invalid")
            throw new FormatException("Destinatário fora da homologação.");
        using var message = new MailMessage(new MailAddress(options.Remetente), destinatario)
        {
            Subject = email.Assunto,
            Body = email.Texto,
            IsBodyHtml = false
        };
        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.UsarTls,
            UseDefaultCredentials = false,
            Credentials = options.Usuario.Length == 0 ? null : new NetworkCredential(options.Usuario, options.Senha),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await client.SendMailAsync(message, timeout.Token);
    }
}
