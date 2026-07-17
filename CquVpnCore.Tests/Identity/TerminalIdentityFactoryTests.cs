using System.Security.Cryptography;
using CquVpnCore.Identity;
using Xunit;

namespace CquVpnCore.Tests.Identity;

public sealed class TerminalIdentityFactoryTests
{
    [Fact]
    public void Create_generates_a_current_user_P256_identity_that_can_sign_and_verify()
    {
        var keyName = $"CquVpnCore.Tests.{Guid.NewGuid():N}";
        var factory = new TerminalIdentityFactory();

        try
        {
            var identity = factory.Create(keyName);

            Assert.Equal(keyName, identity.KeyName);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(identity.SubjectPublicKeyInfo)),
                identity.PublicKeySha256);
            Assert.NotEmpty(identity.SubjectPublicKeyInfo);

            using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            Assert.Equal(CngAlgorithm.ECDsaP256.Algorithm, key.Algorithm.Algorithm);
            Assert.False(key.ExportPolicy.HasFlag(CngExportPolicies.AllowPlaintextExport));
            using var algorithm = new ECDsaCng(key);
            var payload = new byte[] { 1, 2, 3, 4 };
            var signature = algorithm.SignData(payload, HashAlgorithmName.SHA256);
            Assert.True(algorithm.VerifyData(payload, signature, HashAlgorithmName.SHA256));
        }
        finally
        {
            DeleteKeyIfPresent(keyName);
        }
    }

    private static void DeleteKeyIfPresent(string keyName)
    {
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            key.Delete();
        }
    }
}
