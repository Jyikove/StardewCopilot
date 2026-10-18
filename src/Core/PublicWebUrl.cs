namespace StardewCodexChat.Core;

public static class PublicWebUrl
{
    public static bool TryNormalize(string? value,out string url)
    {
        url="";
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0
            ||uri.HostNameType!=UriHostNameType.Dns||!uri.Host.Contains('.')||uri.Host.EndsWith('.')
            ||new[]{".localhost",".local",".internal",".test",".invalid"}.Any(uri.Host.EndsWith))return false;
        var builder=new UriBuilder(uri){Fragment=""};url=builder.Uri.AbsoluteUri;return true;
    }
}
