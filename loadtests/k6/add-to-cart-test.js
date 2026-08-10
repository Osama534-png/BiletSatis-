// Race-condition testi: tek bir bilete onlarca sanal kullanıcı aynı anda "Sepete Ekle" gönderir.
// Amaç: atomik UPDATE ... WHERE Durum='Satışta' korumasının gerçek eşzamanlı yük altında
// hiçbir "double booking"e izin vermediğini kanıtlamak.
//
// Uygulama artık tüm işlemler için giriş zorunlu olduğundan, her sanal kullanıcı (VU) önce
// kendi tek kullanımlık hesabını oluşturup giriş yapar — gerçek dünyada olduğu gibi
// birbirinden bağımsız, gerçekten farklı kullanıcıları simüle eder.
//
// Çalıştırma:
//   k6 run loadtests/k6/add-to-cart-test.js
//   (uygulama http://localhost:5052 adresinde çalışıyor, admin@biletsatis.local/Admin123!
//    seed edilmiş olmalı ve etkinlikte en az 1 "Satışta" bilet olmalı)
//
// Farklı adres/etkinlik için:
//   k6 run -e BASE_URL=http://localhost:5052 -e ETKINLIK_ID=1 loadtests/k6/add-to-cart-test.js

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5052';
const ETKINLIK_ID = __ENV.ETKINLIK_ID || 1;
const ES_ZAMANLI_KULLANICI = Number(__ENV.VUS || 50);
const ADMIN_EMAIL = __ENV.ADMIN_EMAIL || 'admin@biletsatis.local';
const ADMIN_SIFRE = __ENV.ADMIN_SIFRE || 'Admin123!';

const sepeteEklemeBasarili = new Counter('sepete_ekleme_basarili');
const sepeteEklemeZatenAlinmis = new Counter('sepete_ekleme_zaten_alinmis');

export const options = {
  scenarios: {
    ayniBileteSaldiri: {
      executor: 'shared-iterations',
      vus: ES_ZAMANLI_KULLANICI,
      iterations: ES_ZAMANLI_KULLANICI,
      maxDuration: '60s',
    },
  },
  thresholds: {
    // Kritik assertion: ne kadar eşzamanlı istek gelirse gelsin, aynı bilet için
    // tam olarak BİR başarılı "sepete ekleme" olmalı. Fazlası = double booking (test FAIL olur).
    sepete_ekleme_basarili: ['count==1'],
    http_req_failed: ['rate<0.01'],
  },
};

function antiForgeryTokenAl(html) {
  const match = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  return match ? match[1] : null;
}

// Bu fonksiyonun içindeki ardışık istekler aynı yürütme bağlamının (VU ya da setup/teardown)
// çerez kavanozunu paylaşır, bu yüzden giriş sonrası oturum sonraki isteklerde geçerli kalır.
function girisYap(email, sifre) {
  const sayfa = http.get(`${BASE_URL}/Account/GirisYap`);
  const token = antiForgeryTokenAl(sayfa.body);
  return http.post(
    `${BASE_URL}/Account/GirisYap`,
    { Email: email, Sifre: sifre, __RequestVerificationToken: token },
    { redirects: 5 },
  );
}

// Her VU kendi tek kullanımlık hesabını oluşturur (kayıt olma, otomatik giriş yapar) —
// gerçekten birbirinden bağımsız kullanıcıları simüle etmek için.
function kayitOlVeGirisYap() {
  const kayitSayfasi = http.get(`${BASE_URL}/Account/KayitOl`);
  const token = antiForgeryTokenAl(kayitSayfasi.body);
  const email = `yuktest-sepet-${__VU}-${Date.now()}@test.local`;

  http.post(
    `${BASE_URL}/Account/KayitOl`,
    {
      Ad: `Yük Testi ${__VU}`,
      Email: email,
      Sifre: 'YukTest123',
      SifreTekrar: 'YukTest123',
      __RequestVerificationToken: token,
    },
    { redirects: 5 },
  );
  // Kayıt artık otomatik giriş yaptırmıyor: e-posta doğrulaması eklendiğinde
  // akış "doğrulama bekleniyor" sayfasına yönlendirilir hâle geldi. Bu yüzden
  // oturum açmak için ayrıca giriş yapılıyor.
  girisYap(email, 'YukTest123');
}

export function setup() {
  girisYap(ADMIN_EMAIL, ADMIN_SIFRE);

  const ozet = http.get(`${BASE_URL}/Admin/Ozet?etkinlikId=${ETKINLIK_ID}`).json();
  const satistakiBilet = ozet.biletDurumlari.find((b) => b.durum === 'Satista' || b.Durum === 'Satista');

  if (!satistakiBilet) {
    throw new Error('Satışta durumda bilet bulunamadı — önce uygulamayı seed verisiyle çalıştırın.');
  }

  const biletId = satistakiBilet.id ?? satistakiBilet.Id;
  console.log(`Hedef bilet: BiletId=${biletId} (${ES_ZAMANLI_KULLANICI} sanal kullanıcı bu bilete saldıracak)`);
  return { biletId };
}

export default function (data) {
  kayitOlVeGirisYap();

  const listeSayfasi = http.get(`${BASE_URL}/Biletler/Index?etkinlikId=${ETKINLIK_ID}`);
  const token = antiForgeryTokenAl(listeSayfasi.body);

  // Çoklu koltuk seçimi eklendiğinde uç nokta "biletId" yerine "etkinlikId" +
  // "biletIds" almaya başladı. Tek bilete saldırdığımız için biletIds tek değer.
  const res = http.post(
    `${BASE_URL}/Biletler/SepeteEkle`,
    {
      etkinlikId: ETKINLIK_ID,
      biletIds: data.biletId,
      __RequestVerificationToken: token,
    },
    { redirects: 5 },
  );

  // Başarılı rezervasyon sepete yönlendirir; başarısız olan etkinlik sayfasına
  // hata mesajıyla döner. (Eskiden tek biletlik ödeme sayfasına gidiliyordu.)
  const basarili = res.url && res.url.includes('Sepetim');

  if (basarili) {
    sepeteEklemeBasarili.add(1);
  } else {
    sepeteEklemeZatenAlinmis.add(1);
  }

  check(res, {
    'HTTP yaniti basarili (200)': (r) => r.status === 200,
  });
}

export function teardown(data) {
  girisYap(ADMIN_EMAIL, ADMIN_SIFRE);

  const ozet = http.get(`${BASE_URL}/Admin/Ozet?etkinlikId=${ETKINLIK_ID}`).json();
  const hedefBilet = ozet.biletDurumlari.find((b) => (b.id ?? b.Id) === data.biletId);

  check(hedefBilet, {
    'bilet ya Sepette ya da Satildi (hicbir zaman tekrar Satista degil)': (b) =>
      b && (b.durum === 'Sepette' || b.durum === 'Satildi' || b.Durum === 'Sepette' || b.Durum === 'Satildi'),
    'toplam satilan+sepetteki bilet sayisi toplam bilet sayisini gecmiyor': () =>
      ozet.satildiSayisi + ozet.sepetteSayisi <= ozet.toplamBiletSayisi,
  });

  console.log(`Test sonrasi bilet durumu: ${JSON.stringify(hedefBilet)}`);
}
