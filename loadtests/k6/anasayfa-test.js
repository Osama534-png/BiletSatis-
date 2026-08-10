// Ana sayfa ölçümü: etkinlik sayısı arttıkça sayfanın ne kadar yavaşladığını ölçer.
//
// Ana sayfa şu an TÜM etkinlikleri tek sayfada listeler; her etkinlik için müsait
// koltuk sayısı ve en düşük fiyat ayrıca hesaplanır. Sunucu tarafı sayfalama
// gerekip gerekmediğine tahminle değil, bu ölçümle karar veriyoruz.
//
// Çalıştırma:
//   k6 run loadtests/k6/anasayfa-test.js
//   k6 run -e VUS=10 -e SURE=20s loadtests/k6/anasayfa-test.js

import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5052';
const ES_ZAMANLI_KULLANICI = Number(__ENV.VUS || 10);
const SURE = __ENV.SURE || '20s';

const anaSayfaSuresi = new Trend('anasayfa_suresi', true);
const sayfaBoyutu = new Trend('anasayfa_boyutu_kb', false);

export const options = {
  // k6 varsayılan olarak her yinelemede çerez kavanozunu temizler; o zaman her
  // ölçümde yeniden giriş yapmak gerekir ve ölçtüğümüz şey ana sayfa olmaktan
  // çıkar. Oturumu VU boyunca koruyoruz.
  noCookiesReset: true,

  scenarios: {
    anaSayfaGezinme: {
      executor: 'constant-vus',
      vus: ES_ZAMANLI_KULLANICI,
      duration: SURE,
    },
  },
};

function antiForgeryTokenAl(html) {
  const match = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  return match ? match[1] : null;
}

// Her VU kendi hesabıyla giriş yapar; ana sayfa giriş gerektiriyor.
export function setup() {
  return {};
}

let girisYapildi = false;

function girisSaglaAsync() {
  if (girisYapildi) return;

  const kayitSayfasi = http.get(`${BASE_URL}/Account/KayitOl`);
  const kayitToken = antiForgeryTokenAl(kayitSayfasi.body);
  const eposta = `anasayfa-${__VU}-${Date.now()}@test.local`;

  http.post(
    `${BASE_URL}/Account/KayitOl`,
    {
      Ad: `Ana Sayfa Testi ${__VU}`,
      Email: eposta,
      Sifre: 'YukTest123',
      SifreTekrar: 'YukTest123',
      __RequestVerificationToken: kayitToken,
    },
    { redirects: 5 },
  );

  const girisSayfasi = http.get(`${BASE_URL}/Account/GirisYap`);
  http.post(
    `${BASE_URL}/Account/GirisYap`,
    { Email: eposta, Sifre: 'YukTest123', __RequestVerificationToken: antiForgeryTokenAl(girisSayfasi.body) },
    { redirects: 5 },
  );

  girisYapildi = true;
}

export default function () {
  girisSaglaAsync();

  const cevap = http.get(`${BASE_URL}/`);

  anaSayfaSuresi.add(cevap.timings.duration);
  sayfaBoyutu.add(cevap.body ? cevap.body.length / 1024 : 0);

  check(cevap, {
    'ana sayfa 200 dondu': (r) => r.status === 200,
    'etkinlik listesi render edildi': (r) => r.body && r.body.includes('event-card'),
  });
}
