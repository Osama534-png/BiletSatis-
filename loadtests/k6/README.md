# k6 Yük Testleri

Bu klasördeki testler, BiletSatis uygulamasının eşzamanlılık (race condition) korumalarını
gerçek yük altında doğrular.

## Ön koşullar

- [k6](https://k6.io/docs/get-started/installation/) kurulu olmalı.
- Uygulama çalışıyor olmalı: `dotnet run --project BiletSatis.Web` (varsayılan: `http://localhost:5052`).
- Veritabanında en az bir etkinlik ve "Satışta" durumda biletler bulunmalı (varsayılan seed verisi yeterli).

## Testler

### 1. add-to-cart-test.js — Oversell (double booking) yok testi

Onlarca sanal kullanıcı aynı anda tek bir bilete "Sepete Ekle" gönderir.

```bash
k6 run loadtests/k6/add-to-cart-test.js
```

**Assertion (threshold):** `sepete_ekleme_basarili` sayacı tam olarak `1` olmalı — kaç eşzamanlı
istek gelirse gelsin, bilet sadece bir kişiye satılabilir. Threshold sağlanmazsa k6 sürecini
hata koduyla sonlandırır.

### 2. queue-fairness-test.js — Kuyruk adalet testi

M sanal kullanıcı eşzamanlı olarak bekleme kuyruğuna katılır, ardından admin N < M kişilik
satış başlatır.

```bash
k6 run -e M=30 -e N=10 loadtests/k6/queue-fairness-test.js
```

**Assertion:** Satış açıldığında hak tanınan kişiler, kuyruğa katılan tüm kullanıcılar arasından
**tam olarak en düşük SiraNo değerine sahip N kişi** olmalı — hiçbiri atlanmamalı, hiçbiri
sırasından önce hak almamalı.

## Parametreler

Her iki script de ortam değişkenleriyle özelleştirilebilir:

| Değişken | Varsayılan | Açıklama |
|---|---|---|
| `BASE_URL` | `http://localhost:5052` | Uygulamanın adresi |
| `ETKINLIK_ID` | `1` | Test edilecek etkinlik |
| `VUS` (add-to-cart) | `50` | Aynı bilete saldıran sanal kullanıcı sayısı |
| `M` (queue-fairness) | `30` | Kuyruğa katılacak toplam kullanıcı |
| `N` (queue-fairness) | `10` | Satışın açılacağı kişi sayısı |
