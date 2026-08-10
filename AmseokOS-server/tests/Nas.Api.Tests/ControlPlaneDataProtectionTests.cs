using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Nas.Infrastructure.Security;

namespace Nas.Api.Tests;

public sealed class ControlPlaneDataProtectionTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"adp-{Guid.NewGuid():N}");

    [Fact]
    public void LoadsAMatchingExternalCertificateAndPrivateKey()
    {
        Directory.CreateDirectory(directory);
        var certificatePath = Path.Combine(directory, "certificate.pem");
        var privateKeyPath = Path.Combine(directory, "private-key.pem");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=AmseokOS Test Data Protection",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        var configuration = Configuration(certificatePath, privateKeyPath);

        var loaded = ControlPlaneDataProtectionCertificate.Load(
            configuration,
            TimeProvider.System);

        using (loaded.Certificate)
        {
            Assert.True(loaded.Certificate.HasPrivateKey);
            Assert.Equal("AmseokOS.ControlPlane", loaded.Options.ApplicationName);
        }
    }

    [Fact]
    public void RejectsMissingExternalProtectionMaterial()
    {
        var configuration = Configuration(
            Path.Combine(directory, "missing-certificate.pem"),
            Path.Combine(directory, "missing-private-key.pem"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            ControlPlaneDataProtectionCertificate.Load(
                configuration,
                TimeProvider.System));

        Assert.Contains("does not exist", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IConfiguration Configuration(
        string certificatePath,
        string privateKeyPath) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:ApplicationName"] = "AmseokOS.ControlPlane",
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:PrivateKeyPath"] = privateKeyPath
        }).Build();
}
