# Ekran görüntülerini yenileme

Arayüz değişince ana README'deki görseller eskir. Sırasıyla şu adresler çekilir
(`salon-haritasi`, `sepet`, `ana-sayfa`, `etkinlik-listesi`, `kapi-kontrolu`):

```
/Biletler/Index?etkinlikId=9      → koltuk seç, alttaki toplam çubuğu görünsün
/Biletler/Sepetim                 → süre sayacı ve "Stripe ile Öde" görünsün
/?kategori=Konser&siralama=fiyat-artan
/Giris/Dogrula?kod=...            → admin girişi gerekir, ~420 px genişlik
```

Kapı kontrolü için **geçerli bir imzalı kod** gerekir; kod bilet numarasının HMAC
imzasını taşır, elle uydurulamaz. İmza anahtarını okumadan üretmenin yolu,
uygulamanın kendi bildirim hattını kullanmak:

```sql
UPDATE Biletler SET BildirimGonderildi = 0, BildirimKilitZamani = NULL WHERE Id = 1;
```

Uygulamayı `yuktest` profiliyle başlat (SMTP kapalı olsun ki gerçek e-posta
gitmesin). `BildirimWorker` 20 saniye içinde e-postayı `logs/eposta/` altına
yazar; doğrulama adresi onun içindedir:

```bash
grep -ohE '[0-9]+\.[0-9]+\.[a-f0-9]{16}' BiletSatis.Web/logs/eposta/*.html
```

Geniş sayfalarda pencere ~1700 px, kapı kontrolünde ~420 px. Görselde gerçek
e-posta adresi kalmasın.
