namespace API.Security;

public static class AuthorizationPolicies
{
    public const string Administrador = nameof(Administrador);
    public const string IndicacaoOwnerOrAdmin = nameof(IndicacaoOwnerOrAdmin);
    public const string VistoriaOwnerOrAdmin = nameof(VistoriaOwnerOrAdmin);
}
