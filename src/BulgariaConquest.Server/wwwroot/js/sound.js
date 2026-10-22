// Sound effects and a little music made with the Web Audio API (no audio files): a clock ticking
// while the answer time runs out, a gong when it is over, swords, fanfares and a tense drum loop
// while a question is open. Browsers only allow sound after a click or a key press, so unlock()
// is called on the first interaction.

const MUTE_KEY = "bc.muted";
const VOLUME = 1.4; // overall loudness of every effect
const MUSIC_VOLUME = 0.55; // the question loop sits under the effects

let ctx = null;
let noise = null; // one second of white noise, shared by swooshes and crashes
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

/** The audio context if sound may play now, else null. */
function live() {
  if (muted) return null;
  try {
    return audio();
  } catch {
    return null;
  }
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
  if (muted) setTension(false);
  try {
    localStorage.setItem(MUTE_KEY, value ? "1" : "0");
  } catch {
    /* ignore */
  }
}

/** One short tone with a fast attack and an exponential fade. */
function tone(ac, { freq, at = 0, dur, vol, type = "sine", slideTo = null, out = ac.destination }) {
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
  osc.connect(gain).connect(out);
  osc.start(t);
  osc.stop(t + dur + 0.02);
}

/** A burst of filtered noise: a swoosh when the filter slides, a crash or thud when it does not. */
function hiss(ac, { at = 0, dur, vol, from, to = from, q = 1, type = "bandpass", out = ac.destination }) {
  if (!noise) {
    noise = ac.createBuffer(1, ac.sampleRate, ac.sampleRate);
    const data = noise.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
  }
  const t = ac.currentTime + at;
  const src = ac.createBufferSource();
  src.buffer = noise;
  const filter = ac.createBiquadFilter();
  filter.type = type;
  filter.Q.value = q;
  filter.frequency.setValueAtTime(from, t);
  filter.frequency.exponentialRampToValueAtTime(to, t + dur);
  const gain = ac.createGain();
  gain.gain.setValueAtTime(0.0001, t);
  gain.gain.exponentialRampToValueAtTime(Math.min(1, vol * VOLUME), t + Math.min(0.03, dur / 4));
  gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  src.connect(filter).connect(gain).connect(out);
  src.start(t);
  src.stop(t + dur + 0.02);
}

/**
 * Clock tick. "tick" and "tock" alternate; in the last seconds (`urgent`) the tick is
 * higher and doubled, like a heartbeat.
 */
export function tick(urgent, tock) {
  const ac = live();
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
  const ac = live();
  if (!ac) return;
  tone(ac, { freq: 220, dur: 1.1, vol: 0.3, type: "sine", slideTo: 180 });
  tone(ac, { freq: 330, dur: 0.8, vol: 0.12, type: "triangle", slideTo: 270 });
}

/** An attack is declared: a sword swooshes through the air and clangs when it lands (with the map animation). */
export function attack() {
  const ac = live();
  if (!ac) return;
  hiss(ac, { dur: 0.9, vol: 0.22, from: 400, to: 3200, q: 2.5 });
  // metal on metal: two inharmonic partials and a bright click
  tone(ac, { freq: 1240, at: 1.0, dur: 0.6, vol: 0.12, type: "triangle", slideTo: 1180 });
  tone(ac, { freq: 1873, at: 1.0, dur: 0.45, vol: 0.08, type: "sine" });
  hiss(ac, { at: 1.0, dur: 0.12, vol: 0.25, from: 6000, type: "highpass" });
  tone(ac, { freq: 90, at: 1.0, dur: 0.3, vol: 0.25, type: "sine", slideTo: 50 });
}

/** A province changes hands: a short brass-like rising fanfare. */
export function capture() {
  const ac = live();
  if (!ac) return;
  [392, 523.25, 659.25, 783.99].forEach((freq, i) => {
    tone(ac, { freq, at: i * 0.09, dur: i === 3 ? 0.7 : 0.18, vol: 0.13, type: "sawtooth" });
    tone(ac, { freq: freq / 2, at: i * 0.09, dur: i === 3 ? 0.7 : 0.18, vol: 0.1, type: "triangle" });
  });
}

/** A castle tower falls: a heavy rumble and crumbling stones. */
export function towerFall() {
  const ac = live();
  if (!ac) return;
  tone(ac, { freq: 70, dur: 1.2, vol: 0.4, type: "sine", slideTo: 35 });
  hiss(ac, { dur: 1.3, vol: 0.35, from: 900, to: 120, type: "lowpass" });
  for (let i = 0; i < 6; i++) hiss(ac, { at: 0.15 + i * 0.13 + Math.random() * 0.05, dur: 0.08, vol: 0.18, from: 1500 + Math.random() * 1500, q: 4 });
}

/** My answer was right: two bright rising notes. */
export function correct() {
  const ac = live();
  if (!ac) return;
  tone(ac, { freq: 880, dur: 0.18, vol: 0.18, type: "triangle" });
  tone(ac, { freq: 1318.5, at: 0.12, dur: 0.45, vol: 0.18, type: "triangle" });
  tone(ac, { freq: 2637, at: 0.12, dur: 0.3, vol: 0.05, type: "sine" });
}

/** My answer was wrong: a low, falling buzz. */
export function wrong() {
  const ac = live();
  if (!ac) return;
  tone(ac, { freq: 196, dur: 0.22, vol: 0.14, type: "sawtooth", slideTo: 180 });
  tone(ac, { freq: 147, at: 0.2, dur: 0.45, vol: 0.14, type: "sawtooth", slideTo: 110 });
}

/** The province held: a solid shield knock. */
export function defended() {
  const ac = live();
  if (!ac) return;
  tone(ac, { freq: 160, dur: 0.25, vol: 0.3, type: "sine", slideTo: 90 });
  hiss(ac, { dur: 0.1, vol: 0.2, from: 2500, q: 1.5 });
  tone(ac, { freq: 523.25, at: 0.12, dur: 0.4, vol: 0.08, type: "triangle" });
}

/** It is my turn: a horn call. */
export function yourTurn() {
  const ac = live();
  if (!ac) return;
  tone(ac, { freq: 392, dur: 0.16, vol: 0.12, type: "sawtooth" });
  tone(ac, { freq: 587.33, at: 0.17, dur: 0.38, vol: 0.12, type: "sawtooth" });
  tone(ac, { freq: 293.66, at: 0.17, dur: 0.38, vol: 0.08, type: "triangle" });
}

/** End of the game: a fanfare for the winner, a falling line for everyone else. */
export function gameOver(won) {
  const ac = live();
  if (!ac) return;
  const notes = won ? [523.25, 659.25, 783.99, 1046.5] : [440, 392, 349.23, 293.66];
  notes.forEach((freq, i) => {
    const last = i === notes.length - 1;
    tone(ac, { freq, at: i * 0.2, dur: last ? 1.2 : 0.22, vol: 0.13, type: won ? "sawtooth" : "triangle" });
    tone(ac, { freq: freq / 2, at: i * 0.2, dur: last ? 1.2 : 0.22, vol: 0.1, type: "triangle" });
  });
}

// ------------------------------------------------------------------ question music

// A minor drum-and-bass loop: 8 steps per bar, a bass note on some steps, a drum on others.
const BASS = [110, 0, 110, 0, 130.81, 0, 98, 103.83]; // A2 … C3 G2 G#2 (0 = rest)
const DRUM = [1, 0, 0, 1, 1, 0, 1, 0];
const STEP = 0.21; // seconds per step (~143 bpm in eighths)
const music = { on: false, timer: 0, next: 0, step: 0, out: null };

/**
 * Low, tense music while a question is open. Idempotent: call it on every tick with whether
 * the music should be playing. Notes are scheduled a little ahead, so timers firing late do not stutter.
 */
export function setTension(on) {
  if (on && muted) on = false;
  if (on === music.on) return;
  music.on = on;
  clearInterval(music.timer);
  if (!on) {
    if (music.out && ctx) {
      // fade out instead of cutting the notes off
      music.out.gain.setTargetAtTime(0.0001, ctx.currentTime, 0.15);
      const out = music.out;
      setTimeout(() => out.disconnect(), 800);
    }
    music.out = null;
    return;
  }

  const ac = live();
  if (!ac) {
    music.on = false;
    return;
  }
  music.out = ac.createGain();
  music.out.gain.value = MUSIC_VOLUME;
  music.out.connect(ac.destination);
  music.next = ac.currentTime + 0.05;
  music.step = 0;
  const schedule = () => {
    // Background tabs run timers only once a second – schedule further ahead there.
    while (music.next < ac.currentTime + (document.hidden ? 1.5 : 0.3)) {
      const at = music.next - ac.currentTime;
      const i = music.step % BASS.length;
      const bar = Math.floor(music.step / BASS.length);
      if (BASS[i]) tone(ac, { freq: BASS[i], at, dur: STEP * 1.6, vol: 0.12, type: "triangle", out: music.out });
      if (DRUM[i]) {
        tone(ac, { freq: 75, at, dur: 0.18, vol: 0.2, type: "sine", slideTo: 45, out: music.out });
        hiss(ac, { at, dur: 0.05, vol: 0.05, from: 2000, q: 0.8, out: music.out });
      }
      // a high string note every other bar keeps the suspense
      if (i === 0 && bar % 2 === 1) tone(ac, { freq: 659.25, at, dur: STEP * 7, vol: 0.035, type: "sine", out: music.out });
      music.next += STEP;
      music.step++;
    }
  };
  schedule();
  music.timer = setInterval(schedule, 100);
}
