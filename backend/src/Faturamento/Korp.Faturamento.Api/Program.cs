using Korp.Faturamento.Api.Middlewares;
using Korp.Faturamento.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((contexto, configuracao) => configuracao
    .ReadFrom.Configuration(contexto.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("servico", "faturamento")
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opcoes =>
{
    opcoes.SwaggerDoc("v1", new()
    {
        Title = "Korp | Servico de Faturamento",
        Version = "v1",
        Description = "Notas fiscais e impressao. Orquestra a baixa de estoque " +
                      "com compensacao, chamando o servico de Estoque via HTTP."
    });

    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");

    if (File.Exists(xml))
        opcoes.IncludeXmlComments(xml);
});

builder.Services.AddExceptionHandler<ManipuladorGlobalDeExcecoes>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks();

builder.Services.AddCors(opcoes =>
    opcoes.AddDefaultPolicy(politica => politica
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()));

builder.Services.AdicionarInfraestrutura(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseCors();

app.UseSwagger();
app.UseSwaggerUI(opcoes =>
{
    opcoes.SwaggerEndpoint("/swagger/v1/swagger.json", "Servico de Faturamento v1");
    opcoes.RoutePrefix = "swagger";
});

app.MapControllers();

// O health check deste servico NAO consulta o servico de Estoque de proposito.
// O Faturamento esta saudavel mesmo com o Estoque fora do ar: ele continua
// listando, criando e editando notas, e recusa apenas a impressao, com erro
// tratado. Amarrar a saude de um a do outro anularia a independencia entre
// os servicos, que e o ponto da arquitetura.
app.MapHealthChecks("/health");

await app.Services.PrepararBancoAsync();

app.Run();

/// <summary>
/// Exposto para o projeto de testes instanciar a aplicacao com
/// WebApplicationFactory nos testes de integracao.
/// </summary>
public partial class Program { }
