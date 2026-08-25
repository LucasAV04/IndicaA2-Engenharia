namespace Infrastructure.Security;

public interface IDadosPixProtector
{
    DadosPixProtegido Proteger(string chavePix);

    string Desproteger(DadosPixProtegido dadosPixProtegido);
}
