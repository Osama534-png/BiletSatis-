using BiletSatis.Web.BackgroundServices;
using BiletSatis.Web.Data;
using BiletSatis.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
})
    .AddEntityFrameworkStores<BiletSatisDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/GirisYap";
    options.AccessDeniedPath = "/Account/ErisimEngellendi";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IBiletRezervasyonServisi, BiletRezervasyonServisi>();
builder.Services.AddScoped<IKuyrukServisi, KuyrukServisi>();
builder.Services.AddHostedService<CartExpiryWorker>();
builder.Services.AddHostedService<WaitlistWorker>();

Stripe.StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BiletSatisDbContext>();
    db.Database.Migrate();
    DbSeeder.Seed(db);
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
