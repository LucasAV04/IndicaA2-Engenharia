using API.ExceptionHandling;
using API.Authorization;
using API.OpenApi;
using API.Security;
using Application.Interfaces.Services;
using Application.Services;
using Infrastructure.DependencyInjection;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<IIndicacaoService, IndicacaoService>();
builder.Services.AddScoped<IVistoriaService, VistoriaService>();
builder.Services.AddScoped<ICashbackService, CashbackService>();
builder.Services.AddScoped<IPagamentoPixService, PagamentoPixService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
