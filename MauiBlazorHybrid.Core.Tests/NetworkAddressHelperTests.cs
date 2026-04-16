using System.Net;
using MauiBlazorHybrid.Core;

namespace MauiBlazorHybrid.Core.Tests;

public class NetworkAddressHelperTests
{
    #region Null argument

    [Fact]
    public void IsPrivateAddress_NullAddress_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => NetworkAddressHelper.IsPrivateAddress(null!));
    }

    #endregion

    #region IPv4 – Loopback

    [Fact]
    public void IsPrivateAddress_Loopback127_0_0_1_ReturnsTrue()
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("127.0.0.1")));
    }

    [Fact]
    public void IsPrivateAddress_Loopback127_255_255_255_ReturnsTrue()
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("127.255.255.255")));
    }

    #endregion

    #region IPv4 – 10.0.0.0/8

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("10.123.45.67")]
    public void IsPrivateAddress_ClassA_Private_ReturnsTrue(string ip)
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region IPv4 – 172.16.0.0/12

    [Theory]
    [InlineData("172.16.0.0")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("172.20.10.5")]
    public void IsPrivateAddress_ClassB_Private_ReturnsTrue(string ip)
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    public void IsPrivateAddress_ClassB_Outside_ReturnsFalse(string ip)
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region IPv4 – 192.168.0.0/16

    [Theory]
    [InlineData("192.168.0.0")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    [InlineData("192.168.1.100")]
    public void IsPrivateAddress_ClassC_Private_ReturnsTrue(string ip)
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPrivateAddress_192_169_0_1_ReturnsFalse()
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("192.169.0.1")));
    }

    #endregion

    #region IPv4 – 169.254.0.0/16 (Link-local)

    [Theory]
    [InlineData("169.254.0.0")]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.255.255")]
    public void IsPrivateAddress_LinkLocal_ReturnsTrue(string ip)
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region IPv4 – Public addresses

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("142.250.74.46")]
    [InlineData("203.0.113.1")]
    [InlineData("11.0.0.1")]
    public void IsPrivateAddress_PublicIPv4_ReturnsFalse(string ip)
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region IPv6 – Loopback

    [Fact]
    public void IsPrivateAddress_IPv6Loopback_ReturnsTrue()
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.IPv6Loopback));
    }

    #endregion

    #region IPv6 – Link-local (fe80::/10)

    [Theory]
    [InlineData("fe80::1")]
    [InlineData("fe80::abcd:ef01:2345:6789")]
    public void IsPrivateAddress_IPv6LinkLocal_ReturnsTrue(string ip)
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region IPv6 – Unique local (fc00::/7)

    [Theory]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("fdab:cdef:1234::1")]
    public void IsPrivateAddress_IPv6UniqueLocal_ReturnsTrue(string ip)
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region IPv6 – Public addresses

    [Theory]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2607:f8b0:4004:800::200e")]
    public void IsPrivateAddress_PublicIPv6_ReturnsFalse(string ip)
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    #endregion

    #region Edge cases – boundary values

    [Fact]
    public void IsPrivateAddress_172_16_0_0_Boundary_ReturnsTrue()
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("172.16.0.0")));
    }

    [Fact]
    public void IsPrivateAddress_172_31_255_255_Boundary_ReturnsTrue()
    {
        Assert.True(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("172.31.255.255")));
    }

    [Fact]
    public void IsPrivateAddress_172_15_255_255_JustBelow_ReturnsFalse()
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("172.15.255.255")));
    }

    [Fact]
    public void IsPrivateAddress_172_32_0_0_JustAbove_ReturnsFalse()
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("172.32.0.0")));
    }

    [Fact]
    public void IsPrivateAddress_0_0_0_0_ReturnsFalse()
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("0.0.0.0")));
    }

    [Fact]
    public void IsPrivateAddress_255_255_255_255_ReturnsFalse()
    {
        Assert.False(NetworkAddressHelper.IsPrivateAddress(IPAddress.Parse("255.255.255.255")));
    }

    #endregion
}
