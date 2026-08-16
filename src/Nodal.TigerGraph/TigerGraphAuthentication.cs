using System.Net.Http.Headers;
using System.Text;

namespace Nodal.TigerGraph;

internal static class TigerGraphAuthentication
{
    public static void Apply(HttpRequestMessage request, TigerGraphOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Username) || options.Password is null)
        {
            throw new InvalidOperationException(
                "TigerGraph authentication requires either an access token or a username and password.");
        }

        var value = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", value);
    }
}
