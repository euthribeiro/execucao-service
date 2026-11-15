using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using wrench.execucao.application;
using wrench.execucao.infra;
using wrench.execucao.tests.Support;

namespace wrench.execucao.tests.Api;

public abstract class ExecucaoApiFactoryBase(bool permitirFalhaForcada) : WebApplicationFactory<Program>
{
    public const string Segredo = "segredo-de-teste-com-mais-de-32-caracteres!";
    public const string Emissor = "Wrench Auto Repair";
    public const string Audiencia = "Wrench Auto Repair";

    public FakeExecucaoRepository Repositorio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("JwtOptions:Secret", Segredo);
        builder.UseSetting("Execucao:PermitirFalhaForcada", permitirFalhaForcada.ToString());

        builder.ConfigureTestServices(services =>
        {
            foreach (var hospedado in services
                         .Where(d => d.ServiceType == typeof(IHostedService) &&
                                     (d.ImplementationType == typeof(OutboxDispatcher) ||
                                      d.ImplementationType == typeof(DynamoTableInitializer) ||
                                      d.ImplementationType == typeof(SqsComandoConsumer)))
                         .ToList())
            {
                services.Remove(hospedado);
            }

            services.AddSingleton<IExecucaoRepository>(Repositorio);

            var dynamo = new Mock<IAmazonDynamoDB>();
            dynamo.Setup(d => d.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DescribeTableResponse { Table = new TableDescription { TableStatus = TableStatus.ACTIVE } });
            services.AddSingleton(dynamo.Object);
        });
    }

    public HttpClient CriarClienteAutenticado(string papel, string nome = "joao.mecanico")
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GerarToken(papel, nome));
        return cliente;
    }

    public static string GerarToken(string papel, string nome = "joao.mecanico")
    {
        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Segredo)),
            SecurityAlgorithms.HmacSha256Signature);

        var descritor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.Name, nome), new Claim(ClaimTypes.Role, papel)]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            Issuer = Emissor,
            Audience = Audiencia,
            SigningCredentials = credenciais
        };

        var manipulador = new JwtSecurityTokenHandler();
        return manipulador.WriteToken(manipulador.CreateToken(descritor));
    }
}

public class ExecucaoApiFactory() : ExecucaoApiFactoryBase(false);

public class ExecucaoApiFactoryFalhaForcada() : ExecucaoApiFactoryBase(true);
