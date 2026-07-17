using System.Security.Cryptography;

namespace CquVpnCore.Identity;

public sealed class TerminalIdentityFactory
{
    public TerminalIdentity Create(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            throw new InvalidOperationException($"A terminal identity already exists for '{keyName}'.");
        }

        var parameters = new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None
        };
        using var key = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, parameters);
        using var algorithm = new ECDsaCng(key);
        var publicKey = algorithm.ExportSubjectPublicKeyInfo();

        return new TerminalIdentity(
            keyName,
            publicKey,
            Convert.ToHexString(SHA256.HashData(publicKey)));
    }
}
