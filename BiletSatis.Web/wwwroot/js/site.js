// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("DOMContentLoaded", () => {
  initNavbarScroll();
  initCountUp();
  initScrollReveal();
  initEventSearch();
  initVenueMap();
  initSeatSelection();
  initFormDavranislari();
  initShowcaseRail();
  initCategoryNav();
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

// Kategori şeridindeki vurgu hapını aktif sekmenin altına kaydırır.
function initCategoryNav() {
  const nav = document.getElementById("categoryNav");
  const indicator = document.getElementById("categoryIndicator");
  if (!nav || !indicator) return;

  const moveIndicator = () => {
    const active = nav.querySelector(".category-tab.is-active");
    if (!active) return;
    indicator.style.width = `${active.offsetWidth}px`;
    indicator.style.transform = `translateX(${active.offsetLeft}px)`;
  };

  // Sekmeyi şerit içinde yatay olarak görünür yapar — sayfayı dikey kaydırmaz.
  const seritteGoster = (tab) => {
    const solTasma = tab.offsetLeft - nav.scrollLeft;
    const sagTasma = tab.offsetLeft + tab.offsetWidth - (nav.scrollLeft + nav.clientWidth);
    if (solTasma < 0) {
      nav.scrollTo({ left: tab.offsetLeft - 16, behavior: "smooth" });
    } else if (sagTasma > 0) {
      nav.scrollTo({ left: nav.scrollLeft + sagTasma + 16, behavior: "smooth" });
    }
  };

  // Seçilen kategorinin sonuçları görünsün diye listeye kaydırır.
  const browseSection = document.querySelector(".browse-layout");
  const listeyeKaydir = () => {
    browseSection?.scrollIntoView({ behavior: "smooth", block: "start" });
  };

  nav.querySelectorAll(".category-tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      moveIndicator();
      seritteGoster(tab);
      listeyeKaydir();
    });
  });

  // Filtre sıfırlandığında aktif sekme dışarıdan değişebilir.
  document.getElementById("filterReset")?.addEventListener("click", moveIndicator);

  window.addEventListener("resize", moveIndicator);
  // Yazı tipi geç yüklenirse sekme genişlikleri değişir.
  document.fonts?.ready.then(moveIndicator);

  moveIndicator();
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

function initEventSearch() {
  const input = document.getElementById("eventSearch");
  const rail = document.getElementById("eventRail");
  if (!input || !rail) return;

  const searchBar = input.closest(".search-bar");
  const clearBtn = document.getElementById("eventSearchClear");
  const emptyMsg = document.getElementById("searchEmpty");
  const countEl = document.getElementById("resultCount");
  const dateChips = document.getElementById("dateChips");
  const categoryNav = document.getElementById("categoryNav");
  const priceRange = document.getElementById("priceRange");
  const priceValue = document.getElementById("priceValue");
  const showSoldOut = document.getElementById("showSoldOut");
  const sortSelect = document.getElementById("sortSelect");
  const resetBtn = document.getElementById("filterReset");
  const cityPicker = document.getElementById("cityPicker");
  const cityCurrent = document.getElementById("cityCurrent");
  const cards = Array.from(rail.querySelectorAll(".event-card"));

  const SEHIR_ANAHTARI = "biletsatis.sehir";

  let dateRange = "all";
  let category = "all";
  let city = "all";

  // Seçilen aralığın son günü — "all" ise sınır yok.
  const rangeLimit = () => {
    if (dateRange === "all") return null;
    const limit = new Date();
    limit.setHours(23, 59, 59, 999);
    limit.setDate(limit.getDate() + (dateRange === "week" ? 7 : 30));
    return limit;
  };

  // Kartları seçilen ölçüte göre yeniden dizer.
  // Fiyatı olmayan (tükenmiş) etkinlikler her iki yönde de sona atılır.
  const applySort = () => {
    if (!sortSelect) return;
    const olcut = sortSelect.value;

    const siralanmis = [...cards].sort((a, b) => {
      const fiyatA = Number(a.dataset.price || 0);
      const fiyatB = Number(b.dataset.price || 0);

      switch (olcut) {
        case "price-asc":
        case "price-desc": {
          if (fiyatA === 0 && fiyatB === 0) return 0;
          if (fiyatA === 0) return 1;
          if (fiyatB === 0) return -1;
          return olcut === "price-asc" ? fiyatA - fiyatB : fiyatB - fiyatA;
        }
        case "name":
          return (a.dataset.name || "").localeCompare(b.dataset.name || "", "tr");
        default:
          return new Date(a.dataset.date) - new Date(b.dataset.date);
      }
    });

    siralanmis.forEach((card) => rail.appendChild(card));
  };

  const applyFilter = () => {
    const term = input.value.trim().toLowerCase();
    const maxPrice = priceRange ? Number(priceRange.value) : Infinity;
    const limit = rangeLimit();

    searchBar?.classList.toggle("has-value", term.length > 0);
    if (priceValue && priceRange) priceValue.textContent = `${priceRange.value} ₺`;

    let visibleCount = 0;
    cards.forEach((card) => {
      const nameOk = !term || (card.dataset.name || "").includes(term);
      const priceOk = Number(card.dataset.price || 0) <= maxPrice;
      // Varsayılan olarak tükenenler gizli; kutu işaretliyse hepsi görünür.
      const availableOk = showSoldOut?.checked || Number(card.dataset.available || 0) > 0;
      const dateOk = !limit || new Date(card.dataset.date) <= limit;
      const categoryOk = category === "all" || card.dataset.category === category;
      const cityOk = city === "all" || card.dataset.city === city;

      const matches = nameOk && priceOk && availableOk && dateOk && categoryOk && cityOk;
      card.classList.toggle("is-filtered-out", !matches);
      if (matches) visibleCount += 1;
    });

    if (emptyMsg) emptyMsg.hidden = visibleCount > 0;
    if (countEl) countEl.textContent = visibleCount;
  };

  input.addEventListener("input", applyFilter);
  priceRange?.addEventListener("input", applyFilter);
  showSoldOut?.addEventListener("change", applyFilter);
  sortSelect?.addEventListener("change", applySort);

  dateChips?.querySelectorAll(".chip").forEach((chip) => {
    chip.addEventListener("click", () => {
      dateChips.querySelectorAll(".chip").forEach((c) => c.classList.remove("is-active"));
      chip.classList.add("is-active");
      dateRange = chip.dataset.range;
      applyFilter();
    });
  });

  categoryNav?.querySelectorAll(".category-tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      categoryNav.querySelectorAll(".category-tab").forEach((t) => t.classList.remove("is-active"));
      tab.classList.add("is-active");
      category = tab.dataset.category;
      applyFilter();
    });
  });

  // Şehir seçimini uygular; kayitli=true ise tercihi tarayıcıda saklar.
  const sehriSec = (secim, kaydet = true) => {
    const secenekler = cityPicker?.querySelectorAll(".city-option");
    if (!secenekler?.length) return;

    // Kayıtlı şehir artık listede yoksa "Tüm Şehirler"e düş.
    const gecerli = Array.from(secenekler).some((o) => o.dataset.city === secim);
    city = gecerli ? secim : "all";

    secenekler.forEach((o) => o.classList.toggle("is-active", o.dataset.city === city));
    if (cityCurrent) {
      cityCurrent.textContent = city === "all" ? "Tüm Şehirler" : city;
    }
    if (kaydet) {
      try {
        localStorage.setItem(SEHIR_ANAHTARI, city);
      } catch {
        /* gizli sekmede localStorage kapalı olabilir */
      }
    }
  };

  cityPicker?.querySelectorAll(".city-option").forEach((option) => {
    option.addEventListener("click", () => {
      sehriSec(option.dataset.city);
      cityPicker.open = false;
      applyFilter();
    });
  });

  // Menü dışına tıklanınca kapat.
  document.addEventListener("click", (olay) => {
    if (cityPicker?.open && !cityPicker.contains(olay.target)) cityPicker.open = false;
  });

  clearBtn?.addEventListener("click", () => {
    input.value = "";
    applyFilter();
    input.focus();
  });

  resetBtn?.addEventListener("click", () => {
    input.value = "";
    if (priceRange) priceRange.value = priceRange.max;
    if (showSoldOut) showSoldOut.checked = false;
    dateRange = "all";
    category = "all";
    categoryNav?.querySelectorAll(".category-tab").forEach((t) => {
      t.classList.toggle("is-active", t.dataset.category === "all");
    });
    dateChips?.querySelectorAll(".chip").forEach((c) => {
      c.classList.toggle("is-active", c.dataset.range === "all");
    });
    sehriSec("all");
    if (sortSelect) sortSelect.value = "date";
    applySort();
    applyFilter();
  });

  // Önceki ziyaretten kalan şehir tercihini geri yükle.
  let kayitliSehir = null;
  try {
    kayitliSehir = localStorage.getItem(SEHIR_ANAHTARI);
  } catch {
    /* localStorage erişilemiyor olabilir */
  }

  if (kayitliSehir && kayitliSehir !== "all") {
    sehriSec(kayitliSehir, false);
  }

  // Varsayılan durumda da tükenenler gizli olmalı; filtre açılışta bir kez çalışır.
  applyFilter();
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
