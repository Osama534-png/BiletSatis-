using BiletSatis.Web.Models;

namespace BiletSatis.Tests;

// Yönetim panelindeki gelir/doluluk özetleri ve silme kilidi bu hesaplamalara dayanıyor.
public class AdminOzetTests
{
    private static AdminEtkinlikOzeti Ozet(int satista, int sepette, int satildi, decimal gelir = 0) => new()
    {
        Ad = "Test",
        SatistaSayisi = satista,
        SepetteSayisi = sepette,
        SatildiSayisi = satildi,
        Gelir = gelir
    };

    [Fact]
    public void ToplamKoltuk_UcDurumunToplamidir()
    {
        Assert.Equal(60, Ozet(satista: 30, sepette: 10, satildi: 20).ToplamKoltuk);
    }

    [Theory]
    [InlineData(30, 10, 20, 33)]   // 20/60
    [InlineData(0, 0, 40, 100)]    // tamamı satıldı
    [InlineData(40, 0, 0, 0)]      // hiç satılmadı
    public void DolulukYuzdesi_SatilaninOranidir(int satista, int sepette, int satildi, int beklenen)
    {
        Assert.Equal(beklenen, Ozet(satista, sepette, satildi).DolulukYuzdesi);
    }

    // Bileti olmayan etkinlikte sıfıra bölme olmamalı.
    [Fact]
    public void DolulukYuzdesi_KoltukYoksaSifir()
    {
        Assert.Equal(0, Ozet(0, 0, 0).DolulukYuzdesi);
    }

    // Satılmış bilet gerçek bir satın alma kaydı; etkinlik silinememeli.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(13, false)]
    public void Silinebilir_SatilmisBiletVarsaKapali(int satildi, bool beklenen)
    {
        Assert.Equal(beklenen, Ozet(10, 0, satildi).Silinebilir);
    }

    [Fact]
    public void PanelOzeti_EtkinliklerinToplaminiAlir()
    {
        var panel = new AdminPanelVm
        {
            Etkinlikler =
            [
                Ozet(satista: 10, sepette: 2, satildi: 8, gelir: 4000m),
                Ozet(satista: 20, sepette: 0, satildi: 10, gelir: 6000m)
            ]
        };

        Assert.Equal(2, panel.ToplamEtkinlik);
        Assert.Equal(18, panel.ToplamSatilan);
        Assert.Equal(30, panel.ToplamSatista);
        Assert.Equal(10_000m, panel.ToplamGelir);
        Assert.Equal(50, panel.ToplamKoltuk);
        Assert.Equal(36, panel.DolulukYuzdesi);   // 18/50
    }

    [Fact]
    public void PanelOzeti_EtkinlikYoksaSifirDondurur()
    {
        var panel = new AdminPanelVm();

        Assert.Equal(0, panel.ToplamEtkinlik);
        Assert.Equal(0m, panel.ToplamGelir);
        Assert.Equal(0, panel.DolulukYuzdesi);
    }
}
