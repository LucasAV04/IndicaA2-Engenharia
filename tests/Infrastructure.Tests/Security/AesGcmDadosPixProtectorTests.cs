using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using Infrastructure.Security;
using Xunit;

namespace Infrastructure.Tests.Security;

public sealed class AesGcmDadosPixProtectorTests
{
    private const string ChavePix = "pix.teste@example.com";

    [Fact]
    public void ProtegerEDesproteger_DevePreservarPlaintextComCifraAutenticada()
    {
        var protector = CriarProtector();

        var materialProtegido = protector.Proteger(ChavePix);
        var resultado = protector.Desproteger(materialProtegido);

        Assert.Equal(ChavePix, resultado);
        Assert.Equal(AesGcmDadosPixProtector.EncryptionVersion, materialProtegido.EncryptionVersion);
    }

    [Fact]
    public void ProtegerEDesproteger_ComMesmoAssociatedDataDevePreservarPlaintext()
    {
        var protector = CriarProtector();
        var associatedData = Encoding.UTF8.GetBytes("PagamentoPix:v1|contexto-de-teste");

        var materialProtegido = protector.Proteger(ChavePix, associatedData);
        var resultado = protector.Desproteger(materialProtegido, associatedData);

        Assert.Equal(ChavePix, resultado);
    }

    [Fact]
    public void Desproteger_ComAssociatedDataDiferenteDeveFalharNaAutenticacao()
    {
        var protector = CriarProtector();
        var associatedData = Encoding.UTF8.GetBytes("PagamentoPix:v1|ordem-a");
        var materialProtegido = protector.Proteger(ChavePix, associatedData);

        var exception = Assert.Throws<CryptographicException>(() =>
            protector.Desproteger(materialProtegido, Encoding.UTF8.GetBytes("PagamentoPix:v1|ordem-b")));

        Assert.DoesNotContain(ChavePix, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Desproteger_ComAssociatedDataAdulteradoDeveFalharNaAutenticacao()
    {
        var protector = CriarProtector();
        var associatedDataOriginal = Encoding.UTF8.GetBytes("PagamentoPix:v1|ordem-a");
        var associatedDataAdulterado = associatedDataOriginal.ToArray();
        associatedDataAdulterado[^1] ^= 0x01;
        var materialProtegido = protector.Proteger(ChavePix, associatedDataOriginal);

        var exception = Assert.Throws<CryptographicException>(() =>
            protector.Desproteger(materialProtegido, associatedDataAdulterado));

        Assert.DoesNotContain(ChavePix, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtegerEDesproteger_ComAssociatedDataVazioDeveManterCompatibilidade()
    {
        var protector = CriarProtector();
        var materialProtegido = protector.Proteger(ChavePix, Array.Empty<byte>());

        var resultado = protector.Desproteger(materialProtegido, Array.Empty<byte>());

        Assert.Equal(ChavePix, resultado);
    }

    [Fact]
    public void Proteger_QuandoMesmoPlaintextForUsadoDuasVezesDeveGerarNovoNonceEMaterialDiferente()
    {
        var protector = CriarProtector();

        var primeiro = protector.Proteger(ChavePix);
        var segundo = protector.Proteger(ChavePix);

        Assert.NotEqual(primeiro.Nonce, segundo.Nonce);
        Assert.NotEqual(primeiro.Ciphertext, segundo.Ciphertext);
    }

    [Fact]
    public void Proteger_DeveUsarNonceETagNosTamanhosDefinidos()
    {
        var materialProtegido = CriarProtector().Proteger(ChavePix);

        Assert.Equal(AesGcmDadosPixProtector.NonceSizeInBytes, materialProtegido.Nonce.Length);
        Assert.Equal(AesGcmDadosPixProtector.TagSizeInBytes, materialProtegido.Tag.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-base64")]
    public void Criar_QuandoChaveForInvalidaDeveRejeitar(string chaveBase64)
    {
        Assert.Throws<ArgumentException>(() => new AesGcmDadosPixProtector(chaveBase64));
    }

    [Fact]
    public void Criar_QuandoChaveNaoPossuir32BytesDeveRejeitar()
    {
        var chaveComTamanhoInvalido = Convert.ToBase64String(new byte[31]);

        Assert.Throws<ArgumentException>(() => new AesGcmDadosPixProtector(chaveComTamanhoInvalido));
    }

    [Fact]
    public void Proteger_QuandoPlaintextForVazioDeveRejeitar()
    {
        Assert.Throws<ArgumentException>(() => CriarProtector().Proteger(" "));
    }

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    public void Desproteger_QuandoMaterialForAdulteradoDeveFalharNaAutenticacao(string parteAdulterada)
    {
        var protector = CriarProtector();
        var material = protector.Proteger(ChavePix);
        var ciphertext = material.Ciphertext.ToArray();
        var nonce = material.Nonce.ToArray();
        var tag = material.Tag.ToArray();

        switch (parteAdulterada)
        {
            case "ciphertext":
                ciphertext[0] ^= 1;
                break;
            case "nonce":
                nonce[0] ^= 1;
                break;
            default:
                tag[0] ^= 1;
                break;
        }

        var adulterado = new DadosPixProtegido(ciphertext, nonce, tag, material.EncryptionVersion);

        var exception = Assert.Throws<CryptographicException>(() => protector.Desproteger(adulterado));
        Assert.DoesNotContain(ChavePix, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Proteger_NaoDeveManterPlaintextNoMaterialPersistivel()
    {
        var material = CriarProtector().Proteger(ChavePix);
        var plaintext = Encoding.UTF8.GetBytes(ChavePix);

        Assert.False(material.Ciphertext.SequenceEqual(plaintext));
        Assert.False(ContemSequencia(material.Ciphertext, plaintext));
        Assert.False(ContemSequencia(material.Nonce, plaintext));
        Assert.False(ContemSequencia(material.Tag, plaintext));
    }

    [Fact]
    public void Dispose_DeveZerarOMaterialDaChaveEImpedirNovoUso()
    {
        var protector = CriarProtector();
        var keyField = typeof(AesGcmDadosPixProtector).GetField(
            "_key",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var key = Assert.IsType<byte[]>(keyField?.GetValue(protector));

        Assert.Contains(key, value => value != 0);

        protector.Dispose();

        Assert.All(key, value => Assert.Equal((byte)0, value));
        Assert.Throws<ObjectDisposedException>(() => protector.Proteger(ChavePix));

        protector.Dispose();
    }

    private static AesGcmDadosPixProtector CriarProtector() =>
        new(Convert.ToBase64String(Enumerable.Range(1, AesGcmDadosPixProtector.KeySizeInBytes).Select(valor => (byte)valor).ToArray()));

    private static bool ContemSequencia(byte[] origem, byte[] sequencia)
    {
        if (sequencia.Length > origem.Length)
            return false;

        for (var indice = 0; indice <= origem.Length - sequencia.Length; indice++)
        {
            if (origem.AsSpan(indice, sequencia.Length).SequenceEqual(sequencia))
                return true;
        }

        return false;
    }
}
