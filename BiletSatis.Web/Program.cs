using BiletSatis.Web.BackgroundServices;
using BiletSatis.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using BiletSatis.Web.Services;
using BiletSatis.Web.Services.Degerlendirmeler;
using BiletSatis.Web.Services.Eposta;
using BiletSatis.Web.Services.Giris;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

// Giriş/kayıt uçlarında kullanılan hız sınırı politikasının adı.
const string GirisHizSiniri = "giris";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// Kestrel varsayılan olarak her cevaba "Server: Kestrel" başlığı ekler. Saldırganın
// hangi sunucuyu hedeflediğini bilmesi tek başına açık değil, ama bilinen bir açık
// çıktığında taranabilir hedef listesine girmenin de gereği yok.
builder.WebHost.ConfigureKestrel(secenekler => secenekler.AddServerHeader = false);

builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day));

// Arayüz tamamen Türkçe, ama kültür hiçbir yerde ayarlanmıyordu: biçimlendirme
// işletim sisteminin kültürüne kalıyordu. Türkçe bir Windows'ta doğru görünen
// fiyat ve tarihler, projenin desteklediği Docker (Linux) kurulumunda bozuluyordu —
// "1.500 ₺" yerine "1,500", "12 Eyl 2026" yerine "12 Sep 2026".
// Kültürü açıkça sabitlemek, uygulamanın nerede çalıştığından bağımsız olarak
// aynı çıktıyı vermesini sağlar.
var uygulamaKulturu = new CultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = uygulamaKulturu;
CultureInfo.DefaultThreadCurrentUICulture = uygulamaKulturu;

builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));

    // Ondalıklı sayılar formdan noktayla gelir (HTML number alanı standardı),
    // ama Türkçe kültürde nokta binlik ayracıdır: "250.50" değeri 25050 olarak
    // okunuyordu. Bu bağlayıcı yalnızca okuma tarafını kültürden bağımsız yapar;
    // gösterim Türkçe kalır.
    options.ModelBinderProviders.Insert(0, new OndalikModelBaglayiciSaglayicisi());
});
builder.Services.AddDbContext<BiletSatisDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            // Geçici hatalarda (ağ kesintisi, sunucu yeniden başlatma, bulut
            // sağlayıcısının bağlantıyı düşürmesi) istek doğrudan başarısız olmasın.
            // Docker Compose kurulumunda uygulama container'ı, veritabanı container'ı
            // hazır olmadan da ayağa kalkabiliyor; ilk sorgular bu yüzden düşüyordu.
            //
            // DİKKAT: Bu ayar açıkken kendi açtığın işlemler (BeginTransaction)
            // yürütme stratejisinin içine alınmalı. Aksi halde EF çalışma anında
            // "does not support user-initiated transactions" hatası verir — derleme
            // sırasında değil, yalnızca o kod yolu çalıştığında. Projede iki yer
            // böyle: çoklu koltuk ve genel giriş rezervasyonu; ikisi de
            // CreateExecutionStrategy().ExecuteAsync(...) ile sarmalanmış durumda.
            sql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
        }));

// Çerez ve antiforgery jetonlarını imzalayan anahtarlar veritabanında tutulur.
// Varsayılanda dosya sistemine yazılırlar; container'da bu, her yeniden oluşturmada
// herkesin oturumdan düşmesi demek. Ayrıca uygulamanın iki kopyası ayrı anahtar
// üretirse biri diğerinin çerezini doğrulayamaz — kullanıcı kopyalar arasında
// gezindikçe sürekli çıkış yapmış olur.
// Yük testleri (loadtests/k6) tek IP'den yüzlerce kayıt/giriş isteği gönderir ve
// gelen kutusu olmadığı için e-posta doğrulamasını tamamlayamaz. Bu iki koruma
// yalnızca o senaryo için kapatılabilir; varsayılanları açıktır.
var epostaDogrulamaZorunlu = builder.Configuration.GetValue("Guvenlik:EpostaDogrulamaZorunlu", true);
var hizSiniriAktif = builder.Configuration.GetValue("Guvenlik:HizSiniriAktif", true);

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<BiletSatisDbContext>()
    .SetApplicationName("BiletSatis");

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Uzunluk, karmaşıklık kurallarından daha etkili bir koruma: "P@ss1!" altı
    // karakterle bütün kuralları geçer ama kaba kuvvete dayanmaz, "kirmizi bisiklet"
    // hiçbirini geçmez ama kat kat güçlüdür. NIST SP 800-63B de bu yönde: asgari
    // uzunluğu yükselt, karmaşıklık dayatma (kullanıcıyı tahmin edilebilir
    // kalıplara — "Sifre1!" — itiyor).
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireDigit = false;
    options.User.RequireUniqueEmail = true;
    // E-posta doğrulanmadan giriş yapılamaz. Özellik eklenmeden önce açılmış
    // hesaplar migration ile "doğrulanmış" işaretlendi; aksi halde mevcut
    // kullanıcılar bir anda kapıda kalırdı.
    //
    // Yük testleri yüzlerce tek kullanımlık hesap açıp hemen giriş yapar; gelen
    // kutusu olmadığı için doğrulama adımını tamamlayamazlar. Bu yüzden kural
    // kapatılabilir — varsayılan açık, kapatmak bilinçli bir tercih olmalı.
    options.SignIn.RequireConfirmedAccount = epostaDogrulamaZorunlu;

    // Kaba kuvvet koruması: 5 hatalı denemeden sonra hesap 5 dakika kilitlenir.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<BiletSatisDbContext>()
    .AddErrorDescriber<TurkceIdentityHatalari>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    // Giriş yapmamış biri korumalı bir sayfaya girmeye çalışırsa buraya yönlendirilir.
    options.LoginPath = "/Account/GirisYap";

    // Girişi var ama yetkisi yoksa (ör. normal kullanıcı yönetim paneline girerse).
    options.AccessDeniedPath = "/Account/ErisimEngellendi";

    // 14 gün sonra çerez geçersiz olur; SlidingExpiration sayesinde aktif kullanan
    // kullanıcının süresi her istekte tazelenir, yalnızca uzun süre uğramayan düşer.
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    // Çerez yalnızca sunucuya gitsin, JavaScript okuyamasın (XSS'te oturum çalınmasın).
    options.Cookie.HttpOnly = true;

    // Lax: normal gezinmede gönderilir, siteler arası POST'ta gönderilmez. Stripe'tan
    // dönüş üst düzey GET yönlendirmesi olduğu için Lax bu akışı bozmaz.
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Geliştirmede http üzerinden çalışıldığı için Always yapılırsa oturum açılamaz.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// Giriş ve kayıt uçlarına hız sınırı. Hesap kilidi tek bir hesabı korur; bu sınır
// ise tek kaynaktan çok sayıda hesaba yapılan denemeleri (kullanıcı adı taraması,
// kayıt spam'i) yavaşlatır.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(GirisHizSiniri, httpContext =>
        !hizSiniriAktif
        ? RateLimitPartition.GetNoLimiter("kapali")
        : RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                // Hesap kilidi 5 hatalı denemede devreye girer; sınır bunun üstünde
                // olmalı ki kullanıcı önce anlaşılır kilit mesajını görsün.
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Hesap uçlarındaki sınır yalnızca giriş/kayıt'ı koruyordu; giriş yapmış bir
    // kullanıcı sepete ekleme, favori, kuyruk ve değerlendirme uçlarını sınırsız
    // dövebiliyordu. Her istek veritabanına yazma demek.
    //
    // Sınır yalnızca POST'lara uygulanıyor: durum değiştiren istekler onlar.
    // Sayfa gezinmesi ve statik dosyalar etkilenmiyor — aksi halde tek bir sayfa
    // açılışı (HTML + CSS + JS + görseller) kotanın önemli bir kısmını yerdi.
    //
    // Bölümleme kullanıcı kimliğine göre: aynı ağdaki (okul, iş yeri, mobil NAT)
    // farklı kullanıcılar birbirinin kotasını tüketmesin. Girişi olmayanlar IP'ye
    // göre bölümleniyor.
    //
    // 60/dakika bir insan için fazlasıyla geniş, otomatik bir betik için dar.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (!hizSiniriAktif || !HttpMethods.IsPost(httpContext.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("sinirsiz");
        }

        var kimlik = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(kimlik, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1)
        });
    });

    // Varsayılan davranış çıplak bir 429 hata sayfasıdır. Bunun yerine kullanıcıyı
    // ne olduğunu anlatan normal bir sayfaya yönlendiriyoruz.
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var bekleme))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)bekleme.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        context.HttpContext.Response.Redirect("/Account/CokFazlaDeneme");
        return ValueTask.CompletedTask;
    };
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IBiletRezervasyonServisi, BiletRezervasyonServisi>();
builder.Services.AddScoped<IKuyrukServisi, KuyrukServisi>();
builder.Services.AddScoped<BiletSatis.Web.Services.Etkinlikler.IEtkinlikSorguServisi, BiletSatis.Web.Services.Etkinlikler.EtkinlikSorguServisi>();
builder.Services.AddScoped<IDegerlendirmeServisi, DegerlendirmeServisi>();
builder.Services.AddScoped<BiletSatis.Web.Services.Favoriler.IFavoriServisi, BiletSatis.Web.Services.Favoriler.FavoriServisi>();
builder.Services.AddHostedService<CartExpiryWorker>();
builder.Services.AddHostedService<WaitlistWorker>();

builder.Services.Configure<EpostaAyarlari>(builder.Configuration.GetSection(EpostaAyarlari.BolumAdi));

// SMTP sunucusu tanımlıysa gerçek gönderim, değilse e-postaları diske yazan
// geliştirme uygulaması kullanılır. Böylece proje SMTP hesabı olmadan da çalışır.
var epostaAyarlari = builder.Configuration.GetSection(EpostaAyarlari.BolumAdi).Get<EpostaAyarlari>() ?? new EpostaAyarlari();
if (epostaAyarlari.SmtpYapilandirilmisMi)
{
    builder.Services.AddScoped<IEpostaGonderici, SmtpEpostaGonderici>();
}
else
{
    builder.Services.AddScoped<IEpostaGonderici, DosyayaYazanEpostaGonderici>();
}

// Bilet QR kodları bu anahtarla imzalanır. Anahtar boşsa imza tahmin edilebilir
// hâle gelir ve sahte bilet üretilebilir; bu yüzden üretimde eksikse uygulama başlamaz.
builder.Services.Configure<GirisAyarlari>(builder.Configuration.GetSection(GirisAyarlari.BolumAdi));

var imzaAnahtari = builder.Configuration[$"{GirisAyarlari.BolumAdi}:ImzaAnahtari"];
if (string.IsNullOrWhiteSpace(imzaAnahtari))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Giris:ImzaAnahtari tanımlı değil. Bilet QR kodları imzalanamaz. " +
            "Anahtarı user-secrets ya da ortam değişkeniyle verin.");
    }

    // Geliştirmede uygulama çalışmaya devam etsin, ama anahtarın geçici olduğu belli olsun.
    builder.Services.PostConfigure<GirisAyarlari>(a => a.ImzaAnahtari = "gelistirme-icin-gecici-imza-anahtari");
}

builder.Services.AddSingleton<IBiletKoduServisi, BiletKoduServisi>();
builder.Services.AddSingleton<IQrKodUretici, QrKodUretici>();
builder.Services.AddScoped<IGirisServisi, GirisServisi>();
builder.Services.AddScoped<BiletSatis.Web.Services.Devir.IBiletDevirServisi, BiletSatis.Web.Services.Devir.BiletDevirServisi>();
builder.Services.AddScoped<IKuyrukBildirimServisi, KuyrukBildirimServisi>();
builder.Services.AddScoped<IBiletBildirimServisi, BiletBildirimServisi>();
builder.Services.AddScoped<IKimlikEpostaServisi, KimlikEpostaServisi>();
builder.Services.AddHostedService<BildirimWorker>();

// Kayıt, şifre sıfırlama ve adres değişikliği e-postaları isteğin içinde
// gönderiliyordu: kullanıcı cevabı SMTP sunucusu dönene kadar bekliyordu.
// Yük testinde POST /Account/KayitOl 200 eşzamanlı kullanıcıda 4-17 saniyeye
// çıkıyordu. Artık kuyruğa bırakılıp arka planda gönderiliyorlar — bildirimlerde
// zaten uygulanan ilkenin aynısı.
builder.Services.AddSingleton<IKimlikEpostaKuyrugu, KimlikEpostaKuyrugu>();
builder.Services.AddHostedService<KimlikEpostaWorker>();

// Stripe anahtarı yoksa uygulama ayağa kalkar ama ödeme adımında anlaşılmaz bir
// hata verir. QR imza anahtarındaki yaklaşımın aynısı: üretimde eksikse başlamasın.
var stripeAnahtari = builder.Configuration["Stripe:SecretKey"];
if (string.IsNullOrWhiteSpace(stripeAnahtari) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Stripe:SecretKey tanımlı değil. Ödeme alınamaz. " +
        "Anahtarı user-secrets ya da ortam değişkeniyle verin.");
}

Stripe.StripeConfiguration.ApiKey = stripeAnahtari;

// AllowedHosts "*" ise uygulama hangi alan adıyla çağrılırsa çağrılsın cevap verir.
// Üretimde bu, Host başlığı manipülasyonuna kapı aralar: saldırgan kendi alan adıyla
// istek atıp, e-postalardaki bağlantıların kendi sitesini göstermesini sağlayabilir.
// Uygulamayı durdurmuyoruz (yanlış yapılandırmayla da olsa ayakta kalması yeğdir),
// ama sessizce geçmiyoruz.
var izinliHostlar = builder.Configuration["AllowedHosts"];
if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(izinliHostlar) || izinliHostlar == "*"))
{
    Log.Warning(
        "AllowedHosts '*' olarak bırakılmış. Üretimde gerçek alan adlarıyla sınırlandırın " +
        "(ör. \"biletsatis.com;www.biletsatis.com\").");
}

// Yük testi için kapatılan korumaların üretimde açık kalmaması gerekir.
if (!builder.Environment.IsDevelopment() && (!epostaDogrulamaZorunlu || !hizSiniriAktif))
{
    Log.Warning(
        "Üretimde güvenlik korumaları kapalı: EpostaDogrulamaZorunlu={Dogrulama} HizSiniriAktif={HizSiniri}. " +
        "Bu ayarlar yalnızca yük testi içindir.",
        epostaDogrulamaZorunlu, hizSiniriAktif);
}

var app = builder.Build();

// Migration ve seed, uygulamanın birden çok kopyası aynı anda başlasa bile yalnızca
// bir kez çalışmalı. Kilit veritabanı seviyesinde; süreç içi lock burada işe yaramaz.
var baslangicLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Baslangic");

await using (await BaslangicKilidi.AlAsync(
    app.Configuration.GetConnectionString("DefaultConnection")!, baslangicLogger))
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BiletSatisDbContext>();
    db.Database.Migrate();
    DbSeeder.Seed(db);
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Environment, app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// CSP (script'ler için nonce) ve diğer tarayıcı savunma başlıkları.
app.UseGuvenlikBasliklari();

app.UseSerilogRequestLogging();
app.UseRouting();

app.UseAuthentication();

// SIRA ÖNEMLİ: hız sınırı, kimlik doğrulamadan SONRA gelmeli.
//
// Sınırlayıcı istekleri kullanıcı kimliğine göre bölümlüyor. Kimlik doğrulama
// henüz çalışmadıysa HttpContext.User boştur ve bölümleme sessizce IP'ye düşer —
// giriş yapmış bütün kullanıcılar tek kovayı paylaşır. Ölçüldü: iki farklı
// kullanıcı aynı IP'den istek attığında ikincisi, kendi kotasını hiç kullanmadan
// engelleniyordu. Kod "kullanıcıya göre bölümlüyorum" diyor ama yapmıyordu.
//
// UseRouting'den sonra olması da şart: [EnableRateLimiting] öznitelikleri
// endpoint meta verisinden okunuyor, o da yönlendirme aşamasında oluşuyor.
app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

/// <summary>
/// Uçtan uca testler uygulamayı bu sınıf üzerinden test sunucusunda ayağa kaldırır
/// (<c>WebApplicationFactory&lt;Program&gt;</c>). Üst düzey deyimlerle yazılan
/// Program sınıfı varsayılan olarak internal olduğu için açıkça public yapılıyor.
/// </summary>
public partial class Program { }
