using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Sellora.DeliveryService.Api.Security;

/// <summary>
/// Certificate validation for the outbound dependency clients (Organization, Inventory)
/// on hosts that do not trust the shared gateway CA (local and staging). A certificate
/// that fails validation is accepted only from a configured dependency host and port,
/// so the relaxation never reaches any other server.
/// </summary>
public static class DependencyCertificatePolicy
{
    public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> CreateValidator(
        IEnumerable<string?> trustedAddresses)
    {
        var trustedHosts = ParseHosts(trustedAddresses);

        return (request, _, _, errors) => IsTrusted(request.RequestUri, errors, trustedHosts);
    }

    public static bool IsTrusted(Uri? requestUri, SslPolicyErrors errors, IReadOnlyCollection<Uri> trustedHosts)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        return requestUri is not null
            && trustedHosts.Any(trusted =>
                string.Equals(requestUri.Host, trusted.Host, StringComparison.OrdinalIgnoreCase)
                && requestUri.Port == trusted.Port);
    }

    private static List<Uri> ParseHosts(IEnumerable<string?> addresses) =>
        addresses
            .Select(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .ToList();
}
