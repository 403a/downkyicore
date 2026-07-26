using System;
using System.IO;
using Avalonia.Media.Imaging;
using QRCoder;

namespace DownKyi.Services.Account;

internal sealed class LoginQrCodeRenderer : ILoginQrCodeRenderer
{
    public Bitmap Render(Uri loginUri)
    {
        ArgumentNullException.ThrowIfNull(loginUri);
        if (!loginUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The login QR code URI must be absolute.", nameof(loginUri));
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            loginUri.AbsoluteUri,
            QRCodeGenerator.ECCLevel.H,
            forceUtf8: true,
            utf8BOM: false,
            eciMode: QRCodeGenerator.EciMode.Utf8,
            requestedVersion: 11);
        using var qrCode = new BitmapByteQRCode(data);
        using var stream = new MemoryStream(qrCode.GetGraphic(20));
        return new Bitmap(stream);
    }
}
