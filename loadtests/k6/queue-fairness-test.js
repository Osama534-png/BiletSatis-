// Kuyruk adalet testi: M sanal kullanıcı eşzamanlı olarak bekleme kuyruğuna katılır,
// ardından admin N < M kişilik satış başlatır. Amaç: SQL Server IDENTITY tabanlı SiraNo
// sıralamasının, eşzamanlı katılımlarda bile en düşük SiraNo'ya sahip kullanıcılara
// (ve sadece onlara) satın alma hakkı tanındığını kanıtlamak.
//
// Uygulama artık tüm işlemler için giriş zorunlu olduğundan, her sanal kullanıcı (VU) önce
// kendi tek kullanımlık hesabını oluşturup giriş yapar (aynı kullanıcı tekrar sıraya giremez,
// bu yüzden burada benzersiz hesap kullanmak zaten zorunlu).
//
// Çalıştırma:
//   k6 run loadtests/k6/queue-fairness-test.js
//
// Farklı adres/etkinlik/parametreler için:
//   k6 run -e BASE_URL=http://localhost:5052 -e ETKINLIK_ID=1 -e M=30 -e N=10 loadtests/k6/queue-fairness-test.js

import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5052';
const ETKINLIK_ID = __ENV.ETKINLIK_ID || 1;
const M = Number(__ENV.M || 30); // kuyruğa katılacak toplam kullanıcı sayısı
const N = Number(__ENV.N || 10); // satışın açılacağı kişi sayısı (N < M olmalı)
const ADMIN_EMAIL = __ENV.ADMIN_EMAIL || 'admin@biletsatis.local';
const ADMIN_SIFRE = __ENV.ADMIN_SIFRE || 'Admin123!';

export const options = {
  scenarios: {
    kuyrugaKatilim: {
      executor: 'shared-iterations',
      vus: M,
      iterations: M,
      maxDuration: '60s',
    },
  },
};

function antiForgeryTokenAl(html) {
  const match = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  return match ? match[1] : null;
}

function siraNoAl(html) {
  const match = html.match(/class="seat-no[^"]*"[^>]*>#(\d+)/);
  return match ? Number(match[1]) : null;
}

function girisYap(email, sifre) {
  const sayfa = http.get(`${BASE_URL}/Account/GirisYap`);
  const token = antiForgeryTokenAl(sayfa.body);
  return http.post(
    `${BASE_URL}/Account/GirisYap`,
    { Email: email, Sifre: sifre, __RequestVerificationToken: token },
    { redirects: 5 },
  );
}

function kayitOlVeGirisYap() {
  const kayitSayfasi = http.get(`${BASE_URL}/Account/KayitOl`);
  const token = antiForgeryTokenAl(kayitSayfasi.body);
  const email = `yuktest-kuyruk-${__VU}-${Date.now()}@test.local`;

  http.post(
    `${BASE_URL}/Account/KayitOl`,
    {
      Ad: `Kuyruk Testi ${__VU}`,
      Email: email,
      Sifre: 'YukTest123',
      SifreTekrar: 'YukTest123',
      __RequestVerificationToken: token,
    },
    { redirects: 5 },
  );

  // Kayıt artık otomatik giriş yaptırmıyor: e-posta doğrulaması eklendiğinde akış
  // "doğrulama bekleniyor" sayfasına yönlendirilir hâle geldi. Bu yüzden oturum
  // açmak için ayrıca giriş yapılıyor.
  girisYap(email, 'YukTest123');
}

export default function () {
  // Her VU önce kendi tek kullanımlık hesabını oluşturur; kendi çerez kavanozunda
  // giriş yapmış olarak kalır. Bu da gerçekten farklı kullanıcıların eşzamanlı
  // katılımını simüle eder.
  kayitOlVeGirisYap();

  // "Sıraya Gir" artık bir POST işlemi (yan etkili GET/CSRF riskini önlemek için) —
  // token'ı Kuyruk Durumu sayfasındaki formdan alıyoruz.
  const durumSayfasiOnce = http.get(`${BASE_URL}/Kuyruk/Durum?etkinlikId=${ETKINLIK_ID}`);
  const token = antiForgeryTokenAl(durumSayfasiOnce.body);

  const durumSayfasi = http.post(
    `${BASE_URL}/Kuyruk/Katil`,
    { etkinlikId: ETKINLIK_ID, __RequestVerificationToken: token },
    { redirects: 5 },
  );
  const siraNo = siraNoAl(durumSayfasi.body);

  check(siraNo, {
    'sira numarasi atandi': (s) => s !== null && s > 0,
  });
}

export function teardown() {
  // Admin panelinden N kişilik satış başlatmak için admin olarak giriş yapmak gerekiyor.
  girisYap(ADMIN_EMAIL, ADMIN_SIFRE);

  const adminSayfasi = http.get(`${BASE_URL}/Admin/Index`);
  const token = antiForgeryTokenAl(adminSayfasi.body);

  http.post(
    `${BASE_URL}/Admin/SatisiBaslat`,
    { etkinlikId: ETKINLIK_ID, n: N, __RequestVerificationToken: token },
    { redirects: 5 },
  );

  const ozet = http.get(`${BASE_URL}/Admin/Ozet?etkinlikId=${ETKINLIK_ID}`).json();
  const kuyruk = (ozet.kuyrukSiraNolari || []).slice().sort((a, b) => a.siraNo - b.siraNo);

  // Not: bu test aynı etkinliğe karşı art arda çalıştırılabildiği için (dev veritabanı
  // temizlenmez), "HakTanindi" sayısı geçmiş çalıştırmalardan kalan kayıtları da içerebilir.
  // Bu yüzden "tam olarak N kişi" yerine, çalıştırma sayısından bağımsız kalan asıl adalet
  // kuralını test ediyoruz: hak tanınan HİÇBİR kişinin SiraNo'su, hâlâ bekleyen HİÇBİR
  // kişininkinden yüksek olamaz — yani kimse sırasından önce geçemez.
  const hakTanindilar = kuyruk.filter((k) => k.durum === 'HakTanindi');
  const beklemedekiler = kuyruk.filter((k) => k.durum === 'Beklemede');

  const maxHakTanindiSiraNo = hakTanindilar.length ? Math.max(...hakTanindilar.map((k) => k.siraNo)) : -Infinity;
  const minBeklemedeSiraNo = beklemedekiler.length ? Math.min(...beklemedekiler.map((k) => k.siraNo)) : Infinity;

  check(null, {
    'en az N kisiye hak tanindi': () => hakTanindilar.length >= N,
    'hak taninan hicbir SiraNo, bekleyen hicbir SiraNodan yuksek degil (kimse sirasini atlamadi)': () =>
      maxHakTanindiSiraNo < minBeklemedeSiraNo,
  });

  console.log(`Kuyruk (SiraNo -> Durum): ${JSON.stringify(kuyruk)}`);
}
