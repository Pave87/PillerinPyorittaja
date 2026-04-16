using System.Net;
using System.Net.Sockets;

namespace MauiBlazorHybrid.Core;

/// <summary>
/// Utility methods for classifying IP addresses as private or public.
/// Lives in Core so the logic is unit-testable without MAUI dependencies.
/// </summary>
public static class NetworkAddressHelper
{
    /// <summary>
    /// Returns true when the given IP address belongs to a private, loopback,
    /// or link-local range — i.e. traffic stays on a local network.
    /// </summary>
    public static bool IsPrivateAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6 link-local (fe80::/10) or site-local
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
                return true;

            var bytes = address.GetAddressBytes();
            // fc00::/7 (unique local addresses)
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;

            return false;
        }

        var ipBytes = address.GetAddressBytes();
        // 10.0.0.0/8
        if (ipBytes[0] == 10)
            return true;
        // 172.16.0.0/12
        if (ipBytes[0] == 172 && ipBytes[1] >= 16 && ipBytes[1] <= 31)
            return true;
        // 192.168.0.0/16
        if (ipBytes[0] == 192 && ipBytes[1] == 168)
            return true;
        // 169.254.0.0/16 (link-local)
        if (ipBytes[0] == 169 && ipBytes[1] == 254)
            return true;

        return false;
    }
}
