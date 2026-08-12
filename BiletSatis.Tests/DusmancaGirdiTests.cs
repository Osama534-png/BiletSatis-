using System.Net;

namespace BiletSatis.Tests;

/// <summary>
/// Hiçbir uç, bozuk ya da düşmanca girdiyle 500 döndürmemeli.
///
/// Sunucu hatası iki şekilde zarar verir: kullanıcı ne olduğunu anlamaz ve
/// yığın izi (stack trace) sızabilir. Beklenen davranış, geçersiz girdiyi
/// anlaşılır bir cevapla karşılamak — 404, 302 ya da boş sonuç.
///
/// Bu testler tek tek hata aramaz; sınır değerleri, tip uyuşmazlıklarını ve
/// enjeksiyon denemelerini toplu geçirir. Amaç bir kez düzeltmek değil,
/// yeni uç eklendiğinde aynı standardın korunması.
/// </summary>
[Collection("Veritabanı")]
public class DusmancaGirdiTests : IClassFixture<UygulamaFabrikasi>
{
    private readonly UygulamaFabrikasi _fabrika;

    public DusmancaGirdiTests(UygulamaFabrikasi fabrika) => _fabrika = fabrika;

    private static string BenzersizEposta(string on) => $"{on}-{Guid.NewGuid():N}@test.local";

    public static TheoryData<string> Adresler() => new()
    {
        // --- Kayıt numarası: yok, sıfır, negatif, sınır, harf ---
        "/Etkinlik/Detay?id=0",
        "/Etkinlik/Detay?id=-1",
        "/Etkinlik/Detay?id=2147483647",
        "/Etkinlik/Detay?id=abc",
        "/Etkinlik/Detay",
        "/Biletler/Index?etkinlikId=0",
        "/Biletler/Index?etkinlikId=-99",
        "/Biletler/Index?etkinlikId=2147483647",
        "/Biletler/Index",
        "/Kuyruk/Durum?etkinlikId=-1",
        "/Kuyruk/Durum?etkinlikId=2147483647",

        // --- Sayfalama sınırları ---
        "/?sayfa=0",
        "/?sayfa=-1",
        "/?sayfa=2147483647",
        "/?sayfa=abc",
        "/?sayfa=1.5",

        // --- Filtre alanlarına tip uyuşmazlığı ---
        "/?enYuksekFiyat=abc",
        "/?enYuksekFiyat=-500",
        "/?enYuksekFiyat=99999999999999999999",
        "/?kategori=BoyleBirKategoriYok",
        "/?kategori=999",
        "/?siralama=uydurma-siralama",
        "/?tarih=uydurma-tarih",
        "/?tukenenleriGoster=belki",

        // --- Metin alanlarına enjeksiyon ve aşırı uzunluk denemeleri ---
        "/?arama=%3Cscript%3Ealert(1)%3C/script%3E",
        "/?arama=%27%20OR%201%3D1--",
        "/?arama=%25",
        "/?arama=_",
        "/?arama=%5B",
        "/?sehir=%27%3B%20DROP%20TABLE%20Etkinlikler%3B--",

        // --- Kapı kontrolü kodu (yetkisiz kullanıcı; giriş sayfasına dönmeli) ---
        "/Giris/Dogrula?kod=",
        "/Giris/Dogrula?kod=abc.def.ghi",
        "/Giris/Dogrula?kod=1.1.1",
        "/Giris/Dogrula?kod=-5.1.aaaaaaaaaaaaaaaa",

        // --- Profil e-posta onayı ---
        "/Profil/EpostaDegisikliginiOnayla",
        "/Profil/EpostaDegisikliginiOnayla?kullaniciId=yok&yeniEposta=a@b.c&jeton=bozuk",
    };

    [Theory]
    [MemberData(nameof(Adresler))]
    public async Task Hicbir_Uc_SunucuHatasi_Dondurmemeli(string adres)
    {
        var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("dusmanca"));

        var cevap = await istemci.GetAsync(adres);

        Assert.True(
            (int)cevap.StatusCode < 500,
            $"{adres} → {(int)cevap.StatusCode} {cevap.StatusCode}");
    }

    /// <summary>
    /// Aynı kural POST uçları için de geçerli. Antiforgery jetonu doğru, içerik bozuk:
    /// böylece 400 (jeton reddi) değil, gerçek model bağlama davranışı ölçülür.
    /// </summary>
    [Theory]
    [InlineData("/Biletler/SepeteEkle", "etkinlikId=-1&biletIds=-1")]
    [InlineData("/Biletler/SepeteEkle", "etkinlikId=abc&biletIds=abc")]
    [InlineData("/Biletler/SepeteEkle", "etkinlikId=1")]
    [InlineData("/Biletler/GenelGirisSepeteEkle", "etkinlikId=-1&adet=-5")]
    [InlineData("/Biletler/GenelGirisSepeteEkle", "etkinlikId=1&adet=2147483647")]
    [InlineData("/Biletler/IptalEt", "biletId=-1")]
    [InlineData("/Biletler/Devret", "biletId=-1&aliciEposta=gecersiz")]
    [InlineData("/Biletler/Devret", "biletId=1&aliciEposta=")]
    [InlineData("/Kuyruk/Katil", "etkinlikId=-1")]
    [InlineData("/Favori/Degistir", "etkinlikId=-1")]
    [InlineData("/Favori/Degistir", "etkinlikId=2147483647")]
    [InlineData("/Etkinlik/Degerlendir", "etkinlikId=-1&puan=99&yorum=x")]
    [InlineData("/Etkinlik/Degerlendir", "etkinlikId=1&puan=-5")]
    public async Task Hicbir_PostUcu_SunucuHatasi_Dondurmemeli(string adres, string govde)
    {
        var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("dusmanca-post"));

        // Jetonu herhangi bir formdan alıyoruz; amaç antiforgery'yi değil,
        // action'ın bozuk veriye tepkisini ölçmek.
        var sayfa = await istemci.GetStringAsync("/Profil");
        var jeton = UygulamaFabrikasi.AntiforgeryJetonu(sayfa);

        var icerik = new StringContent(
            $"{govde}&__RequestVerificationToken={Uri.EscapeDataString(jeton)}",
            System.Text.Encoding.UTF8,
            "application/x-www-form-urlencoded");

        var cevap = await istemci.PostAsync(adres, icerik);

        Assert.True(
            (int)cevap.StatusCode < 500,
            $"POST {adres} ({govde}) → {(int)cevap.StatusCode} {cevap.StatusCode}");
    }
}
