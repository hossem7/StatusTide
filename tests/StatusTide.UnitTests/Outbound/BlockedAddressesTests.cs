using System.Net;

using StatusTide.Common.Outbound;

namespace StatusTide.UnitTests.Outbound;

public class BlockedAddressesTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // cloud metadata service
    [InlineData("169.254.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("192.0.2.10")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fc00::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:0:4136:e378::1")] // Teredo
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("::127.0.0.1")] // old IPv4-compatible form
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64 for 169.254.169.254
    [InlineData("2002:7f00:1::1")] // 6to4 for 127.0.0.1
    [InlineData("2002:c0a8:101::1")] // 6to4 for 192.168.1.1
    public void Private_loopback_link_local_and_reserved_addresses_are_blocked(string address)
    {
        Assert.True(BlockedAddresses.IsBlocked(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")] // just past 172.16.0.0/12
    [InlineData("172.15.255.255")]
    [InlineData("100.128.0.1")] // just past 100.64.0.0/10
    [InlineData("169.255.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")] // NAT64 for 8.8.8.8
    [InlineData("2002:808:808::1")] // 6to4 for 8.8.8.8
    public void Public_addresses_are_allowed(string address)
    {
        Assert.False(BlockedAddresses.IsBlocked(IPAddress.Parse(address)));
    }
}
