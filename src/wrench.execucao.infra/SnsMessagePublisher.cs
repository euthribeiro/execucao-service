using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using wrench.execucao.application;

namespace wrench.execucao.infra;

public class SnsMessagePublisher(IAmazonSimpleNotificationService sns, IOptions<SnsOptions> opcoes) : IMessagePublisher
{
    private readonly SnsOptions _opcoes = opcoes.Value;

    public async Task PublicarAsync(OutboxMessage mensagem, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.TopicArn))
            throw new InvalidOperationException("O tópico SNS (Sns:TopicArn) não foi configurado.");

        var requisicao = new PublishRequest
        {
            TopicArn = _opcoes.TopicArn,
            Message = mensagem.Payload,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["messageId"] = Atributo(mensagem.Id.ToString()),
                ["tipo"] = Atributo(mensagem.Tipo)
            }
        };

        if (!string.IsNullOrEmpty(mensagem.CorrelationId))
            requisicao.MessageAttributes["correlationId"] = Atributo(mensagem.CorrelationId);

        if (!string.IsNullOrEmpty(mensagem.Traceparent))
            requisicao.MessageAttributes["traceparent"] = Atributo(mensagem.Traceparent);

        await sns.PublishAsync(requisicao, cancellationToken);
    }

    private static MessageAttributeValue Atributo(string valor) =>
        new() { DataType = "String", StringValue = valor };
}
