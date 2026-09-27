using System.Text.Json;

namespace CyberBilling.Shared.Networking;

public static class ProtocolJson
{
    private static readonly JsonSerializerOptions
        SerializerOptions =
            new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(
        string type,
        T payload)
    {
        string payloadJson =
            JsonSerializer.Serialize(
                payload,
                SerializerOptions);

        var message =
            new ProtocolMessage(
                type,
                payloadJson);

        return JsonSerializer.Serialize(
            message,
            SerializerOptions);
    }

    public static ProtocolMessage?
        DeserializeMessage(
            string json)
    {
        return JsonSerializer.Deserialize<
            ProtocolMessage>(
                json,
                SerializerOptions);
    }

    public static T?
        DeserializePayload<T>(
            ProtocolMessage message)
    {
        return JsonSerializer.Deserialize<T>(
            message.Payload,
            SerializerOptions);
    }
}