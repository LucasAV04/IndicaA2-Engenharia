namespace Infrastructure.Security;

public interface IDadosPixProtector
{
    DadosPixProtegido Proteger(string chavePix);

    DadosPixProtegido Proteger(string chavePix, byte[] associatedData);

    string Desproteger(DadosPixProtegido dadosPixProtegido);

    string Desproteger(DadosPixProtegido dadosPixProtegido, byte[] associatedData);
}
