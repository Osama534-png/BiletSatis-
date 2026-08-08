using QRCoder;

namespace BiletSatis.Web.Services.Eposta;

public interface IQrKodUretici
{
    byte[] PngUret(string icerik);
}

public class QrKodUretici : IQrKodUretici
{
    public byte[] PngUret(string icerik)
    {
        using var uretici = new QRCodeGenerator();

        // ECC seviyesi Q: kodun dörtte biri okunamasa bile (baskı lekesi, ekran
        // parlaması) veri kurtarılabilir.
        using var veri = uretici.CreateQrCode(icerik, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(veri);

        return png.GetGraphic(pixelsPerModule: 8);
    }
}
