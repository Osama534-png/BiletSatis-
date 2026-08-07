// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("DOMContentLoaded", () => {
  initNavbarScroll();
  initCountUp();
  initScrollReveal();
  initEventSearch();
  initVenueMap();
  initShowcaseRail();
  initCategoryNav();
});

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
  const onlyAvailable = document.getElementById("onlyAvailable");
  const resetBtn = document.getElementById("filterReset");
  const cards = Array.from(rail.querySelectorAll(".event-card"));

  let dateRange = "all";
  let category = "all";

  // Seçilen aralığın son günü — "all" ise sınır yok.
  const rangeLimit = () => {
    if (dateRange === "all") return null;
    const limit = new Date();
    limit.setHours(23, 59, 59, 999);
    limit.setDate(limit.getDate() + (dateRange === "week" ? 7 : 30));
    return limit;
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
      const availableOk = !onlyAvailable?.checked || Number(card.dataset.available || 0) > 0;
      const dateOk = !limit || new Date(card.dataset.date) <= limit;
      const categoryOk = category === "all" || card.dataset.category === category;

      const matches = nameOk && priceOk && availableOk && dateOk && categoryOk;
      card.classList.toggle("is-filtered-out", !matches);
      if (matches) visibleCount += 1;
    });

    if (emptyMsg) emptyMsg.hidden = visibleCount > 0;
    if (countEl) countEl.textContent = visibleCount;
  };

  input.addEventListener("input", applyFilter);
  priceRange?.addEventListener("input", applyFilter);
  onlyAvailable?.addEventListener("change", applyFilter);

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

  clearBtn?.addEventListener("click", () => {
    input.value = "";
    applyFilter();
    input.focus();
  });

  resetBtn?.addEventListener("click", () => {
    input.value = "";
    if (priceRange) priceRange.value = priceRange.max;
    if (onlyAvailable) onlyAvailable.checked = false;
    dateRange = "all";
    category = "all";
    categoryNav?.querySelectorAll(".category-tab").forEach((t) => {
      t.classList.toggle("is-active", t.dataset.category === "all");
    });
    dateChips?.querySelectorAll(".chip").forEach((c) => {
      c.classList.toggle("is-active", c.dataset.range === "all");
    });
    applyFilter();
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
