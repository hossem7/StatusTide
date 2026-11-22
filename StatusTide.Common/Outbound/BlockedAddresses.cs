using System.Net;

namespace StatusTide.Common.Outbound;

/// <summary>
/// Addresses the server must never connect to on a user's behalf: its own machine, private
/// networks, the cloud metadata service and anything that isn't a normal public address.
/// </summary>
public static class BlockedAddresses
{
    private static readonly IPNetwork[] Ranges =
    [
        // IPv4
        IPNetwork.Parse("0.0.0.0/8"), // "this network"
        IPNetwork.Parse("10.0.0.0/8"), // private
        IPNetwork.Parse("100.64.0.0/10"), // carrier-grade NAT
        IPNetwork.Parse("127.0.0.0/8"), // loopback
        IPNetwork.Parse("169.254.0.0/16"), // link-local, including the metadata service at 169.254.169.254
        IPNetwork.Parse("172.16.0.0/12"), // private
        IPNetwork.Parse("192.0.0.0/24"), // protocol assignments
        IPNetwork.Parse("192.0.2.0/24"), // documentation
        IPNetwork.Parse("192.88.99.0/24"), // old 6to4 relays
        IPNetwork.Parse("192.168.0.0/16"), // private
        IPNetwork.Parse("198.18.0.0/15"), // benchmarking
        IPNetwork.Parse("198.51.100.0/24"), // documentation
        IPNetwork.Parse("203.0.113.0/24"), // documentation
        IPNetwork.Parse("224.0.0.0/4"), // multicast
        IPNetwork.Parse("240.0.0.0/4"), // reserved, including broadcast

        // IPv6
        IPNetwork.Parse("::/128"), // unspecified
        IPNetwork.Parse("::1/128"), // loopback
        IPNetwork.Parse("64:ff9b:1::/48"), // local NAT64
        IPNetwork.Parse("100::/64"), // discard
        IPNetwork.Parse("2001::/23"), // protocol assignments, including Teredo
        IPNetwork.Parse("2001:db8::/32"), // documentation
        IPNetwork.Parse("fc00::/7"), // unique local (private)
        IPNetwork.Parse("fe80::/10"), // link-local
        IPNetwork.Parse("fec0::/10"), // old site-local
        IPNetwork.Parse("ff00::/8"), // multicast
    ];

    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");
    private static readonly IPNetwork SixToFour = IPNetwork.Parse("2002::/16");

    /// <summary>
    /// True when <paramref name="address"/> is not a normal public address. IPv6 forms that carry
    /// an IPv4 address (mapped, NAT64, 6to4) are judged by that IPv4 address, so
    /// <c>::ffff:127.0.0.1</c> is blocked like <c>127.0.0.1</c>.
    /// </summary>
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlocked(address.MapToIPv4());
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            if (Nat64.Contains(address))
            {
                return IsBlocked(new IPAddress(bytes[12..16]));
            }

            if (SixToFour.Contains(address))
            {
                return IsBlocked(new IPAddress(bytes[2..6]));
            }

            // Old IPv4-compatible form, ::a.b.c.d. Nothing public uses it any more.
            if (bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0)
            {
                return true;
            }
        }

        return Ranges.Any(range => range.BaseAddress.AddressFamily == address.AddressFamily && range.Contains(address));
    }
}
