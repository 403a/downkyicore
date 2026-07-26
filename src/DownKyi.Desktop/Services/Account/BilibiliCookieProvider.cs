using DownKyi.Application.Bilibili;
using DownKyi.Core.BiliApi.Login;

namespace DownKyi.Services.Account;

internal sealed class BilibiliCookieProvider : IBilibiliCookieProvider
{
    public string GetCookieHeader()
    {
        return LoginHelper.GetLoginInfoCookiesString();
    }
}
