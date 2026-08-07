using BiletSatis.Web.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Data;

public class BiletSatisDbContext : IdentityDbContext<ApplicationUser>
{
    public BiletSatisDbContext(DbContextOptions<BiletSatisDbContext> options) : base(options) { }

    public DbSet<Etkinlik> Etkinlikler => Set<Etkinlik>();
    public DbSet<Bilet> Biletler => Set<Bilet>();
    public DbSet<RezervasyonKuyrugu> RezervasyonKuyrugu => Set<RezervasyonKuyrugu>();

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
            b.HasIndex(x => new { x.Durum, x.KilitBitisZamani });

            b.HasOne(x => x.Etkinlik)
                .WithMany(x => x.Biletler)
                .HasForeignKey(x => x.EtkinlikId);
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

            r.HasIndex(x => new { x.EtkinlikId, x.Durum, x.SiraNo });
        });
    }
}
