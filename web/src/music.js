// "Doki Doki" themes, looped and faded in and out (both Pixabay Content License):
//   twist: "Shining Smile Melody - Jpop" by ZTMusic, crashing in on the trailer twist and over the title screen
//   end:   "Pink Candy" by AI-SEVEN-BGM, for walking out of the Spire together
const TRACKS = { twist: "/assets/doki_twist.mp3", end: "/assets/doki_end.mp3" };
let audio, track, fadeTimer;

function fadeTo(target, secs, then) {
  clearInterval(fadeTimer);
  const from = audio.volume;
  const t0 = performance.now();
  fadeTimer = setInterval(() => {
    const k = Math.min(1, (performance.now() - t0) / (secs * 1000));
    audio.volume = from + (target - from) * k;
    if (k >= 1) { clearInterval(fadeTimer); then?.(); }
  }, 30);
}

export function startMusic(vol = 0.35, which = "twist") {
  if (track !== which) {
    audio?.pause();
    clearInterval(fadeTimer);
    track = which;
    audio = new Audio(TRACKS[which]);
    audio.loop = true;
    audio.volume = 0;
  }
  audio.play().catch(() => {
    // No user gesture yet (e.g. ?nointro): start on the first click instead.
    addEventListener("pointerdown", () => startMusic(vol, which), { once: true });
  });
  fadeTo(vol, which === "twist" ? 0.15 : 0.6); // the twist hits hard; the ending eases in
}

export function stopMusic(fade = 0.8) {
  if (!audio || audio.paused) return;
  fadeTo(0, fade, () => audio.pause());
}
