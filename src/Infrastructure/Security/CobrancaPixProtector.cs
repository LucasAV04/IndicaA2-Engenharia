using System.Text;

namespace Infrastructure.Security;

// Instância e chave independentes; reutiliza somente a primitiva AES-GCM já testada.
public sealed class CobrancaPixProtector(string keyBase64) : IDisposable
{
    private readonly AesGcmDadosPixProtector _aes = new(keyBase64);
    private static byte[] Contexto(Guid id) => Encoding.UTF8.GetBytes($"CobrancaPix:v1|{id:N}");
    public DadosPixProtegido Proteger(Guid id, string codigo) => _aes.Proteger(codigo, Contexto(id));
    public string Desproteger(Guid id, DadosPixProtegido protegido) => _aes.Desproteger(protegido, Contexto(id));
    public void Dispose() => _aes.Dispose();
}
