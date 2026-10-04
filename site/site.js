// Motion for the landing page: fade-ins, a two-row marquee driven by scrolling, a gently tilting hero picture,
// text that lights up letter by letter and cards that stack as you scroll. Every value eases toward its target in
// one animation loop (exponential smoothing, frame-rate independent), so wheel steps and quick scrolling back and
// forth never jerk. Everything stays still and fully visible with "reduce motion", in background tabs, or without
// JavaScript (the CSS only hides .fade elements once this file, or the inline snippet in <head>, has run).
(() => {
  document.documentElement.classList.add("js");
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const hidden = document.visibilityState === "hidden";
  if (hidden) document.documentElement.classList.add("still");

  // Fade-ins: once, slightly before the element enters the viewport.
  const fades = document.querySelectorAll(".fade");
  if (reduce || hidden || !("IntersectionObserver" in window)) {
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
  const magnet = document.querySelector("[data-magnet]");
  const frame = magnet ? magnet.parentElement : null;

  // A value that eases toward its target; `speed` is how quickly (higher = snappier).
  const smooth = (speed) => ({ value: null, target: 0, speed });
  const state = {
    marquee: smooth(9),
    reveal: smooth(8),
    stack: smooth(10),
    tiltX: smooth(6),
    tiltY: smooth(6),
  };

  let rowWidths = [];
  function measureLayout() {
    rowWidths = rows.map((row) => row.scrollWidth / 3);
  }

  // Targets from the scroll position.
  function readScroll() {
    const view = window.innerHeight;
    if (marquee) {
      state.marquee.target = (view - marquee.getBoundingClientRect().top) * 0.3;
    }
    if (reveal) {
      const box = reveal.getBoundingClientRect();
      state.reveal.target = clamp((view * 0.8 - box.top) / (box.height + view * 0.6), 0, 1);
    }
    if (stack) {
      const box = stack.getBoundingClientRect();
      state.stack.target = clamp(-box.top / Math.max(1, box.height - view), 0, 1);
    }
  }

  function render() {
    if (marquee) {
      const offset = state.marquee.value;
      rows.forEach((row, index) => {
        const x = row.dataset.direction === "right" ? offset - 200 - rowWidths[index] : -(offset - 200);
        row.style.transform = `translate3d(${x.toFixed(1)}px, 0, 0)`;
      });
    }

    if (reveal) {
      const lit = state.reveal.value * letters.length;
      letters.forEach((letter, index) => {
        letter.style.opacity = (0.2 + 0.8 * clamp(lit - index, 0, 1)).toFixed(3);
      });
    }

    if (stack) {
      const progress = state.stack.value;
      cards.forEach((card, index) => {
        const target = 1 - (cards.length - 1 - index) * 0.03;
        const start = index / cards.length;
        const local = clamp((progress - start) / (1 - start), 0, 1);
        card.style.transform = `translateZ(0) scale(${(1 - (1 - target) * local).toFixed(4)})`;
      });
    }

    if (magnet) {
      const x = state.tiltX.value;
      const y = state.tiltY.value;
      magnet.style.transform =
        `translate3d(${(x * 10).toFixed(2)}px, ${(y * 6).toFixed(2)}px, 0) rotateX(${(-y * 3).toFixed(2)}deg) rotateY(${(x * 5).toFixed(2)}deg)`;
    }
  }

  // One loop for everything; it stops by itself when all values have settled.
  let running = false;
  let last = 0;
  function tick(now) {
    const dt = Math.min(0.05, (now - last) / 1000 || 0.016);
    last = now;
    let moving = false;
    for (const item of Object.values(state)) {
      const delta = item.target - item.value;
      if (Math.abs(delta) > 0.0005) {
        item.value += delta * (1 - Math.exp(-item.speed * dt));
        moving = true;
      } else {
        item.value = item.target;
      }
    }
    render();
    if (moving) {
      requestAnimationFrame(tick);
    } else {
      running = false;
    }
  }

  function wake() {
    if (!running) {
      running = true;
      last = performance.now();
      requestAnimationFrame(tick);
    }
  }

  window.addEventListener("scroll", () => {
    readScroll();
    wake();
  }, { passive: true });
  window.addEventListener("resize", () => {
    measureLayout();
    readScroll();
    wake();
  });
  measureLayout();
  readScroll();
  for (const item of Object.values(state)) item.value = item.target; // start where the page is, no sweep on load
  render();

  // Hero picture: tilts a little toward the pointer while it is near, and settles back when it leaves.
  if (magnet && matchMedia("(pointer: fine)").matches) {
    const padding = 150;
    window.addEventListener("pointermove", (event) => {
      const box = frame.getBoundingClientRect();
      const near = event.clientX > box.left - padding && event.clientX < box.right + padding &&
        event.clientY > box.top - padding && event.clientY < box.bottom + padding;
      state.tiltX.target = near ? clamp((event.clientX - (box.left + box.width / 2)) / (box.width / 2 + padding), -1, 1) : 0;
      state.tiltY.target = near ? clamp((event.clientY - (box.top + box.height / 2)) / (box.height / 2 + padding), -1, 1) : 0;
      wake();
    }, { passive: true });
    document.documentElement.addEventListener("pointerleave", () => {
      state.tiltX.target = 0;
      state.tiltY.target = 0;
      wake();
    });
  }
})();
