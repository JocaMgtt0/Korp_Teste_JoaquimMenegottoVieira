using Korp.Estoque.Api.Middlewares;
using Korp.Estoque.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Log estruturado em JSON. Cada linha vira um objeto com propriedades
// consultaveis, em vez de texto solto. E o que permite rastrear uma
// requisicao atravessando os dois servicos pelo mesmo identificador.
builder.Host.UseSerilog((contexto, configuracao) => configuracao
    .ReadFrom.Configuration(contexto.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("servico", "estoque")
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opcoes =>
{
    opcoes.SwaggerDoc("v1", new()
    {
        Title = "Korp | Servico de Estoque",
        Version = "v1",
        Description = "Cadastro de produtos e controle de saldo. " +
                      "Dono exclusivo do saldo em todo o sistema."
    });

    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");

    if (File.Exists(xml))
        opcoes.IncludeXmlComments(xml);
});

builder.Services.AddExceptionHandler<ManipuladorGlobalDeExcecoes>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks();

// O frontend Angular roda em outra origem, entao precisa de CORS liberado.
// Aberto de proposito: e um ambiente de avaliacao local, sem autenticacao.
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
    opcoes.SwaggerEndpoint("/swagger/v1/swagger.json", "Servico de Estoque v1");
    opcoes.RoutePrefix = "swagger";
});

app.MapControllers();
app.MapHealthChecks("/health");

// Migrations e seed antes de aceitar requisicao. Se o banco nao estiver
// pronto, o servico falha ao subir em vez de responder erro a cada chamada.
await app.Services.PrepararBancoAsync();

app.Run();

/// <summary>
/// Exposto para que o projeto de testes consiga instanciar a aplicacao
/// com WebApplicationFactory nos testes de integracao.
/// </summary>
public partial class Program { }
