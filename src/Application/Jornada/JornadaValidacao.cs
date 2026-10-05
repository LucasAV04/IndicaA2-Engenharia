using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Domain.Exceptions;

namespace Application.Jornada;

public static partial class JornadaValidacao
{
    public const string VersaoTermo = "2026-10-01";
    public static string Codigo(string? valor)
    {
        var codigo=valor?.Trim().ToUpperInvariant() ?? "";
        if (!CodigoRegex().IsMatch(codigo)) throw new ArgumentException("Link indisponível.");
        return codigo;
    }
    public static IndicacaoPublicaRequest Normalizar(IndicacaoPublicaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var nome=request.Nome?.Trim() ?? "";
        if(nome.Length is < 2 or > 150 || nome.Any(char.IsControl)) throw new ArgumentException("Confira os campos informados.");
        var bruto=request.Telefone ?? "";
        if(bruto.Length>30 || bruto.Any(c=>!char.IsAsciiDigit(c) && !"+ ()-.".Contains(c))) throw new ArgumentException("Telefone inválido.");
        var telefone=new string(bruto.Where(char.IsAsciiDigit).ToArray());
        if(telefone.Length is not (10 or 11 or 12 or 13)) throw new ArgumentException("Telefone inválido.");
        if(telefone.Length>=12 && !telefone.StartsWith("55",StringComparison.Ordinal)) throw new ArgumentException("Telefone inválido.");
        if(!request.Consentimento || request.VersaoTermo!=VersaoTermo) throw new DomainException("Aceite o termo vigente para continuar.");
        return request with { Codigo=Codigo(request.Codigo),Nome=nome,Telefone=telefone };
    }
    public static string HashChave(string chave)
    {
        if(!Guid.TryParseExact(chave,"D",out var id) || id==Guid.Empty) throw new ArgumentException("Chave de envio inválida.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString("D")))).ToLowerInvariant();
    }
    public static string PrimeiroNome(string nome)=>nome.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Pessoa indicada";
    public static string TelefoneMascarado(string telefone)=>"••••"+telefone[Math.Max(0,telefone.Length-2)..];
    public static string Link(string? baseUrl,string codigo)
    {
        if(!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri) || uri.Scheme!=Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Configure PublicWeb:BaseUrl HTTPS antes de compartilhar links.");
        return uri.AbsoluteUri.TrimEnd('/')+"/indicar/"+Uri.EscapeDataString(Codigo(codigo));
    }
    [GeneratedRegex("^[A-Z0-9]{8}$",RegexOptions.CultureInvariant)] private static partial Regex CodigoRegex();
}
