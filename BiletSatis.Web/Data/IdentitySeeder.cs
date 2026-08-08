using Microsoft.AspNetCore.Identity;

namespace BiletSatis.Web.Data;

public static class IdentitySeeder
{
    private const string AdminRole = "Admin";
    private const string VarsayilanAdminEposta = "admin@biletsatis.local";

    /// <summary>
    /// Yalnızca geliştirmede kullanılan sabit şifre. Üretimde bu şifreyle hesap
    /// oluşturulmaz; şifre yapılandırmadan (user-secrets / ortam değişkeni) gelir.
    /// </summary>
    private const string GelistirmeAdminSifresi = "Admin123!";

    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment ortam, IConfiguration yapilandirma)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(IdentitySeeder));

        if (!await roleManager.RoleExistsAsync(AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(AdminRole));
        }

        var eposta = yapilandirma["Yonetici:Eposta"] ?? VarsayilanAdminEposta;
        var sifre = yapilandirma["Yonetici:Sifre"];

        if (string.IsNullOrWhiteSpace(sifre))
        {
            if (!ortam.IsDevelopment())
            {
                // Üretimde koda gömülü şifreyle admin oluşturmak, adresi bilen herkese
                // yönetici erişimi vermek demektir. Şifre verilmediyse hesap açılmaz.
                logger.LogWarning(
                    "Yonetici:Sifre tanımlı değil — yönetici hesabı oluşturulmadı. " +
                    "Hesabı açmak için Yonetici:Eposta ve Yonetici:Sifre değerlerini tanımlayın.");
                return;
            }

            sifre = GelistirmeAdminSifresi;
        }

        var adminUser = await userManager.FindByEmailAsync(eposta);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = eposta,
                Email = eposta,
                Ad = "Yönetici",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(adminUser, sifre);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Admin kullanıcısı oluşturulamadı: " + string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            logger.LogInformation("Yönetici hesabı oluşturuldu: {Eposta}", eposta);
        }

        if (!await userManager.IsInRoleAsync(adminUser, AdminRole))
        {
            await userManager.AddToRoleAsync(adminUser, AdminRole);
        }
    }
}
