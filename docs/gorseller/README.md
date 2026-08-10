# Ekran görüntüleri

Ana README'nin "Arayüz" bölümü bu klasördeki dosyaları gösterir. Dosyalar
eklenene kadar o bölümde kırık görsel simgesi görünür.

## Gereken dört dosya

| Dosya | Hangi sayfa | Nasıl ulaşılır |
|---|---|---|
| `salon-haritasi.png` | Koltuk seçimi | Bir etkinliğe tıkla → "Bilet Al". Birkaç koltuk **seçili** olsun ki alttaki seçim çubuğu ve canlı toplam görünsün. |
| `sepet.png` | Sepetim | Koltukları sepete ekledikten sonra. Kilit sayacı ve toplam tutar görünür olsun. |
| `ana-sayfa.png` | Ana sayfa | Giriş yaptıktan sonra açılan liste. Bir kategori ya da şehir filtresi seçili olsun. |
| `kapi-kontrolu.png` | Bilet doğrulama | Yönetici hesabıyla, satın alınmış bir biletin e-postasındaki QR bağlantısını aç (`/Giris/Dogrula?kod=...`). Mobil genişlikte daha iyi görünür. |

## Nasıl alınır

1. Uygulamayı normal profille çalıştır: `dotnet run --project BiletSatis.Web`
2. Giriş yap, yukarıdaki sayfaları sırayla aç.
3. Tarayıcı penceresini **1280 piksel** genişliğe yakın tut — README'deki iki sütunlu
   tabloda dar görseller daha okunaklı çıkar. Kapı kontrolü için pencereyi
   daraltmak (yaklaşık 420 piksel) daha doğru bir izlenim verir; sayfa zaten
   mobil öncelikli tasarlandı.
4. Tam sayfa yerine **ilgili bölümü** kırp; boş alan görselin etkisini düşürür.
5. Dosyaları bu klasöre yukarıdaki adlarla kaydet.

## Dikkat

- Görsellerde **gerçek e-posta adresi ya da kişisel bilgi kalmasın**. Test hesabıyla
  çalış; gerekirse adresi bulanıklaştır.
- QR kodu içeren görselde kod okunabilir olabilir — o bilet geliştirme
  veritabanında kaldığı sürece sorun değil, ama gerçek bir dağıtımda paylaşma.
- PNG tercih et; 300 KB altını hedefle (GitHub'da sayfa hızlı açılsın).
