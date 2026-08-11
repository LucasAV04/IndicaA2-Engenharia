using API.OpenApi;
using Microsoft.OpenApi.Models;
using Xunit;

namespace API.Tests.OpenApi;

public sealed class BearerSecuritySchemeTransformerTests
{
    [Fact]
    public async Task TransformAsync_DeveAdicionarEsquemaBearerAoDocumento()
    {
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents()
        };

        var transformer = new BearerSecuritySchemeTransformer();

        await transformer.TransformAsync(document, context: null!, CancellationToken.None);

        var scheme = document.Components.SecuritySchemes["Bearer"];

        Assert.Equal(SecuritySchemeType.Http, scheme.Type);
        Assert.Equal("bearer", scheme.Scheme);
        Assert.Equal("JWT", scheme.BearerFormat);
        Assert.Equal(ParameterLocation.Header, scheme.In);
    }
}
