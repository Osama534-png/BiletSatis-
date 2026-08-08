using BiletSatis.Web.BackgroundServices;
using BiletSatis.Web.Data;
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
using System.Threading.RateLimiting;

// Giriş/kayıt uçlarında kullanılan hız sınırı politikasının adı.
const string GirisHizSiniri = "giris";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day));

builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});
builder.Services.AddDbContext<BiletSatisDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireDigit = false;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false;

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
    options.LoginPath = "/Account/GirisYap"; // giri? yapmam?? biri korumal? sayfaya girerse buraya at
	options.AccessDeniedPath = "/Account/ErisimEngellendi";// yetkisi olmayan biri buraya at
	options.ExpireTimeSpan = TimeSpan.FromDays(14); // 14 g�n sonra �erez ge�ersiz olur, tekrar giri? gerekir
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
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(GirisHizSiniri, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IBiletRezervasyonServisi, BiletRezervasyonServisi>();
builder.Services.AddScoped<IKuyrukServisi, KuyrukServisi>();
builder.Services.AddScoped<IDegerlendirmeServisi, DegerlendirmeServisi>();
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
builder.Services.AddScoped<IKuyrukBildirimServisi, KuyrukBildirimServisi>();
builder.Services.AddScoped<IBiletBildirimServisi, BiletBildirimServisi>();
builder.Services.AddHostedService<BildirimWorker>();

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

var app = builder.Build();

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

// Tarayıcı tarafı savunma başlıkları. Tek satırlık maliyetle üç sınıf saldırıyı zorlaştırır.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;

    // Sunucunun bildirdiği içerik türünü tarayıcı "tahmin ederek" değiştirmesin.
    headers["X-Content-Type-Options"] = "nosniff";

    // Site başka bir sayfaya iframe ile gömülüp tıklama hırsızlığında kullanılmasın.
    headers["X-Frame-Options"] = "DENY";

    // Dış sitelere giderken tam adres (ve içindeki parametreler) sızmasın.
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

app.UseSerilogRequestLogging();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
