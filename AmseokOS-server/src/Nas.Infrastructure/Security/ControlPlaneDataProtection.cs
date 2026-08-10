//--------------------------//
//--------加载数据库密钥环使用的外部证书保护材料---------//
//--------Loads external certificate material protecting the database key ring--------//
//-------------------------//

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace Nas.Infrastructure.Security;

public sealed class ControlPlaneDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; init; } = "AmseokOS.ControlPlane";

    public string CertificatePath { get; init; } = string.Empty;

    public string PrivateKeyPath { get; init; } = string.Empty;
}

internal static class ControlPlaneDataProtectionCertificate
{
    public static (ControlPlaneDataProtectionOptions Options, X509Certificate2 Certificate)
        Load(IConfiguration configuration, TimeProvider timeProvider)
    {
        var options = configuration
            .GetSection(ControlPlaneDataProtectionOptions.SectionName)
            .Get<ControlPlaneDataProtectionOptions>()
            ?? new ControlPlaneDataProtectionOptions();

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            throw new InvalidOperationException(
                "DataProtection:ApplicationName is required");
        }
        ValidateAbsoluteFile(options.CertificatePath, "CertificatePath");
        ValidateAbsoluteFile(options.PrivateKeyPath, "PrivateKeyPath");

        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPemFile(
                options.CertificatePath,
                options.PrivateKeyPath);
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException(
                "The configured Data Protection certificate could not be loaded",
                error);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "The configured Data Protection certificate has no private key");
        }
        if (new DateTimeOffset(certificate.NotAfter.ToUniversalTime())
            <= timeProvider.GetUtcNow())
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "The configured Data Protection certificate has expired");
        }

        return (options, certificate);
    }

    private static void ValidateAbsoluteFile(string path, string optionName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"DataProtection:{optionName} must be an absolute path");
        }
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"DataProtection:{optionName} does not exist");
        }
    }
}
