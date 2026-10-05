using API.ExceptionHandling;
using API.Authorization;
using API.OpenApi;
using API.Security;
using API.Processing;
using Application.Interfaces.Services;
using Application.Services;
using Infrastructure.DependencyInjection;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

if (builder.Configuration.GetValue<bool>("RecebimentoPix:Habilitado"))
{
    var webhookCa = builder.Configuration["RecebimentoPix:WebhookCaPath"] ?? "";
    builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https =>
    {
        https.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.AllowCertificate;
        https.ClientCertificateValidation = (certificate, _, _) => RecebimentoPixMtlsMiddleware.Confiavel(certificate, webhookCa);
    }));
}

// O handler abaixo registra falhas de forma controlada. O middleware não deve
// registrar antes dele a exceção bruta, que pode conter uma chave Pix inválida.
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<AuthorizationOperationTransformer>();
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
jwtOptions.Validate();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IAuthorizationHandler, IndicacaoOwnerOrAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, VistoriaOwnerOrAdminHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Administrador, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(AuthorizationRoles.Administrador);
        policy.RequireAssertion(context => CurrentUserClaims.TryGetUserId(context.User, out _));
    });
    options.AddPolicy(AuthorizationPolicies.IndicacaoOwnerOrAdmin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new IndicacaoOwnerOrAdminRequirement());
    });
    options.AddPolicy(AuthorizationPolicies.VistoriaOwnerOrAdmin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new VistoriaOwnerOrAdminRequirement());
    });
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRecebimentoPix(builder.Configuration);
builder.Services.AddHostedService<RecebimentoPixVistoriaWorker>();
var cashbackPreparacao=builder.Configuration.GetSection("CashbackPagamentoPreparacaoWorker").Get<CashbackPreparacaoOptions>() ?? new();
cashbackPreparacao.Validar();
builder.Services.AddSingleton(cashbackPreparacao);
builder.Services.AddHostedService<CashbackPagamentoPreparacaoWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("referral-link", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            { PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("payment-link", context =>
    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var processamentoWorkerOptions = builder.Configuration
    .GetSection(PagamentoPixProcessamentoWorkerOptions.SectionName)
    .Get<PagamentoPixProcessamentoWorkerOptions>() ?? new PagamentoPixProcessamentoWorkerOptions();
processamentoWorkerOptions.Validate();
builder.Services.Configure<PagamentoPixProcessamentoWorkerOptions>(
    builder.Configuration.GetSection(PagamentoPixProcessamentoWorkerOptions.SectionName));
builder.Services.AddHostedService<PagamentoPixProcessamentoWorker>();
builder.Services.AddScoped<IIndicacaoService, IndicacaoService>();
builder.Services.AddScoped<IUsuarioService, IndicA2.Application.Services.UsuarioService>();
builder.Services.AddScoped<IDadosPixService, DadosPixService>();
builder.Services.AddScoped<IPagamentoVistoriaService, PagamentoVistoriaService>();
builder.Services.AddScoped<IVistoriaService, VistoriaService>();
builder.Services.AddScoped<IPrecificacaoService, PrecificacaoService>();
builder.Services.AddScoped<ICashbackService, CashbackService>();
builder.Services.AddScoped<IPagamentoPixService, PagamentoPixService>();
builder.Services.AddScoped<IPagamentoPixEnvioService, PagamentoPixEnvioService>();
builder.Services.AddScoped<IPagamentoPixReconciliacaoService, PagamentoPixReconciliacaoService>();
builder.Services.AddScoped<IPagamentoPixAplicacaoResultadoService, PagamentoPixAplicacaoResultadoService>();
builder.Services.AddScoped<IPagamentoPixProcessamentoService, PagamentoPixProcessamentoService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseMiddleware<RecebimentoPixMtlsMiddleware>();
app.UseRateLimiter();
app.Use(async (context,next)=>
{
    if(context.Request.Path.StartsWithSegments("/api/public/indicacoes") || context.Request.Path.StartsWithSegments("/api/minha-conta") || context.Request.Path.StartsWithSegments("/api/notificacoes") || context.Request.Path.StartsWithSegments("/api/admin/jornada"))
    { context.Response.Headers.CacheControl="no-store"; context.Response.Headers["Referrer-Policy"]="no-referrer"; }
    if((context.Request.Path.StartsWithSegments("/api/minha-conta") || context.Request.Path.StartsWithSegments("/api/notificacoes")) && context.Request.Query.Count>0)
    { context.Response.StatusCode=StatusCodes.Status404NotFound; return; }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
