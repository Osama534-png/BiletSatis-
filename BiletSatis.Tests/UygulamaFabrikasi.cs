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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Testler ayrı veritabanına yazsın; geliştirme verisi bozulmasın.
                ["ConnectionStrings:DefaultConnection"] = DatabaseFixture.ConnectionString,

                // Test hesaplarının gelen kutusu yok; doğrulama adımı akışı kilitlerdi.
                ["Guvenlik:EpostaDogrulamaZorunlu"] = "false",

                // Tüm istekler tek IP'den geliyor, sınır testleri boğardı.
                ["Guvenlik:HizSiniriAktif"] = "false",

                // SMTP tanımsız kalsın: e-postalar diske yazılır, gerçek gönderim olmaz.
                ["Eposta:SmtpSunucu"] = ""
            });
        });

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

        if (cevap.StatusCode != System.Net.HttpStatusCode.Found)
        {
            throw new InvalidOperationException($"Giriş yapılamadı: {cevap.StatusCode}");
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
