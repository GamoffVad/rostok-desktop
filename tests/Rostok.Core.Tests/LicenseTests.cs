using System.Security.Cryptography;
using Rostok.Licensing;

namespace Rostok.Core.Tests;

public class LicenseTests
{
    private static string? RepoPrivateKey()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var p = Path.Combine(dir, "keys", "rostok-license.private.pem");
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        return null;
    }

    [Fact]
    public void Base32RoundTrip()
    {
        var data = RandomNumberGenerator.GetBytes(64);
        Assert.Equal(data, Base32.Decode(Base32.Encode(data))[..64]);
        Assert.Equal("01", Base32.Clean("o-i"));
    }

    [Fact]
    public void MachineKeyIsStableAndWellFormed()
    {
        var a = License.MachineKey();
        Assert.Equal(a, License.MachineKey());
        Assert.StartsWith("RSTK-", a);
        Assert.True(License.IsKeyWellFormed(a));
        Assert.True(License.IsKeyWellFormed(a.ToLowerInvariant().Replace("-", " ")));
    }

    [Fact]
    public void SerialFromOtherKeyPairIsRejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = other.ExportECPrivateKeyPem();
        var key = License.MachineKey();
        Assert.False(License.MatchesPublicKey(pem));
        Assert.False(License.Verify(key, License.Sign(key, pem)));
    }

    [Fact]
    public void SerialFromAdminKeyWorksOnlyForItsComputer()
    {
        var pem = RepoPrivateKey();
        if (pem is null) return; // секретного ключа нет на сборочной машине — проверка пропускается
        Assert.True(License.MatchesPublicKey(pem));
        var key = License.MachineKey();
        var serial = License.Sign(key, pem);
        Assert.True(License.Verify(key, serial));
        Assert.True(License.Verify(key.ToLowerInvariant(), serial.Replace("-", "")));
        var otherKey = "RSTK-" + string.Join("-", Base32.Encode(RandomNumberGenerator.GetBytes(15)).Chunk(4).Select(c => new string(c)));
        Assert.False(License.Verify(otherKey, serial));
        var tampered = (serial[0] == 'A' ? "B" : "A") + serial[1..];
        Assert.False(License.Verify(key, tampered));
    }
}
