using System.Security.Cryptography;
using Application.Interfaces.Security;

namespace Infrastructure.Security;

public sealed class CodigoIndicacaoGenerator : ICodigoIndicacaoGenerator
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int TamanhoCodigo = 8;

    public string Gerar()
    {
        Span<char> caracteres = stackalloc char[TamanhoCodigo];

        for (var indice = 0; indice < caracteres.Length; indice++)
            caracteres[indice] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];

        return new string(caracteres);
    }
}
