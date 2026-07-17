namespace CquVpnCore.Identity;

public sealed record TerminalIdentity(
    string KeyName,
    byte[] SubjectPublicKeyInfo,
    string PublicKeySha256);
