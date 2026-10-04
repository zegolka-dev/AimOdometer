// Motion for the landing page: fade-ins, a two-row marquee driven by scrolling, a magnetic hero picture,
// text that lights up letter by letter and cards that stack as you scroll. Everything stays still and fully
// visible with "reduce motion" or without JavaScript (the CSS only hides .fade elements once this file runs).
(() => {
  document.documentElement.classList.add("js"); // also set inline in <head>, so nothing flashes
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;

  // Opened in a background tab: nobody sees the entrance, so show everything at once (and nothing waits for
  // frames a hidden tab never paints).
  if (document.visibilityState === "hidden") document.documentElement.classList.add("still");

  // Fade-ins: once, slightly before the element enters the viewport.
  const fades = document.querySelectorAll(".fade");
  if (reduce || document.visibilityState === "hidden" || !("IntersectionObserver" in window)) {
    fades.forEach((element) => element.classList.add("in"));
  } else {
    const observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add("in");
          observer.unobserve(entry.target);
        }
      }
    }, { rootMargin: "50px" });
    fades.forEach((element) => observer.observe(element));
  }

  if (reduce) return;

  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
  const marquee = document.querySelector(".marquee");
  const rows = marquee ? [...marquee.querySelectorAll(".marquee-row")] : [];
  const reveal = document.querySelector(".reveal");
  const letters = reveal ? [...reveal.querySelectorAll(".ch")] : [];
  const stack = document.querySelector(".stack");
  const cards = stack ? [...stack.querySelectorAll(".stack-card")] : [];

  function update() {
    const view = window.innerHeight;

    // Marquee: the rows slide in opposite directions as the page scrolls.
    if (marquee) {
      const top = marquee.getBoundingClientRect().top + window.scrollY;
      const offset = (window.scrollY - top + view) * 0.3;
      for (const row of rows) {
        const x = row.dataset.direction === "right" ? offset - 200 : -(offset - 200);
        row.style.transform = `translate3d(${row.dataset.direction === "right" ? x - row.scrollWidth / 3 : x}px, 0, 0)`;
      }
    }

    // Letters light up from 0.2 to 1 while the paragraph travels from 80% to 20% of the viewport.
    if (reveal) {
      const box = reveal.getBoundingClientRect();
      const progress = clamp((view * 0.8 - box.top) / (box.height + view * 0.6), 0, 1);
      const lit = progress * letters.length;
      letters.forEach((letter, index) => {
        letter.style.opacity = (0.2 + 0.8 * clamp(lit - index, 0, 1)).toFixed(2);
      });
    }

    // Stacking cards: each card shrinks a little once the next ones start covering it.
    if (stack && cards.length) {
      const box = stack.getBoundingClientRect();
      const progress = clamp(-box.top / Math.max(1, box.height - view), 0, 1);
      cards.forEach((card, index) => {
        const target = 1 - (cards.length - 1 - index) * 0.03;
        const start = index / cards.length;
        const local = clamp((progress - start) / (1 - start), 0, 1);
        card.style.transform = `scale(${(1 - (1 - target) * local).toFixed(4)})`;
      });
    }
  }

  let queued = false;
  const schedule = () => {
    if (!queued) {
      queued = true;
      requestAnimationFrame(() => {
        queued = false;
        update();
      });
    }
  };
  window.addEventListener("scroll", schedule, { passive: true });
  window.addEventListener("resize", schedule);
  update();

  // Magnetic hero picture: follows the pointer a little while it is near.
  const magnet = document.querySelector("[data-magnet]");
  if (magnet && matchMedia("(pointer: fine)").matches) {
    const padding = 150;
    const strength = 3;
    let active = false;
    window.addEventListener("pointermove", (event) => {
      const box = magnet.parentElement.getBoundingClientRect(); // the frame does not move, the picture does
      const near = event.clientX > box.left - padding && event.clientX < box.right + padding &&
        event.clientY > box.top - padding && event.clientY < box.bottom + padding;
      if (near) {
        if (!active) {
          active = true;
          magnet.style.transition = "transform 0.3s ease-out";
        }
        const x = (event.clientX - (box.left + box.width / 2)) / strength;
        const y = (event.clientY - (box.top + box.height / 2)) / strength;
        magnet.style.transform = `translate3d(${x}px, ${y}px, 0)`;
      } else if (active) {
        active = false;
        magnet.style.transition = "transform 0.6s ease-in-out";
        magnet.style.transform = "translate3d(0, 0, 0)";
      }
    }, { passive: true });
  }
})();
