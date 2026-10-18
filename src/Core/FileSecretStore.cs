using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StardewCodexChat.Core;

/// <summary>Authenticated AES-CBC credentials and a per-install random key, both confined to the Mod directory.</summary>
public sealed class FileSecretStore : ISecretStore
{
    private readonly string path;
    private readonly string keyPath;
    private readonly SemaphoreSlim gate = new(1, 1);
    public FileSecretStore(string path) { this.path = path; keyPath = Path.Combine(Path.GetDirectoryName(path)!, ".credential-key"); }

    public Task<string> GetKeyAsync(CancellationToken token) => Task.Run(async () =>
    {
        await gate.WaitAsync(token);
        try
        {
            if (new FileInfo(path).Length > 4096) throw new InvalidOperationException();
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
            var root = json.RootElement;
            if (root.TryGetProperty("ApiKey", out var legacy))
            {
                string value = Validate(legacy.GetString() ?? "");
                await WriteEncrypted(value, token); // Replace the old plaintext file; no plaintext backup.
                return value;
            }
            if (root.GetProperty("Version").GetInt32() != 1) throw new InvalidOperationException();
            if (new FileInfo(keyPath).Length != 32) throw new InvalidOperationException();
            byte[] key = await File.ReadAllBytesAsync(keyPath, token);
            byte[] plain = Array.Empty<byte>();
            try
            {
                byte[] ciphertext = Convert.FromBase64String(root.GetProperty("Ciphertext").GetString()!);
                if (root.GetProperty("Algorithm").GetString() != "AES-256-CBC-HMAC-SHA256") throw new InvalidOperationException();
                byte[] iv = Convert.FromBase64String(root.GetProperty("IV").GetString()!);
                byte[] tag = Convert.FromBase64String(root.GetProperty("Tag").GetString()!);
                byte[] encryptionKey = Derive(key, "encryption"), authenticationKey = Derive(key, "authentication");
                try
                {
                    byte[] expected = HMACSHA256.HashData(authenticationKey, iv.Concat(ciphertext).ToArray());
                    if (iv.Length != 16 || !CryptographicOperations.FixedTimeEquals(tag, expected)) throw new CryptographicException();
                    using var aes = Aes.Create(); aes.Key = encryptionKey; aes.IV = iv;
                    using var decryptor = aes.CreateDecryptor();
                    plain = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
                finally { CryptographicOperations.ZeroMemory(encryptionKey); CryptographicOperations.ZeroMemory(authenticationKey); }
                return Validate(Encoding.UTF8.GetString(plain));
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
        }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("尚未配置 Key 或凭据无法读取，请在游戏内重新配置（/resetkey）。"); }
        finally { gate.Release(); }
    }, token);

    public Task SaveKeyAsync(string value, CancellationToken token = default) => Task.Run(async () =>
    {
        value = Validate(value);
        await gate.WaitAsync(token);
        try { await WriteEncrypted(value, token); }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("Key 保存失败，请检查 Mod 文件夹的写入权限后重试。"); }
        finally { gate.Release(); }
    }, token);

    private async Task WriteEncrypted(string value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] key;
        if (File.Exists(keyPath))
        {
            key = new FileInfo(keyPath).Length == 32 ? await File.ReadAllBytesAsync(keyPath, token) : Array.Empty<byte>();
            if (key.Length != 32) { CryptographicOperations.ZeroMemory(key); key = RandomNumberGenerator.GetBytes(32); await AtomicWrite(keyPath, key, token); }
        }
        else { key = RandomNumberGenerator.GetBytes(32); await AtomicWrite(keyPath, key, token); }
        byte[] plain = Encoding.UTF8.GetBytes(value);
        try
        {
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            byte[] encryptionKey = Derive(key, "encryption"), authenticationKey = Derive(key, "authentication");
            byte[] json;
            try
            {
                using var aes = Aes.Create(); aes.Key = encryptionKey; aes.IV = iv;
                using var encryptor = aes.CreateEncryptor();
                byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                byte[] tag = HMACSHA256.HashData(authenticationKey, iv.Concat(cipher).ToArray());
                json = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Algorithm = "AES-256-CBC-HMAC-SHA256", IV = Convert.ToBase64String(iv),
                    Tag = Convert.ToBase64String(tag), Ciphertext = Convert.ToBase64String(cipher) });
            }
            finally { CryptographicOperations.ZeroMemory(encryptionKey); CryptographicOperations.ZeroMemory(authenticationKey); }
            Restrict(keyPath);
            await AtomicWrite(path, json, token);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private static byte[] Derive(byte[] key, string purpose) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("StardewCopilot credential v1 " + purpose));

    public static string Validate(string value)
    {
        value = value.Trim();
        if (value.Length is < 10 or > 512 || value.Any(c => c < 33 || c > 126))
            throw new InvalidOperationException("请输入完整的 API Key，长度需为 10–512 个字符，不能包含空格或换行。");
        return value;
    }
    private static async Task AtomicWrite(string destination, byte[] bytes, CancellationToken token)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Restrict(temporary); // Restrict before writing any sensitive bytes.
                await file.WriteAsync(bytes, token);
                await file.FlushAsync(token);
                file.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int chmod(string path, uint mode);
    private static void Restrict(string file) { if (!OperatingSystem.IsWindows() && chmod(file, 0x180) != 0) throw new IOException(); }
}
