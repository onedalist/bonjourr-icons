using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BonjourrIconStudio.Models;
using Konscious.Security.Cryptography;

namespace BonjourrIconStudio.Services;

public sealed class TokenVaultService
{
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("BonjourrIconStudio.TokenVault.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool HasSavedToken => File.Exists(PortablePaths.VaultFile);

    public async Task SaveTokenAsync(string token, string recoveryPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryPassword);
        PortablePaths.EnsureFolders();

        var dataKey = RandomNumberGenerator.GetBytes(32);
        var tokenNonce = RandomNumberGenerator.GetBytes(12);
        var tokenBytes = Encoding.UTF8.GetBytes(token.Trim());
        var tokenCipher = new byte[tokenBytes.Length];
        var tokenTag = new byte[16];

        using (var aes = new AesGcm(dataKey, 16))
            aes.Encrypt(tokenNonce, tokenBytes, tokenCipher, tokenTag, DpapiEntropy);

        var dpapiWrappedKey = ProtectedData.Protect(dataKey, DpapiEntropy, DataProtectionScope.CurrentUser);

        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        var recoveryKey = await DeriveRecoveryKeyAsync(recoveryPassword, recoverySalt);
        var recoveryNonce = RandomNumberGenerator.GetBytes(12);
        var recoveryWrappedKey = new byte[dataKey.Length];
        var recoveryTag = new byte[16];

        using (var aes = new AesGcm(recoveryKey, 16))
            aes.Encrypt(recoveryNonce, dataKey, recoveryWrappedKey, recoveryTag, DpapiEntropy);

        var model = new TokenVaultModel
        {
            TokenNonce = Convert.ToBase64String(tokenNonce),
            TokenCiphertext = Convert.ToBase64String(tokenCipher),
            TokenTag = Convert.ToBase64String(tokenTag),
            DpapiWrappedDataKey = Convert.ToBase64String(dpapiWrappedKey),
            RecoverySalt = Convert.ToBase64String(recoverySalt),
            RecoveryNonce = Convert.ToBase64String(recoveryNonce),
            RecoveryWrappedDataKey = Convert.ToBase64String(recoveryWrappedKey),
            RecoveryTag = Convert.ToBase64String(recoveryTag)
        };

        File.WriteAllText(PortablePaths.VaultFile, JsonSerializer.Serialize(model, JsonOptions));

        CryptographicOperations.ZeroMemory(dataKey);
        CryptographicOperations.ZeroMemory(recoveryKey);
        CryptographicOperations.ZeroMemory(tokenBytes);
    }

    public bool TryLoadWithWindows(out string token)
    {
        token = string.Empty;
        var model = ReadModel();
        if (model is null) return false;

        try
        {
            var dataKey = ProtectedData.Unprotect(
                Convert.FromBase64String(model.DpapiWrappedDataKey),
                DpapiEntropy,
                DataProtectionScope.CurrentUser);

            token = DecryptToken(model, dataKey);
            CryptographicOperations.ZeroMemory(dataKey);
            return !string.IsNullOrWhiteSpace(token);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public async Task<string?> UnlockWithRecoveryPasswordAsync(string recoveryPassword)
    {
        var model = ReadModel();
        if (model is null) return null;

        byte[]? recoveryKey = null;
        byte[]? dataKey = null;

        try
        {
            var salt = Convert.FromBase64String(model.RecoverySalt);
            recoveryKey = await DeriveRecoveryKeyAsync(recoveryPassword, salt);
            dataKey = new byte[32];

            using (var aes = new AesGcm(recoveryKey, 16))
            {
                aes.Decrypt(
                    Convert.FromBase64String(model.RecoveryNonce),
                    Convert.FromBase64String(model.RecoveryWrappedDataKey),
                    Convert.FromBase64String(model.RecoveryTag),
                    dataKey,
                    DpapiEntropy);
            }

            var token = DecryptToken(model, dataKey);

            model.DpapiWrappedDataKey = Convert.ToBase64String(
                ProtectedData.Protect(dataKey, DpapiEntropy, DataProtectionScope.CurrentUser));
            File.WriteAllText(PortablePaths.VaultFile, JsonSerializer.Serialize(model, JsonOptions));

            return token;
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            if (recoveryKey is not null) CryptographicOperations.ZeroMemory(recoveryKey);
            if (dataKey is not null) CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public void DeleteToken()
    {
        if (File.Exists(PortablePaths.VaultFile))
            File.Delete(PortablePaths.VaultFile);
    }

    public void ExportProfile(string destinationPath)
    {
        if (!HasSavedToken)
            throw new InvalidOperationException("Сохранённый профиль не найден.");

        File.Copy(PortablePaths.VaultFile, destinationPath, true);
    }

    public void ImportProfile(string sourcePath)
    {
        PortablePaths.EnsureFolders();
        _ = JsonSerializer.Deserialize<TokenVaultModel>(File.ReadAllText(sourcePath))
            ?? throw new InvalidDataException("Файл профиля повреждён.");
        File.Copy(sourcePath, PortablePaths.VaultFile, true);
    }

    private static async Task<byte[]> DeriveRecoveryKeyAsync(string password, byte[] salt)
    {
        var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = 2,
            Iterations = 3,
            MemorySize = 64 * 1024
        };

        return await argon.GetBytesAsync(32);
    }

    private static string DecryptToken(TokenVaultModel model, byte[] dataKey)
    {
        var cipher = Convert.FromBase64String(model.TokenCiphertext);
        var plaintext = new byte[cipher.Length];

        using (var aes = new AesGcm(dataKey, 16))
        {
            aes.Decrypt(
                Convert.FromBase64String(model.TokenNonce),
                cipher,
                Convert.FromBase64String(model.TokenTag),
                plaintext,
                DpapiEntropy);
        }

        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static TokenVaultModel? ReadModel()
    {
        if (!File.Exists(PortablePaths.VaultFile)) return null;

        try
        {
            return JsonSerializer.Deserialize<TokenVaultModel>(File.ReadAllText(PortablePaths.VaultFile));
        }
        catch
        {
            return null;
        }
    }
}
