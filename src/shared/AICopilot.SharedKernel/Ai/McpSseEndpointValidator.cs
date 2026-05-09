using System.Net;
using System.Net.Sockets;

namespace AICopilot.SharedKernel.Ai;

public static class McpSseEndpointValidator
{
    public static bool TryValidate(string? endpoint, out Uri? uri, out string errorMessage)
    {
        uri = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            errorMessage = "MCP SSE endpoint is required.";
            return false;
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var candidate))
        {
            errorMessage = "MCP SSE endpoint must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage = "MCP SSE endpoint must use HTTP or HTTPS.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate.Host))
        {
            errorMessage = "MCP SSE endpoint must include a host.";
            return false;
        }

        if (!string.IsNullOrEmpty(candidate.UserInfo))
        {
            errorMessage = "MCP SSE endpoint must not include credentials.";
            return false;
        }

        if (!string.IsNullOrEmpty(candidate.Fragment))
        {
            errorMessage = "MCP SSE endpoint must not include a fragment.";
            return false;
        }

        if (IsLocalhostName(candidate.Host))
        {
            errorMessage = "MCP SSE endpoint must not target localhost.";
            return false;
        }

        if (IPAddress.TryParse(candidate.Host, out var address) && IsUnsafeAddress(address))
        {
            errorMessage = "MCP SSE endpoint must not target loopback, private, link-local, multicast, or unspecified addresses.";
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool IsLocalhostName(string host)
    {
        var normalizedHost = host.TrimEnd('.');
        return normalizedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || normalizedHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsUnsafeIPv4Address(address),
            AddressFamily.InterNetworkV6 => IsUnsafeIPv6Address(address),
            _ => true
        };
    }

    private static bool IsUnsafeIPv4Address(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
               || bytes[0] == 10
               || bytes[0] == 127
               || bytes[0] == 169 && bytes[1] == 254
               || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
               || bytes[0] == 192 && bytes[1] == 168
               || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
               || bytes[0] == 198 && bytes[1] is 18 or 19
               || bytes[0] >= 224;
    }

    private static bool IsUnsafeIPv6Address(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return address.Equals(IPAddress.IPv6Any)
               || address.IsIPv6LinkLocal
               || address.IsIPv6Multicast
               || (bytes[0] & 0xfe) == 0xfc;
    }
}
