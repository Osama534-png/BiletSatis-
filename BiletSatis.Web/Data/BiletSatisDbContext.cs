using BiletSatis.Web.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Data;

/// <summary>
/// <see cref="IDataProtectionKeyContext"/>: ASP.NET'in çerezleri ve antiforgery
/// jetonlarını imzaladığı anahtarlar da bu veritabanında tutulur. Varsayılan
/// davranışta anahtarlar dosya sistemine yazılır; container'da bu, her yeniden
/// oluşturmada tüm kullanıcıların oturumdan düşmesi ve iki kopyanın birbirinin
/// çerezini doğrulayamaması demektir.
/// </summary>
public class BiletSatisDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public BiletSatisDbContext(DbContextOptions<BiletSatisDbContext> options) : base(options) { }

    public DbSet<Etkinlik> Etkinlikler => Set<Etkinlik>();
    public DbSet<Bilet> Biletler => Set<Bilet>();
    public DbSet<RezervasyonKuyrugu> RezervasyonKuyrugu => Set<RezervasyonKuyrugu>();
    public DbSet<Degerlendirme> Degerlendirmeler => Set<Degerlendirme>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Etkinlik>(e =>
        {
            e.Property(x => x.Mekan).HasMaxLength(200).HasDefaultValue("");
            e.Property(x => x.AfisUrl).HasMaxLength(400).HasDefaultValue("");
            e.Property(x => x.Aciklama).HasMaxLength(2000).HasDefaultValue("");

            e.Property(x => x.Kategori)
                .HasConversion<string>()
                .HasMaxLength(40)
                .HasDefaultValue(EtkinlikKategorisi.Konser);

            e.HasIndex(x => x.Kategori);

            // rowversion: SQL Server her güncellemede kendisi artırır. EF, güncelleme
            // sorgusuna "WHERE ... AND SatirSurumu = okuduğum değer" koşulunu ekler;
            // araya başka bir kayıt girdiyse hiçbir satır etkilenmez ve
            // DbUpdateConcurrencyException fırlar.
            e.Property(x => x.SatirSurumu).IsRowVersion();
        });

        modelBuilder.Entity<Bilet>(b =>
        {
            b.Property(x => x.Durum)
                .HasConversion(
                    v => v == BiletDurumu.Satista ? BiletDurumMetni.Satista
                       : v == BiletDurumu.Sepette ? BiletDurumMetni.Sepette
                       : BiletDurumMetni.Satildi,
                    v => v == BiletDurumMetni.Satista ? BiletDurumu.Satista
                       : v == BiletDurumMetni.Sepette ? BiletDurumu.Sepette
                       : BiletDurumu.Satildi)
                .HasMaxLength(20);

            b.Property(x => x.Fiyat).HasColumnType("decimal(10,2)");
            b.Property(x => x.OdemeReferansi).HasMaxLength(200);
            b.HasIndex(x => new { x.Durum, x.KilitBitisZamani });

            // Bildirim görevi "satılmış ama bildirilmemiş" biletleri tarar.
            b.HasIndex(x => new { x.Durum, x.BildirimGonderildi });

            // Admin panelindeki giriş sayacı etkinlik bazında bu alanı sayar.
            b.HasIndex(x => new { x.EtkinlikId, x.GirisYapildi });

            // Aynı etkinlikte aynı koltuk numarası iki kez bulunamaz. Bilet ekleme
            // "kaç tane var" sayıp numara üretiyor; iki eşzamanlı ekleme aynı numarayı
            // üretebilir. Bu dizin, çakışmayı veritabanı seviyesinde imkânsız kılar.
            b.HasIndex(x => new { x.EtkinlikId, x.KoltukNo }).IsUnique();

            b.Property(x => x.RezerveEdenKullaniciId).HasMaxLength(450);

            // "Biletlerim", "Sepetim" ve profil özeti hep bu alana göre filtreliyor.
            // Dizin olmadan bu sorgular tüm bilet tablosunu tarıyordu.
            b.HasIndex(x => new { x.RezerveEdenKullaniciId, x.Durum });

            b.HasOne(x => x.Etkinlik)
                .WithMany(x => x.Biletler)
                .HasForeignKey(x => x.EtkinlikId);
        });

        modelBuilder.Entity<Degerlendirme>(d =>
        {
            d.Property(x => x.KullaniciId).HasMaxLength(450);
            d.Property(x => x.Yorum).HasMaxLength(Degerlendirme.EnUzunYorum).HasDefaultValue("");

            // Bir kullanıcı bir etkinliği yalnızca bir kez değerlendirebilir. Kural
            // servis içinde de kontrol ediliyor; buradaki benzersiz dizin, iki isteğin
            // aynı anda gelmesi hâlinde ikinci satırın veritabanı seviyesinde
            // reddedilmesini sağlar.
            d.HasIndex(x => new { x.EtkinlikId, x.KullaniciId }).IsUnique();

            d.HasOne(x => x.Etkinlik)
                .WithMany()
                .HasForeignKey(x => x.EtkinlikId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RezervasyonKuyrugu>(r =>
        {
            r.HasKey(x => x.SiraNo);
            r.Property(x => x.SiraNo).ValueGeneratedOnAdd();

            r.Property(x => x.Durum)
                .HasConversion(
                    v => v == KuyrukDurumu.Beklemede ? KuyrukDurumMetni.Beklemede
                       : v == KuyrukDurumu.HakTanindi ? KuyrukDurumMetni.HakTanindi
                       : v == KuyrukDurumu.Tamamlandi ? KuyrukDurumMetni.Tamamlandi
                       : KuyrukDurumMetni.SuresiDoldu,
                    v => v == KuyrukDurumMetni.Beklemede ? KuyrukDurumu.Beklemede
                       : v == KuyrukDurumMetni.HakTanindi ? KuyrukDurumu.HakTanindi
                       : v == KuyrukDurumMetni.Tamamlandi ? KuyrukDurumu.Tamamlandi
                       : KuyrukDurumu.SuresiDoldu)
                .HasMaxLength(20);

            r.Property(x => x.KullaniciId).HasMaxLength(450);

            r.HasIndex(x => new { x.EtkinlikId, x.Durum, x.SiraNo });

            // "Bu kullanıcı bu etkinlikte zaten sırada mı" sorgusu hem kuyruk durumu
            // sayfasında hem de sıraya girmedeki NOT EXISTS kontrolünde kullanılıyor.
            // Dizin olmadan o kontrolün aldığı aralık kilidi gereksiz genişti.
            r.HasIndex(x => new { x.EtkinlikId, x.KullaniciId });

            // Kuyruk kayıtlarının etkinliğe foreign key'i yoktu; silme sırasında elle
            // temizlenmeleri gerekiyordu ve bu unutulmaya açıktı. Artık ilişki şemada.
            r.HasOne<Etkinlik>()
                .WithMany()
                .HasForeignKey(x => x.EtkinlikId)
                .OnDelete(DeleteBehavior.Cascade);

            // Bildirim görevi "hakkı tanınmış ama bildirilmemiş" kayıtları tarar.
            r.HasIndex(x => new { x.Durum, x.BildirimGonderildi });
        });
    }
}
