// Race-condition testi: tek bir bilete onlarca sanal kullanıcı aynı anda "Sepete Ekle" gönderir.
// Amaç: atomik UPDATE ... WHERE Durum='Satışta' korumasının gerçek eşzamanlı yük altında
// hiçbir "double booking"e izin vermediğini kanıtlamak.
//
// Çalıştırma:
//   k6 run loadtests/k6/add-to-cart-test.js
//   (uygulama http://localhost:5052 adresinde çalışıyor ve etkinlikte en az 1 "Satışta" bilet olmalı)
//
// Farklı adres/etkinlik için:
//   k6 run -e BASE_URL=http://localhost:5052 -e ETKINLIK_ID=1 loadtests/k6/add-to-cart-test.js

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5052';
const ETKINLIK_ID = __ENV.ETKINLIK_ID || 1;
const ES_ZAMANLI_KULLANICI = Number(__ENV.VUS || 50);

const sepeteEklemeBasarili = new Counter('sepete_ekleme_basarili');
const sepeteEklemeZatenAlinmis = new Counter('sepete_ekleme_zaten_alinmis');

export const options = {
  scenarios: {
    ayniBileteSaldiri: {
      executor: 'shared-iterations',
      vus: ES_ZAMANLI_KULLANICI,
      iterations: ES_ZAMANLI_KULLANICI,
      maxDuration: '30s',
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

export function setup() {
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
  // Her VU kendi cookie jar'ına sahiptir (k6 varsayılanı) — bu da gerçek dünyada olduğu gibi
  // her sanal kullanıcının ayrı bir kullaniciId çerezine (ve ayrı bir antiforgery token'a) sahip olmasını sağlar.
  const listeSayfasi = http.get(`${BASE_URL}/Biletler/Index?etkinlikId=${ETKINLIK_ID}`);
  const token = antiForgeryTokenAl(listeSayfasi.body);

  const res = http.post(
    `${BASE_URL}/Biletler/SepeteEkle`,
    { biletId: data.biletId, __RequestVerificationToken: token },
    { redirects: 5 },
  );

  const basarili = res.url && res.url.includes('OdemeStub');

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
