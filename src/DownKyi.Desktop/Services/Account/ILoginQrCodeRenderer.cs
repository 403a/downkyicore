using System;
using Avalonia.Media.Imaging;

namespace DownKyi.Services.Account;

internal interface ILoginQrCodeRenderer
{
    Bitmap Render(Uri loginUri);
}
