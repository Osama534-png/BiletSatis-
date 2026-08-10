// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("DOMContentLoaded", () => {
  initNavbarScroll();
  initCountUp();
  initScrollReveal();
  initFiyatKaydirici();
  initAnaSayfaFiltre();
  initDinamikStiller();
  initFavoriDugmeleri();
  initVenueMap();
  initSeatSelection();
  initFormDavranislari();
  initShowcaseRail();
  initViewToggle();
});

// Izgara / liste görünümü arasında geçiş yapar ve tercihi saklar.
function initViewToggle() {
  const toggle = document.getElementById("viewToggle");
  const grid = document.getElementById("eventRail");
  if (!toggle || !grid) return;

  const GORUNUM_ANAHTARI = "biletsatis.gorunum";
  const butonlar = toggle.querySelectorAll(".view-btn");

  const gorunumSec = (gorunum, kaydet = true) => {
    grid.classList.toggle("is-list", gorunum === "list");
    butonlar.forEach((b) => b.classList.toggle("is-active", b.dataset.view === gorunum));
    if (kaydet) {
      try {
        localStorage.setItem(GORUNUM_ANAHTARI, gorunum);
      } catch {
        /* gizli sekmede localStorage kapalı olabilir */
      }
    }
  };

  butonlar.forEach((b) => {
    b.addEventListener("click", () => gorunumSec(b.dataset.view));
  });

  let kayitli = null;
  try {
    kayitli = localStorage.getItem(GORUNUM_ANAHTARI);
  } catch {
    /* localStorage erişilemiyor olabilir */
  }

  if (kayitli === "list") gorunumSec("list", false);
}

function initShowcaseRail() {
  const rail = document.getElementById("showcaseRail");
  const prev = document.getElementById("showcasePrev");
  const next = document.getElementById("showcaseNext");
  if (!rail || !prev || !next) return;

  const step = () => rail.clientWidth * 0.7;

  // Şeridin başında/sonunda ilgili oku gizle.
  // Başlangıç eşiği sol dolgu kadar: snap hizalaması scrollLeft'i 0'a değil dolgu değerine oturtuyor.
  const syncButtons = () => {
    const basEsigi = parseFloat(getComputedStyle(rail).paddingLeft) + 4;
    const max = rail.scrollWidth - rail.clientWidth;
    prev.hidden = rail.scrollLeft <= basEsigi;
    next.hidden = rail.scrollLeft >= max - 4;
  };

  prev.addEventListener("click", () => rail.scrollBy({ left: -step(), behavior: "smooth" }));
  next.addEventListener("click", () => rail.scrollBy({ left: step(), behavior: "smooth" }));
  rail.addEventListener("scroll", syncButtons, { passive: true });
  window.addEventListener("resize", syncButtons);

  syncButtons();
}

function initNavbarScroll() {
  const navbar = document.querySelector(".navbar-clean");
  if (!navbar) return;

  const onScroll = () => {
    navbar.classList.toggle("is-scrolled", window.scrollY > 8);
  };
  onScroll();
  window.addEventListener("scroll", onScroll, { passive: true });
}

function initCountUp() {
  const targets = document.querySelectorAll(".stat-value[data-count]");
  if (!targets.length) return;

  const animate = (el) => {
    const target = parseInt(el.dataset.count, 10) || 0;
    const duration = 900;
    const start = performance.now();

    const step = (now) => {
      const progress = Math.min((now - start) / duration, 1);
      const eased = 1 - Math.pow(1 - progress, 3);
      el.textContent = Math.round(eased * target).toString();
      if (progress < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  };

  const observer = new IntersectionObserver((entries, obs) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        animate(entry.target);
        obs.unobserve(entry.target);
      }
    });
  }, { threshold: 0.4 });

  targets.forEach((el) => observer.observe(el));
}

function initScrollReveal() {
  const items = document.querySelectorAll(".reveal");
  if (!items.length) return;

  const observer = new IntersectionObserver((entries, obs) => {
    entries.forEach((entry, index) => {
      if (entry.isIntersecting) {
        setTimeout(() => entry.target.classList.add("is-visible"), index * 70);
        obs.unobserve(entry.target);
      }
    });
  }, { threshold: 0.2 });

  items.forEach((el) => observer.observe(el));
}

// Fiyat kaydırıcısının yanındaki sayıyı sürüklerken günceller. Asıl filtreleme
// sunucuda yapılır; bu yalnızca anlık geri bildirim.
function initFiyatKaydirici() {
  const kaydirici = document.getElementById("priceRange");
  const etiket = document.getElementById("priceValue");
  if (!kaydirici || !etiket) return;

  const yaz = () => {
    etiket.textContent = `${Number(kaydirici.value).toLocaleString("tr-TR")} ₺`;
  };

  kaydirici.addEventListener("input", yaz);
  yaz();
}

// Ana sayfa filtreleri: yazarken/seçerken kendiliğinden uygulanır, düğmeye
// basmak gerekmez ve sayfa yenilenmez.
//
// Filtreleme yine sunucuda yapılıyor (ölçek için şart: 2000 etkinlikte tüm
// listeyi tarayıcıya göndermek sayfayı 5 MB'a çıkarıyordu). Değişen tek şey,
// sonucun tam sayfa yerine yalnızca liste parçası olarak çekilmesi.
//
// Her tuş vuruşunda istek gitmesin diye 300 ms beklenir: "konser" yazmak 6
// istek değil 1 istek üretir.
function initAnaSayfaFiltre() {
  const form = document.getElementById("filtrePaneli");
  const kap = document.getElementById("sonuclar");
  if (!form || !kap) return;

  // JavaScript çalışıyorsa düğme gereksiz; çalışmıyorsa form normal gönderimle
  // çalışmaya devam eder, o yüzden düğme HTML'de duruyor.
  form.querySelector("button[type=submit]")?.setAttribute("hidden", "hidden");

  let zamanlayici = null;
  let sonIstek = null;

  const adresOlustur = (sayfa) => {
    const veriler = new FormData(form);
    const parametreler = new URLSearchParams();

    for (const [ad, deger] of veriler.entries()) {
      if (deger !== "" && deger !== null) parametreler.append(ad, deger);
    }

    if (sayfa && sayfa > 1) parametreler.set("sayfa", String(sayfa));
    else parametreler.delete("sayfa");

    const sorgu = parametreler.toString();
    return sorgu ? `?${sorgu}` : "/";
  };

  const uygula = async (adres, kaydir = false) => {
    // Önceki istek hâlâ sürüyorsa iptal et: hızlı yazarken eski cevabın geç
    // gelip yenisinin üstüne yazmasını engeller.
    sonIstek?.abort();
    sonIstek = new AbortController();

    kap.classList.add("yukleniyor");

    try {
      const cevap = await fetch(adres, {
        headers: { "X-Requested-With": "XMLHttpRequest" },
        signal: sonIstek.signal,
      });

      if (!cevap.ok) throw new Error(`Sunucu ${cevap.status} döndü`);

      kap.innerHTML = await cevap.text();

      // Adres çubuğu filtreyi yansıtsın: bağlantı paylaşılabilir kalır ve
      // sayfa yenilenirse aynı sonuçlar gelir.
      history.replaceState(null, "", adres);

      // Yeni basılan içerikteki kalpler ve değere bağlı stiller bağlanmalı.
      initFavoriDugmeleri();
      initDinamikStiller();

      if (kaydir) kap.scrollIntoView({ behavior: "smooth", block: "start" });
    } catch (hata) {
      if (hata.name !== "AbortError") {
        // Ağ hatası: kullanıcı boş ekranla kalmasın, normal gönderime düş.
        form.submit();
      }
    } finally {
      kap.classList.remove("yukleniyor");
    }
  };

  const geciktir = (ms) => {
    clearTimeout(zamanlayici);
    zamanlayici = setTimeout(() => uygula(adresOlustur(1)), ms);
  };

  // Metin ve kaydırıcı yazarken beklemeli; açılır liste ve kutucuk anında.
  form.addEventListener("input", (olay) => {
    geciktir(olay.target.type === "range" || olay.target.type === "text" ? 300 : 0);
  });

  form.addEventListener("change", () => geciktir(0));

  form.addEventListener("submit", (olay) => {
    olay.preventDefault();
    uygula(adresOlustur(1));
  });

  // Sayfalama bağlantıları da tam yenileme yapmasın. Liste her filtrede yeniden
  // basıldığı için bağlantılara tek tek dinleyici eklenemez; olay kap üzerinden
  // yakalanıyor.
  kap.addEventListener("click", (olay) => {
    const baglanti = olay.target.closest("a.sayfa-baglantisi");
    if (!baglanti || baglanti.classList.contains("is-pasif")) return;

    olay.preventDefault();
    uygula(baglanti.getAttribute("href"), true);
  });

  // Kategori sekmeleri ve şehir seçenekleri formun dışında; seçimi gizli alana
  // yazıp aynı akışı kullanıyorlar.
  const gizliAlanaYaz = (ad, deger) => {
    const alan = form.querySelector(`input[name="${ad}"]`);
    if (alan) alan.value = deger ?? "";
  };

  document.querySelectorAll(".category-tab").forEach((sekme) => {
    sekme.addEventListener("click", (olay) => {
      olay.preventDefault();
      const adres = new URL(sekme.href, location.origin);
      gizliAlanaYaz("kategori", adres.searchParams.get("kategori"));

      document.querySelectorAll(".category-tab").forEach((s) => s.classList.remove("is-active"));
      sekme.classList.add("is-active");

      uygula(adresOlustur(1));
    });
  });

  document.querySelectorAll(".city-option").forEach((secenek) => {
    secenek.addEventListener("click", (olay) => {
      olay.preventDefault();
      const adres = new URL(secenek.href, location.origin);
      const sehir = adres.searchParams.get("sehir");
      gizliAlanaYaz("sehir", sehir);

      document.querySelectorAll(".city-option").forEach((s) => s.classList.remove("is-active"));
      secenek.classList.add("is-active");

      const baslik = document.getElementById("cityCurrent");
      if (baslik) baslik.textContent = sehir || "Tüm Şehirler";

      document.getElementById("cityPicker")?.removeAttribute("open");

      uygula(adresOlustur(1));
    });
  });
}


function initVenueMap() {
  const map = document.getElementById("venueMap");
  if (!map) return;

  const selectors = document.querySelectorAll(".venue-block, .category-row");
  const panels = document.querySelectorAll(".seat-panel[data-block]");

  const selectBlock = (kod) => {
    selectors.forEach((el) => el.classList.toggle("is-active", el.dataset.block === kod));
    panels.forEach((panel) => panel.classList.toggle("is-active", panel.dataset.block === kod));
  };

  selectors.forEach((el) => {
    el.addEventListener("click", () => selectBlock(el.dataset.block));
  });

  // Harita yakınlaştırma
  let zoom = 1;
  const applyZoom = () => {
    map.style.transform = `scale(${zoom})`;
  };

  document.getElementById("zoomIn")?.addEventListener("click", () => {
    zoom = Math.min(zoom + 0.15, 1.8);
    applyZoom();
  });

  document.getElementById("zoomOut")?.addEventListener("click", () => {
    zoom = Math.max(zoom - 0.15, 0.7);
    applyZoom();
  });
}

// Form davranışları. Bunlar eskiden görünümlerde onsubmit="..." olarak duruyordu;
// satır içi olay öznitelikleri nonce alamadığı için CSP altında çalışmazlar, bu
// yüzden data-* öznitelikleriyle işaretlenip davranış buraya taşındı.
function initFormDavranislari() {
  // Onay isteyen formlar: data-onay="mesaj"
  document.querySelectorAll("form[data-onay]").forEach((form) => {
    form.addEventListener("submit", (olay) => {
      if (!window.confirm(form.dataset.onay)) {
        olay.preventDefault();
      }
    });
  });

  // Çift gönderimi engelleyen formlar: data-tek-gonderim="bekleme metni"
  document.querySelectorAll("form[data-tek-gonderim]").forEach((form) => {
    form.addEventListener("submit", () => {
      const dugme = form.querySelector("button[type=submit]");
      if (!dugme) return;

      dugme.disabled = true;
      dugme.textContent = form.dataset.tekGonderim;
    });
  });
}

// Favori kalbi: form gönderimini durdurup isteği arka planda yapar, yalnızca
// düğmenin görünümünü değiştirir. Öncesinde her tıklama tam sayfa yenilemesi
// tetikliyordu; ana sayfanın bütün sorguları (etkinlikler, koltuk sayıları,
// favori listesi) tek satırlık bir yazma için baştan çalışıyordu.
//
// Form olduğu gibi duruyor: JavaScript çalışmazsa ya da istek başarısız olursa
// normal gönderime düşülüyor, yani özellik JS'siz de çalışır.
function initFavoriDugmeleri() {
  document.querySelectorAll("form.favori-form").forEach((form) => {
    // Favorilerim sayfasında kalp kaldırılınca kartın da listeden çıkması
    // gerekiyor; orada tam yenileme doğru davranış.
    if (form.dataset.favoriYenile === "true") return;

    // Sonuç listesi filtreden sonra yeniden basıldığında bu fonksiyon tekrar
    // çağrılıyor; daha önce bağlanmış formlara ikinci kez dinleyici eklenmesin.
    if (form.dataset.baglandi === "1") return;
    form.dataset.baglandi = "1";

    form.addEventListener("submit", async (olay) => {
      olay.preventDefault();

      const dugme = form.querySelector(".favori-dugme");
      if (!dugme || dugme.disabled) return;

      dugme.disabled = true;

      try {
        const cevap = await fetch(form.action, {
          method: "POST",
          headers: { "X-Requested-With": "XMLHttpRequest" },
          body: new FormData(form),
        });

        if (!cevap.ok) throw new Error(`Sunucu ${cevap.status} döndü`);

        const sonuc = await cevap.json();
        const favoride = sonuc.favoride === true;

        dugme.classList.toggle("is-favori", favoride);
        dugme.textContent = favoride ? "♥" : "♡";
        dugme.setAttribute("aria-pressed", String(favoride));

        const metin = favoride ? "Favorilerden çıkar" : "Favorilere ekle";
        dugme.title = metin;
        dugme.setAttribute("aria-label", metin);
      } catch {
        // Ağ hatası ya da oturum düşmesi: formu normal yoldan gönder, kullanıcı
        // en azından sonucu görsün.
        form.submit();
        return;
      } finally {
        dugme.disabled = false;
      }
    });
  });
}

// Not: giriş animasyonunun kademeli gecikmesi buradan atanmıyor. Kart "opacity: 0"
// ile animasyona sayfa çözümlenirken başladığı için, gecikmeyi DOMContentLoaded'da
// vermek animasyonu yeniden tetikleyip titremeye yol açıyordu; o değer .gecikme-N
// sınıflarıyla veriliyor. Buradakiler animasyona bağlı olmayan, sonradan atanması
// güvenli olan stiller.
//
// Değere göre değişen stiller. Bunlar eskiden style="..." özniteliğiyle yazılıyordu,
// ama CSP'de style-src için 'unsafe-inline' kaldırıldığı için tarayıcı onları
// engelliyordu: nonce yalnızca <style> etiketlerinde çalışır, style özniteliğinde değil.
// JS ile stil atamak (CSSOM) CSP tarafından engellenmez, çünkü sayfaya metin
// enjekte edilmiyor — bu yüzden değerler data-* özniteliğinden okunup burada uygulanıyor.
function initDinamikStiller() {
  document.querySelectorAll("[data-genislik-yuzde]").forEach((el) => {
    const yuzde = parseFloat(el.dataset.genislikYuzde);
    if (!Number.isNaN(yuzde)) el.style.width = `${yuzde}%`;
  });

  document.querySelectorAll("[data-yukseklik-px]").forEach((el) => {
    const px = parseFloat(el.dataset.yukseklikPx);
    if (!Number.isNaN(px)) el.style.height = `${px}px`;
  });

  document.querySelectorAll("[data-arka-plan]").forEach((el) => {
    el.style.background = el.dataset.arkaPlan;
  });
}

// Salon haritasında çoklu koltuk seçimi. Seçilen koltuklar forma gizli input olarak
// yazılır; sunucu tarafında hepsi tek bir rezervasyon isteği olarak değerlendirilir.
function initSeatSelection() {
  const form = document.getElementById("seatForm");
  if (!form) return;

  const bar = document.getElementById("selectionBar");
  const sayacEl = document.getElementById("selectionCount");
  const koltuklarEl = document.getElementById("selectionSeats");
  const toplamEl = document.getElementById("selectionTotal");
  const maxKoltuk = parseInt(form.dataset.maxKoltuk, 10) || 6;

  const secilenler = new Map();

  const paraFormat = new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: "TRY",
    maximumFractionDigits: 0,
  });

  const yenile = () => {
    const adet = secilenler.size;
    bar.hidden = adet === 0;
    if (adet === 0) return;

    const toplam = [...secilenler.values()].reduce((t, k) => t + k.fiyat, 0);
    const koltukNolar = [...secilenler.values()].map((k) => k.koltukNo).sort();

    sayacEl.textContent = `${adet} koltuk seçildi`;
    koltuklarEl.textContent = koltukNolar.join(" · ");
    toplamEl.textContent = paraFormat.format(toplam);
  };

  form.querySelectorAll(".seat.seat-free").forEach((koltuk) => {
    koltuk.addEventListener("click", () => {
      const id = koltuk.dataset.biletId;

      if (secilenler.has(id)) {
        secilenler.delete(id);
        koltuk.classList.remove("is-selected");
        koltuk.setAttribute("aria-pressed", "false");
        form.querySelector(`input[data-bilet-id="${id}"]`)?.remove();
        yenile();
        return;
      }

      if (secilenler.size >= maxKoltuk) {
        koltuk.classList.add("seat-shake");
        setTimeout(() => koltuk.classList.remove("seat-shake"), 400);
        sayacEl.textContent = `En fazla ${maxKoltuk} koltuk seçebilirsiniz`;
        return;
      }

      secilenler.set(id, {
        koltukNo: koltuk.dataset.koltukNo,
        fiyat: parseFloat(koltuk.dataset.fiyat) || 0,
      });
      koltuk.classList.add("is-selected");
      koltuk.setAttribute("aria-pressed", "true");

      const gizli = document.createElement("input");
      gizli.type = "hidden";
      gizli.name = "biletIds";
      gizli.value = id;
      gizli.dataset.biletId = id;
      form.appendChild(gizli);

      yenile();
    });
  });
}
