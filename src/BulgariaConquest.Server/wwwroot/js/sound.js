// Small sound effects made with the Web Audio API (no audio files): a clock ticking while the
// answer time runs out and a gong when it is over. Browsers only allow sound after a click or a
// key press, so unlock() is called on the first interaction.

const MUTE_KEY = "bc.muted";
let ctx = null;
let muted = false;
try {
  muted = localStorage.getItem(MUTE_KEY) === "1";
} catch {
  /* ignore */
}

function audio() {
  if (!ctx) {
    const Ctx = window.AudioContext ?? window.webkitAudioContext;
    if (!Ctx) return null;
    ctx = new Ctx();
  }
  if (ctx.state === "suspended") ctx.resume();
  return ctx;
}

export function unlock() {
  try {
    audio();
  } catch {
    /* no audio – the game works without it */
  }
}

export const isMuted = () => muted;

export function setMuted(value) {
  muted = value;
  try {
    localStorage.setItem(MUTE_KEY, value ? "1" : "0");
  } catch {
    /* ignore */
  }
}

/** One short tone with a fast attack and an exponential fade. */
const VOLUME = 1.4; // overall loudness of every effect

function tone(ac, { freq, at = 0, dur, vol, type = "sine", slideTo = null }) {
  vol = Math.min(1, vol * VOLUME);
  const t = ac.currentTime + at;
  const osc = ac.createOscillator();
  const gain = ac.createGain();
  osc.type = type;
  osc.frequency.setValueAtTime(freq, t);
  if (slideTo) osc.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
  gain.gain.setValueAtTime(0.0001, t);
  gain.gain.exponentialRampToValueAtTime(vol, t + 0.005);
  gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  osc.connect(gain).connect(ac.destination);
  osc.start(t);
  osc.stop(t + dur + 0.02);
}

/**
 * Clock tick. "tick" and "tock" alternate; in the last seconds (`urgent`) the tick is
 * higher and doubled, like a heartbeat.
 */
export function tick(urgent, tock) {
  if (muted) return;
  const ac = audio();
  if (!ac) return;
  if (urgent) {
    tone(ac, { freq: 1500, dur: 0.05, vol: 0.22, type: "square" });
    tone(ac, { freq: 1500, at: 0.16, dur: 0.05, vol: 0.16, type: "square" });
  } else {
    tone(ac, { freq: tock ? 820 : 1050, dur: 0.035, vol: 0.14, type: "triangle" });
  }
}

/** Time is up: a low gong. */
export function timeUp() {
  if (muted) return;
  const ac = audio();
  if (!ac) return;
  tone(ac, { freq: 220, dur: 1.1, vol: 0.3, type: "sine", slideTo: 180 });
  tone(ac, { freq: 330, dur: 0.8, vol: 0.12, type: "triangle", slideTo: 270 });
}
