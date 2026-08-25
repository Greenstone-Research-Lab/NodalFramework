using System.Text.Json;

namespace Nodal.TigerGraph;

internal static class TigerGraphAdministrativeResponse
{
    public static bool IsMissingVertex(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.True &&
                document.RootElement.TryGetProperty("code", out var code) &&
                string.Equals(code.GetString(), "601", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static void EnsureSuccess(HttpResponseMessage response, string payload, string endpointName)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TigerGraph {endpointName} returned HTTP {(int)response.StatusCode}: {payload}",
                null,
                response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.True)
            {
                var message = document.RootElement.TryGetProperty("message", out var value)
                    ? value.GetString()
                    : null;
                throw new InvalidOperationException(
                    $"TigerGraph {endpointName} reported an error: {message ?? payload}");
            }
        }
        catch (JsonException)
        {
            // Some supported administrative proxies return a non-JSON success body.
        }
    }
}
