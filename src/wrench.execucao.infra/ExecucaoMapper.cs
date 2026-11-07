using System.Globalization;
using Amazon.DynamoDBv2.Model;
using wrench.execucao.domain;

namespace wrench.execucao.infra;

public static class ExecucaoMapper
{
    public static Dictionary<string, AttributeValue> ParaItem(Execucao execucao, long versao)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["ordemServicoId"] = S(execucao.OrdemServicoId.ToString()),
            ["status"] = S(execucao.Status.ToString()),
            ["descricao"] = S(execucao.Descricao),
            ["valorAprovado"] = N(execucao.ValorAprovado.ToString(CultureInfo.InvariantCulture)),
            ["itens"] = new AttributeValue
            {
                L = [.. execucao.Itens.Select(i => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["pecaId"] = S(i.PecaId.ToString()),
                        ["nome"] = S(i.Nome),
                        ["quantidade"] = N(i.Quantidade.ToString(CultureInfo.InvariantCulture))
                    }
                })]
            },
            ["enfileiradaEm"] = S(Data(execucao.EnfileiradaEm)),
            ["versao"] = N(versao.ToString(CultureInfo.InvariantCulture))
        };

        if (execucao.IniciadaEm.HasValue) item["iniciadaEm"] = S(Data(execucao.IniciadaEm.Value));
        if (execucao.ConcluidaEm.HasValue) item["concluidaEm"] = S(Data(execucao.ConcluidaEm.Value));
        if (!string.IsNullOrEmpty(execucao.Mecanico)) item["mecanico"] = S(execucao.Mecanico);
        if (!string.IsNullOrEmpty(execucao.Motivo)) item["motivo"] = S(execucao.Motivo);
        if (!string.IsNullOrEmpty(execucao.CorrelationId)) item["correlationId"] = S(execucao.CorrelationId);

        return item;
    }

    public static Execucao ParaDominio(IReadOnlyDictionary<string, AttributeValue> item)
    {
        var itens = item.TryGetValue("itens", out var lista) && lista.L is not null
            ? lista.L.Select(i => new ItemExecucao(
                Guid.Parse(i.M["pecaId"].S),
                i.M["nome"].S,
                int.Parse(i.M["quantidade"].N, CultureInfo.InvariantCulture)))
            : [];

        return Execucao.Restaurar(
            Guid.Parse(item["ordemServicoId"].S),
            Enum.Parse<ExecucaoStatus>(item["status"].S),
            item["descricao"].S,
            decimal.Parse(item["valorAprovado"].N, CultureInfo.InvariantCulture),
            itens,
            LerData(item["enfileiradaEm"].S),
            Opcional(item, "iniciadaEm") is { } iniciada ? LerData(iniciada) : null,
            Opcional(item, "concluidaEm") is { } concluida ? LerData(concluida) : null,
            Opcional(item, "mecanico"),
            Opcional(item, "motivo"),
            Opcional(item, "correlationId"),
            long.Parse(item["versao"].N, CultureInfo.InvariantCulture));
    }

    public static string Data(DateTime data) => data.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTime LerData(string texto) =>
        DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    public static AttributeValue S(string valor) => new() { S = valor };

    public static AttributeValue N(string valor) => new() { N = valor };

    private static string? Opcional(IReadOnlyDictionary<string, AttributeValue> item, string chave) =>
        item.TryGetValue(chave, out var valor) ? valor.S : null;
}
