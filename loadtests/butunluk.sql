-- Veri bütünlüğü taraması: şemanın izin verdiği ama iş kurallarının izin vermediği
-- durumları arar. Foreign key ve unique index bir şeyi imkânsız kılar; bunlar ise
-- "olmaması gereken ama teknik olarak yazılabilen" durumlar.
--
-- Her satırın 0 dönmesi beklenir. Sıfırdan büyük bir sonuç ya bir kod hatası ya da
-- geçmişte kalmış bir veri artığı demektir — ikisi de incelenmeli.
--
-- Çalıştırma:
--   sqlcmd -S localhost -E -d BiletSatisDb -i loadtests/butunluk.sql -f 65001 -W
--
-- Not: -f 65001 şart. Durum değerleri Türkçe karakter içeriyor ('Satışta', 'Satıldı');
-- yanlış kod sayfasıyla okunan dosyada bu karşılaştırmalar sessizce tutmaz ve tarama
-- "her şey bozuk" ya da "her şey temiz" gibi yanıltıcı sonuç verir.
--
-- Son tarama sonucu README'de "Veritabanı bütünlüğü" başlığında.

SET NOCOUNT ON;
USE BiletSatisDb;

DECLARE @sonuc TABLE (Sira INT, Kontrol NVARCHAR(120), Adet INT);

INSERT INTO @sonuc
SELECT 1, N'Sepette ama sahibi yok', COUNT(*) FROM Biletler WHERE Durum = N'Sepette' AND RezerveEdenKullaniciId IS NULL
UNION ALL SELECT 2, N'Sepette ama kilit zamani yok', COUNT(*) FROM Biletler WHERE Durum = N'Sepette' AND KilitBitisZamani IS NULL
UNION ALL SELECT 3, N'Satildi ama sahibi yok', COUNT(*) FROM Biletler WHERE Durum = N'Satıldı' AND RezerveEdenKullaniciId IS NULL
UNION ALL SELECT 4, N'Satista ama sahip/kilit kalmis', COUNT(*) FROM Biletler WHERE Durum = N'Satışta' AND (RezerveEdenKullaniciId IS NOT NULL OR KilitBitisZamani IS NOT NULL)
UNION ALL SELECT 5, N'Silinmis kullaniciya ait bilet', COUNT(*) FROM Biletler b WHERE b.RezerveEdenKullaniciId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = b.RezerveEdenKullaniciId)
UNION ALL SELECT 6, N'Silinmis kullaniciya ait degerlendirme', COUNT(*) FROM Degerlendirmeler d WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = d.KullaniciId)
UNION ALL SELECT 7, N'Silinmis kullaniciya ait favori', COUNT(*) FROM Favoriler f WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = f.KullaniciId)
UNION ALL SELECT 8, N'Silinmis kullaniciya ait kuyruk kaydi', COUNT(*) FROM RezervasyonKuyrugu k WHERE NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = k.KullaniciId)
UNION ALL SELECT 9, N'Kapidan gecmeden birakilmis yorum', COUNT(*) FROM Degerlendirmeler d WHERE NOT EXISTS (SELECT 1 FROM Biletler b WHERE b.EtkinlikId = d.EtkinlikId AND b.RezerveEdenKullaniciId = d.KullaniciId AND b.Durum = N'Satıldı' AND b.GirisYapildi = 1)
UNION ALL SELECT 10, N'Gecersiz puan (1-5 disi)', COUNT(*) FROM Degerlendirmeler WHERE Puan < 1 OR Puan > 5
UNION ALL SELECT 11, N'Negatif veya sifir fiyat', COUNT(*) FROM Biletler WHERE Fiyat <= 0
UNION ALL SELECT 12, N'Gecersiz bilet durumu', COUNT(*) FROM Biletler WHERE Durum NOT IN (N'Satışta', N'Sepette', N'Satıldı')
UNION ALL SELECT 13, N'Gecersiz kuyruk durumu', COUNT(*) FROM RezervasyonKuyrugu WHERE Durum NOT IN (N'Beklemede', N'HakTanindi', N'Tamamlandi', N'SuresiDoldu')
UNION ALL SELECT 14, N'Tekrar eden koltuk numarasi', (SELECT COUNT(*) FROM (SELECT EtkinlikId, KoltukNo FROM Biletler GROUP BY EtkinlikId, KoltukNo HAVING COUNT(*) > 1) t)
UNION ALL SELECT 15, N'Ayni kullanici cift aktif kuyruk kaydi', (SELECT COUNT(*) FROM (SELECT EtkinlikId, KullaniciId FROM RezervasyonKuyrugu WHERE Durum <> N'SuresiDoldu' GROUP BY EtkinlikId, KullaniciId HAVING COUNT(*) > 1) t)
UNION ALL SELECT 16, N'Giris yapilmis ama satilmamis bilet', COUNT(*) FROM Biletler WHERE GirisYapildi = 1 AND Durum <> N'Satıldı'
UNION ALL SELECT 17, N'Giris yapilmis ama giris zamani yok', COUNT(*) FROM Biletler WHERE GirisYapildi = 1 AND GirisZamani IS NULL
UNION ALL SELECT 18, N'Suresi dolmus ama hala sepette', COUNT(*) FROM Biletler WHERE Durum = N'Sepette' AND KilitBitisZamani < GETUTCDATE()
UNION ALL SELECT 19, N'Gecersiz kod surumu (<1)', COUNT(*) FROM Biletler WHERE KodSurumu < 1
UNION ALL SELECT 20, N'Sehir sutunu Mekan ile tutarsiz', COUNT(*) FROM Etkinlikler WHERE Sehir <> CASE WHEN CHARINDEX(',', REVERSE(Mekan)) = 0 THEN N'' ELSE LTRIM(RTRIM(RIGHT(Mekan, CHARINDEX(',', REVERSE(Mekan)) - 1))) END
UNION ALL SELECT 21, N'HakTanindi ama bitis zamani yok', COUNT(*) FROM RezervasyonKuyrugu WHERE Durum = N'HakTanindi' AND HakBitisZamani IS NULL
UNION ALL SELECT 22, N'Satildi ama odeme referansi yok', COUNT(*) FROM Biletler WHERE Durum = N'Satıldı' AND OdemeReferansi IS NULL
UNION ALL SELECT 23, N'Devredilmis ama bildirimi bekleyen', COUNT(*) FROM Biletler WHERE KodSurumu > 1 AND BildirimGonderildi = 0;

SELECT Sira, Kontrol, Adet, CASE WHEN Adet = 0 THEN N'TEMIZ' ELSE N'>>> INCELE' END AS Durum
FROM @sonuc ORDER BY Sira;

SELECT N'TOPLAM SORUNLU KONTROL' AS Ozet, COUNT(*) AS Adet FROM @sonuc WHERE Adet > 0;
