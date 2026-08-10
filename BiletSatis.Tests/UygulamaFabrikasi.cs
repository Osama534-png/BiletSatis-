using BiletSatis.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BiletSatis.Tests;

/// <summary>
/// Uygulamanın tamamını (yönlendirme, kimlik doğrulama, filtreler, Razor görünümleri)
/// bellek içi bir test sunucusunda ayağa kaldırır. Servisleri tek tek çağıran birim
/// testlerinden farkı, isteğin gerçek boru hattından geçmesi: yetkilendirme, antiforgery
/// ve model bağlama dahil. Giriş gerektiren akışlar ancak böyle test edilebiliyor.
/// </summary>
public class UygulamaFabrikasi : WebApplicationFactory<Program>
{
    /// <summary>Testlerde oluşturulan hesapların şifresi. Yalnızca test veritabanında geçerlidir.</summary>
    public const string TestSifresi = "TestSifre123!";

    static UygulamaFabrikasi()
    {
        // Ayarlar ortam değişkeniyle veriliyor, ConfigureAppConfiguration ile değil.
        // Sebebi: Program.cs bu değerleri `builder` kurulurken okuyor, yani fabrikanın
        // sonradan eklediği yapılandırma kaynağı o okumaya yetişmiyordu. Ortam
        // değişkenleri ise varsayılan kaynaklar arasında ve en baştan görülüyor.
        //
        // Bu ayar atlandığında testler tek tek geçip tam pakette kalıyordu: test
        // sunucusunda istemci IP'si boş olduğu için tüm testler aynı hız sınırı
        // kovasını paylaşıyor ve 15. girişten sonra kilit devreye giriyordu.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", DatabaseFixture.ConnectionString);
        Environment.SetEnvironmentVariable("Guvenlik__EpostaDogrulamaZorunlu", "false");
        Environment.SetEnvironmentVariable("Guvenlik__HizSiniriAktif", "false");
        Environment.SetEnvironmentVariable("Eposta__SmtpSunucu", "");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            // Arka plan görevleri test sırasında sepet kilitlerini düşürüp
            // kuyruk haklarını değiştirerek sonuçları belirsiz hâle getirir.
            foreach (var arkaPlan in services.Where(s => s.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(arkaPlan);
            }
        });
    }

    /// <summary>
    /// Doğrulanmış bir hesap oluşturur ve o hesapla giriş yapmış bir istemci döner.
    /// Çerezler istemcide taşınır, yani sonraki istekler oturumu korur.
    /// </summary>
    public async Task<HttpClient> GirisYapmisIstemciAsync(string eposta, string? rol = null)
    {
        using (var kapsam = Services.CreateScope())
        {
            var kullaniciYoneticisi = kapsam.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            if (await kullaniciYoneticisi.FindByEmailAsync(eposta) == null)
            {
                var kullanici = new ApplicationUser
                {
                    UserName = eposta,
                    Email = eposta,
                    Ad = "Test Kullanıcı",
                    EmailConfirmed = true
                };

                var sonuc = await kullaniciYoneticisi.CreateAsync(kullanici, TestSifresi);
                if (!sonuc.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Test kullanıcısı oluşturulamadı: " + string.Join(", ", sonuc.Errors.Select(e => e.Description)));
                }

                if (rol != null)
                {
                    var rolYoneticisi = kapsam.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    if (!await rolYoneticisi.RoleExistsAsync(rol))
                    {
                        await rolYoneticisi.CreateAsync(new IdentityRole(rol));
                    }
                    await kullaniciYoneticisi.AddToRoleAsync(kullanici, rol);
                }
            }
        }

        var istemci = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var girisSayfasi = await istemci.GetStringAsync("/Account/GirisYap");

        var cevap = await istemci.PostAsync("/Account/GirisYap", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Email"] = eposta,
                ["Sifre"] = TestSifresi,
                ["BeniHatirla"] = "false",
                ["__RequestVerificationToken"] = AntiforgeryJetonu(girisSayfasi)
            }));

        // 302 tek başına yeterli değil: başarısız giriş de yönlendirme dönebilir
        // (ör. doğrulanmamış hesap "e-postanızı doğrulayın" sayfasına gider).
        // Hedefin /Account altında olmaması, oturumun gerçekten açıldığını gösterir.
        var hedef = cevap.Headers.Location?.OriginalString ?? "";

        if (cevap.StatusCode != System.Net.HttpStatusCode.Found ||
            hedef.StartsWith("/Account", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Giriş yapılamadı: {cevap.StatusCode} → '{hedef}' (hesap: {eposta})");
        }

        return istemci;
    }

    /// <summary>Formdaki gizli antiforgery alanını okur; olmadan POST'lar reddedilir.</summary>
    public static string AntiforgeryJetonu(string html)
    {
        var eslesme = System.Text.RegularExpressions.Regex.Match(
            html, """name="__RequestVerificationToken"[^>]*value="([^"]+)""");

        if (!eslesme.Success) throw new InvalidOperationException("Antiforgery jetonu bulunamadı.");
        return eslesme.Groups[1].Value;
    }
}
