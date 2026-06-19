namespace BonjourrIconStudio.Models;

public sealed class TokenVaultModel
{
    public int Version { get; set; } = 1;
    public required string TokenNonce { get; set; }
    public required string TokenCiphertext { get; set; }
    public required string TokenTag { get; set; }
    public required string DpapiWrappedDataKey { get; set; }
    public required string RecoverySalt { get; set; }
    public required string RecoveryNonce { get; set; }
    public required string RecoveryWrappedDataKey { get; set; }
    public required string RecoveryTag { get; set; }
}
