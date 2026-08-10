-- Yük testi artıklarını temizler.
--
-- k6 testleri her sanal kullanıcı için tek kullanımlık bir hesap açar
-- (yuktest-...@test.local). Bunlar birikir: birkaç yüz koşudan sonra AspNetUsers
-- tablosunda binlerce kayıt olur. Kayıtlar zararsızdır ama geliştirme
-- veritabanını okunmaz hâle getirir.
--
-- Çalıştırma:
--   sqlcmd -S localhost -E -d BiletSatisDb -i loadtests/temizlik.sql
--
-- ⚠️ Gerçek kullanıcıları silmez: yalnızca "yuktest-" ile başlayan adresleri
-- hedefler. Önce SELECT ile ne silineceğini gösterir.

SET NOCOUNT ON;

-- sqlcmd bu iki ayarı varsayılan olarak KAPALI çalıştırır; SSMS ise açık.
--
-- QUOTED_IDENTIFIER kapalıyken Identity tablolarındaki filtrelenmiş dizinler
-- yüzünden DELETE reddediliyor ("SET options have incorrect settings").
--
-- XACT_ABORT kapalıyken hata yalnızca o deyimi iptal eder; sonraki deyimler ve
-- COMMIT çalışmaya devam eder, yani yarım temizlik kalıcı olabilirdi. Açıkken
-- herhangi bir hata işlemin tamamını geri alır.
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @TestOneki NVARCHAR(20) = N'yuktest-%';

PRINT '--- Silinecekler ---';

SELECT
    (SELECT COUNT(*) FROM AspNetUsers WHERE Email LIKE @TestOneki)          AS Kullanici,
    (SELECT COUNT(*) FROM RezervasyonKuyrugu k
       JOIN AspNetUsers u ON u.Id = k.KullaniciId
      WHERE u.Email LIKE @TestOneki)                                        AS KuyrukKaydi,
    (SELECT COUNT(*) FROM Biletler b
       JOIN AspNetUsers u ON u.Id = b.RezerveEdenKullaniciId
      WHERE u.Email LIKE @TestOneki AND b.Durum = N'Sepette')               AS SerbestBirakilacakBilet;

BEGIN TRANSACTION;

-- 1. Test kullanıcılarının tuttuğu sepet kilitlerini bırak. Bu biletler gerçek
--    satış değil, yarıda kalmış rezervasyon; satışa geri dönmeleri gerekir.
UPDATE b
SET Durum = N'Satışta', RezerveEdenKullaniciId = NULL, KilitBitisZamani = NULL
FROM Biletler b
JOIN AspNetUsers u ON u.Id = b.RezerveEdenKullaniciId
WHERE u.Email LIKE @TestOneki AND b.Durum = N'Sepette';

-- 2. Satılmış biletleri OLDUĞU GİBİ BIRAK ve sahibini silme. Satış kaydı silinirse
--    gelir raporları ile bilet sayıları tutmaz. Bu kullanıcılar atlanır.
DELETE k
FROM RezervasyonKuyrugu k
JOIN AspNetUsers u ON u.Id = k.KullaniciId
WHERE u.Email LIKE @TestOneki
  AND NOT EXISTS (SELECT 1 FROM Biletler b
                  WHERE b.RezerveEdenKullaniciId = u.Id AND b.Durum = N'Satıldı');

DELETE f
FROM Favoriler f
JOIN AspNetUsers u ON u.Id = f.KullaniciId
WHERE u.Email LIKE @TestOneki;

DELETE d
FROM Degerlendirmeler d
JOIN AspNetUsers u ON u.Id = d.KullaniciId
WHERE u.Email LIKE @TestOneki;

DELETE r
FROM AspNetUserRoles r
JOIN AspNetUsers u ON u.Id = r.UserId
WHERE u.Email LIKE @TestOneki;

DELETE c
FROM AspNetUserClaims c
JOIN AspNetUsers u ON u.Id = c.UserId
WHERE u.Email LIKE @TestOneki;

DELETE l
FROM AspNetUserLogins l
JOIN AspNetUsers u ON u.Id = l.UserId
WHERE u.Email LIKE @TestOneki;

DELETE t
FROM AspNetUserTokens t
JOIN AspNetUsers u ON u.Id = t.UserId
WHERE u.Email LIKE @TestOneki;

-- Satılmış bileti olan test kullanıcıları korunur: bileti duran bir hesabı silmek
-- öksüz satış kaydı bırakırdı.
DELETE FROM AspNetUsers
WHERE Email LIKE @TestOneki
  AND NOT EXISTS (SELECT 1 FROM Biletler b
                  WHERE b.RezerveEdenKullaniciId = AspNetUsers.Id AND b.Durum = N'Satıldı');

COMMIT;

PRINT '--- Kalan ---';

SELECT
    (SELECT COUNT(*) FROM AspNetUsers WHERE Email LIKE @TestOneki)       AS KalanTestKullanicisi,
    (SELECT COUNT(*) FROM AspNetUsers WHERE Email NOT LIKE @TestOneki)   AS GercekKullanici;
