// Kuyruk adalet testi: M sanal kullanıcı eşzamanlı olarak bekleme kuyruğuna katılır,
// ardından admin N < M kişilik satış başlatır. Amaç: SQL Server IDENTITY tabanlı SiraNo
// sıralamasının, eşzamanlı katılımlarda bile en düşük SiraNo'ya sahip kullanıcılara
// (ve sadece onlara) satın alma hakkı tanındığını kanıtlamak.
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

export const options = {
  scenarios: {
    kuyrugaKatilim: {
      executor: 'shared-iterations',
      vus: M,
      iterations: M,
      maxDuration: '30s',
    },
  },
};

function antiForgeryTokenAl(html) {
  const match = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  return match ? match[1] : null;
}

function siraNoAl(html) {
  const match = html.match(/Sıra Numaranız:\s*<\/strong>?\s*(\d+)|Sıra Numaranız:\s*(\d+)/);
  if (!match) return null;
  return Number(match[1] || match[2]);
}

export default function () {
  // Her VU kendi cookie jar'ına (dolayısıyla kendi kullaniciId'sine) sahip olduğu için
  // bu istekler gerçekten farklı kullanıcıların eşzamanlı katılımını simüle eder.
  const durumSayfasi = http.get(`${BASE_URL}/Kuyruk/Katil?etkinlikId=${ETKINLIK_ID}`);
  const siraNo = siraNoAl(durumSayfasi.body);

  check(siraNo, {
    'sira numarasi atandi': (s) => s !== null && s > 0,
  });
}

export function teardown() {
  // Admin panelinden N kişilik satış başlatmak için antiforgery token gerekiyor.
  const adminSayfasi = http.get(`${BASE_URL}/Admin/Index`);
  const token = antiForgeryTokenAl(adminSayfasi.body);

  http.post(
    `${BASE_URL}/Admin/SatisiBaslat`,
    { etkinlikId: ETKINLIK_ID, n: N, __RequestVerificationToken: token },
    { redirects: 5 },
  );

  const ozet = http.get(`${BASE_URL}/Admin/Ozet?etkinlikId=${ETKINLIK_ID}`).json();
  const kuyruk = (ozet.kuyrukSiraNolari || []).slice().sort((a, b) => a.siraNo - b.siraNo);

  const beklemedeVeUstu = kuyruk.filter((k) => k.durum === 'Beklemede' || k.durum === 'HakTanindi');
  const enDusukN = beklemedeVeUstu.slice(0, N).map((k) => k.siraNo);
  const kalanlar = beklemedeVeUstu.slice(N);

  const enDusukNHepsiHakTanindi = beklemedeVeUstu
    .filter((k) => enDusukN.includes(k.siraNo))
    .every((k) => k.durum === 'HakTanindi');

  const kalanlarinHicbiriHakTanindiDegil = kalanlar.every((k) => k.durum !== 'HakTanindi');

  check(null, {
    'en dusuk SiraNo degerlerine sahip N kisi HakTanindi aldi': () => enDusukNHepsiHakTanindi,
    'daha yuksek SiraNo degerine sahip kimse atlanip hak almadi': () => kalanlarinHicbiriHakTanindiDegil,
  });

  console.log(`Kuyruk (SiraNo -> Durum): ${JSON.stringify(kuyruk)}`);
}
