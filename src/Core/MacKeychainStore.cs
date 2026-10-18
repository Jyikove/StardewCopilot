using System.Runtime.InteropServices;
using System.Text;

namespace StardewCodexChat.Core;

public interface ISecretStore
{
    Task<string> GetKeyAsync(CancellationToken token);
}

/// <summary>Read a generic password through Security.framework; never invoke a shell or log a key.</summary>
public sealed class MacKeychainStore : ISecretStore
{
    public const string Service = "StardewCopilot.DeepSeek";
    public const string Account = "api-key";
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly Lazy<IntPtr> SecurityLibrary = new(() => NativeLibrary.Load(Security));
    private static readonly Lazy<IntPtr> CoreLibrary = new(() => NativeLibrary.Load(CoreFoundation));
    private readonly string serviceName;
    private readonly string accountName;

    public MacKeychainStore() : this(Service, Account) { }
    internal MacKeychainStore(string service, string account) { serviceName = service; accountName = account; }

    public Task<string> GetKeyAsync(CancellationToken token) => Task.Run(Read, token).WaitAsync(token);

    private string Read()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("当前 Key 存储仅支持 macOS 钥匙串。");
        IntPtr service = CFStringCreateWithCString(IntPtr.Zero, serviceName, 0x08000100);
        IntPtr account = CFStringCreateWithCString(IntPtr.Zero, accountName, 0x08000100);
        IntPtr query = CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        IntPtr result = IntPtr.Zero;
        try
        {
            CFDictionarySetValue(query, Symbol("kSecClass"), Symbol("kSecClassGenericPassword"));
            CFDictionarySetValue(query, Symbol("kSecAttrService"), service);
            CFDictionarySetValue(query, Symbol("kSecAttrAccount"), account);
            CFDictionarySetValue(query, Symbol("kSecReturnData"), Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreLibrary.Value, "kCFBooleanTrue")));
            CFDictionarySetValue(query, Symbol("kSecMatchLimit"), Symbol("kSecMatchLimitOne"));
            int status = SecItemCopyMatching(query, out result);
            if (status == -25300)
                throw new InvalidOperationException("尚未配置 DeepSeek Key。请先双击 Mod 文件夹中的“配置 DeepSeek Key.command”，保存 Key 后重试。");
            if (status != 0 || result == IntPtr.Zero)
                throw new InvalidOperationException($"无法读取 DeepSeek 钥匙串凭据（系统代码 {status}）。如系统弹出访问请求，请允许 StardewModdingAPI 访问该条目。");
            nint length = CFDataGetLength(result);
            if (length is < 10 or > 512) throw new InvalidOperationException("DeepSeek Key 长度无效，请重新配置。");
            byte[] data = new byte[(int)length];
            try { Marshal.Copy(CFDataGetBytePtr(result), data, 0, data.Length); return Encoding.UTF8.GetString(data); }
            finally { Array.Clear(data, 0, data.Length); }
        }
        finally
        {
            if (result != IntPtr.Zero) CFRelease(result);
            if (query != IntPtr.Zero) CFRelease(query);
            if (account != IntPtr.Zero) CFRelease(account);
            if (service != IntPtr.Zero) CFRelease(service);
        }
    }

    private static IntPtr Symbol(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityLibrary.Value, name));
    [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
