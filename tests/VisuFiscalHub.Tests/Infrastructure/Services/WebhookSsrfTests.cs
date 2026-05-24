using System.Net;
using System.Reflection;
using Shouldly;
using VisuFiscalHub.Infrastructure.Services;

namespace VisuFiscalHub.Tests.Infrastructure.Services;

public class WebhookSsrfTests
{
    private static bool IsInBlockedRange(string ip)
    {
        var method = typeof(WebhookDeliveryService)
            .GetMethod("IsInBlockedRange", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [IPAddress.Parse(ip)])!;
    }

    // ── IPv4 bloqueados ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.0")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    [InlineData("127.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("169.254.0.0")]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.255.255")]
    public void IsInBlockedRange_IPv4Privado_Bloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeTrue();

    // ── RFC 6598 (Shared Address Space / CGN) ───────────────────────────────────

    [Theory]
    [InlineData("100.64.0.0")]   // início do bloco
    [InlineData("100.64.0.1")]
    [InlineData("100.100.100.100")]
    [InlineData("100.127.255.255")] // fim do bloco
    public void IsInBlockedRange_Rfc6598CgnBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeTrue();

    [Theory]
    [InlineData("100.63.255.255")]  // um antes do bloco
    [InlineData("100.128.0.0")]     // um depois do bloco
    public void IsInBlockedRange_Rfc6598Fronteira_NaoBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeFalse();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("203.0.113.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    public void IsInBlockedRange_IPv4Publico_NaoBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeFalse();

    // ── IPv6 bloqueados ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("fdff:ffff::1")]
    [InlineData("fe80::1")]
    [InlineData("fe80::ffff")]
    [InlineData("febf::1")]
    [InlineData("ff00::1")]
    [InlineData("ffff::1")]
    [InlineData("2002:0a00::1")]
    [InlineData("2002:c0a8::1")]
    public void IsInBlockedRange_IPv6Privado_Bloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeTrue();

    [Theory]
    [InlineData("2001:db8::1")]
    [InlineData("2606:4700::1")]
    public void IsInBlockedRange_IPv6Publico_NaoBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeFalse();

    // ── IPv4-mapped-to-IPv6 (normalização) ──────────────────────────────────────

    [Theory]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:192.168.1.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.0.1")]
    public void IsInBlockedRange_IPv4MappedIPv6Privado_Bloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeTrue();

    [Theory]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("::ffff:1.1.1.1")]
    public void IsInBlockedRange_IPv4MappedIPv6Publico_NaoBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeFalse();
}
