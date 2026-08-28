const stageContent = {
  build: {
    label: "Build result",
    title: "Immutable artifact created",
    code: "manifest → normalized → immutable"
  },
  validate: {
    label: "Validation result",
    title: "Schema and policy checks passed",
    code: "0 errors · 2 advisories"
  },
  preview: {
    label: "Preview result",
    title: "12 safe changes identified",
    code: "+8 update · +4 create · 0 delete"
  },
  apply: {
    label: "Apply result",
    title: "Production release in progress",
    code: "release 4.2.0 · target applying"
  }
};

const stageButtons = [...document.querySelectorAll("[data-stage]")];
const stageDetail = document.querySelector("[data-stage-detail]");
const productionStatus = document.querySelector('[data-environment="prod"] strong');

function selectStage(button) {
  const selectedIndex = stageButtons.indexOf(button);
  const stage = stageContent[button.dataset.stage];

  stageButtons.forEach((item, index) => {
    const isSelected = item === button;
    item.classList.toggle("is-active", isSelected);
    item.classList.toggle("is-complete", index < selectedIndex);
    item.setAttribute("aria-selected", String(isSelected));
    item.setAttribute("tabindex", isSelected ? "0" : "-1");
    item.querySelector(".rail-node span").textContent = index < selectedIndex ? "✓" : isSelected ? "→" : "";
  });

  productionStatus.textContent = button.dataset.stage === "apply" ? "Applying" : "Queued";
  stageDetail.innerHTML = `
    <div>
      <span class="detail-label">${stage.label}</span>
      <strong>${stage.title}</strong>
    </div>
    <code>${stage.code}</code>
  `;
}

stageButtons.forEach((button) => {
  button.addEventListener("click", () => selectStage(button));
  button.addEventListener("keydown", (event) => {
    const currentIndex = stageButtons.indexOf(button);
    const keyTargets = {
      ArrowRight: (currentIndex + 1) % stageButtons.length,
      ArrowLeft: (currentIndex - 1 + stageButtons.length) % stageButtons.length,
      Home: 0,
      End: stageButtons.length - 1
    };

    if (keyTargets[event.key] === undefined) return;
    event.preventDefault();
    const nextButton = stageButtons[keyTargets[event.key]];
    selectStage(nextButton);
    nextButton.focus();
  });
});

const revealObserver = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (entry.isIntersecting) {
      entry.target.classList.add("is-visible");
      revealObserver.unobserve(entry.target);
    }
  });
}, { threshold: 0.12 });

document.querySelectorAll(".reveal").forEach((element) => revealObserver.observe(element));

const header = document.querySelector("[data-header]");
let lastScrollY = window.scrollY;

window.addEventListener("scroll", () => {
  const currentScrollY = window.scrollY;
  header.classList.toggle("is-scrolled", currentScrollY > 12);
  header.classList.toggle("is-hidden", currentScrollY > lastScrollY && currentScrollY > 260);
  lastScrollY = currentScrollY;
}, { passive: true });

const menuButton = document.querySelector("[data-menu-toggle]");
const menu = document.querySelector("[data-menu]");

menuButton.addEventListener("click", () => {
  const isOpen = menuButton.getAttribute("aria-expanded") === "true";
  menuButton.setAttribute("aria-expanded", String(!isOpen));
  menu.classList.toggle("is-open", !isOpen);
});

document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape" || menuButton.getAttribute("aria-expanded") !== "true") return;
  menuButton.setAttribute("aria-expanded", "false");
  menu.classList.remove("is-open");
  menuButton.focus();
});

menu.querySelectorAll("a").forEach((link) => {
  link.addEventListener("click", () => {
    menuButton.setAttribute("aria-expanded", "false");
    menu.classList.remove("is-open");
  });
});

if (matchMedia("(hover: hover) and (pointer: fine)").matches) {
  document.querySelectorAll(".magnetic").forEach((button) => {
    button.addEventListener("pointermove", (event) => {
      const bounds = button.getBoundingClientRect();
      const x = event.clientX - bounds.left - bounds.width / 2;
      const y = event.clientY - bounds.top - bounds.height / 2;
      button.style.transform = `translate3d(${x * 0.08}px, ${y * 0.08}px, 0)`;
    });

    button.addEventListener("pointerleave", () => {
      button.style.transform = "translate3d(0, 0, 0)";
    });
  });
}
