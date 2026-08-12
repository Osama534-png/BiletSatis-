using BiletSatis.Web.Models;

namespace BiletSatis.Tests;

// Yönetim panelindeki gelir/doluluk özetleri ve silme kilidi bu hesaplamalara dayanıyor.
public class AdminOzetTests
{
    private static AdminEtkinlikOzeti Ozet(int satista, int sepette, int satildi, decimal gelir = 0) =>
        Ozet(satista, sepette, satildi, gelir, DateTime.Now.AddDays(30));

    private static AdminEtkinlikOzeti Ozet(
        int satista, int sepette, int satildi, decimal gelir, DateTime tarih) => new()
    {
        Ad = "Test",
        Tarih = tarih,
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

    // Silme kuralı iki şeye birden bakar: satış var mı ve etkinlik geçti mi.
    //
    // GELECEK etkinlikte satılmış bilet varsa silinemez — insanların elinde
    // kullanacakları geçerli bilet var. SONA ERMİŞ etkinlikte silinebilir, çünkü
    // biletler artık kullanılamaz; arşiv temizliği yöneticinin kararıdır.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(13, false)]
    public void Silinebilir_GelecekEtkinliktePeSatisVarsaKapali(int satildi, bool beklenen)
    {
        Assert.Equal(beklenen, Ozet(10, 0, satildi).Silinebilir);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(13)]
    public void Silinebilir_SonaErmisEtkinlikteHerZamanAcik(int satildi)
    {
        var ozet = Ozet(10, 0, satildi, gelir: 0, tarih: DateTime.Now.AddDays(-1));

        Assert.True(ozet.SonaErdi);
        Assert.True(ozet.Silinebilir);
    }

    // Satış varken uyarı, ne kaybedileceğini açıkça söylemeli: yönetici "sil"e
    // basmadan önce satış kayıtlarının da gideceğini bilmeli.
    [Fact]
    public void SilmeUyarisi_SatisVarsaKaybedilecekleriSoylemeli()
    {
        var uyari = Ozet(0, 0, 7, gelir: 0, tarih: DateTime.Now.AddDays(-1)).SilmeUyarisi;

        Assert.Contains("7", uyari);
        Assert.Contains("kayd", uyari);
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
