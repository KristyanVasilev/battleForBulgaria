// Entry point: wires the map, the server connection and the UI panels together.

import { BulgariaMap } from "./map.js";
import { GameConnection, loadSession, saveSession, friendlyError } from "./connection.js";
import * as ui from "./ui.js";
import * as sound from "./sound.js";

const { $ } = ui;
const NAME_KEY = "bc.name";

const [geometry, provinces, settings, categories] = await Promise.all([
  fetch("data/map-geometry.json").then((r) => r.json()),
  fetch("api/provinces").then((r) => r.json()),
  fetch("api/settings").then((r) => r.json()),
  fetch("api/categories").then((r) => r.json()),
]);

const map = new BulgariaMap($("#map"), geometry, provinces);
const conn = new GameConnection();

let session = loadSession(); // { gameId, playerId, token }
let game = null;
let latest = null; // last view received, even before we know our session
let clockOffset = 0;
let hovered = null;
let gameOverDismissed = null; // game id whose final standings the player closed
let shownIntro = false; // whether the last render showed a question intro (the question itself still hidden)
// What my lifelines revealed on the current question (private – only this browser has it).
const lifelines = { question: null, results: [], restoring: false };

const me = () => session?.playerId ?? null;
const provinceName = (id) => (map.province(id)?.name ?? id).replace(" (град)", "");

// ------------------------------------------------------------------ rendering

function render() {
  syncLifelineResults();
  ui.renderStatus(game, me());
  ui.renderScoreboard(game, me());
  ui.renderRounds(game);
  ui.renderChat(game, me());
  ui.renderAnnouncer(game, me(), provinceName);
  ui.renderQuestion(game, me(), answer, provinceName);
  shownIntro = ui.inIntro(game?.question);
  ui.applyLifelines(game, lifelines.results);
  ui.renderJokers($("#jokers"), game, me(), useLifeline);
  ui.renderJokers($("#question .q-jokers"), game, me(), useLifeline, { compact: true });
  $("#joker-hint").textContent = ui.jokerHint(game, me());
  if (!ui.canPickTheme(game, me()) && !$("#theme-picker").hidden) ui.closeCategoryPicker();
  if (hovered) ui.renderProvinceTip(game, map, hovered);

  const myTurn = !!game?.options?.playerId && game.options.playerId === me();
  map.setState({
    owners: game?.owners,
    players: game?.players,
    targets: myTurn ? game.options.provinces : [],
    targetKind: game?.options?.kind,
    battle: game?.battle,
    turnPlayer: game && game.phase !== "finished" ? ui.activePlayerId(game) : null,
    points: game?.provincePoints,
  });

  $("#landing").hidden = !!game;
  $("#game-screen").hidden = !game;
  ui.renderGameOver(game, me(), game && gameOverDismissed === game.id, {
    newGame: leaveGame,
    dismiss: () => {
      gameOverDismissed = game.id;
      render();
    },
  });
  if (!game) return;

  $("#game-code").textContent = game.id;
  const bots = game.players.filter((p) => p.botLevel);
  $("#game-kind").textContent = bots.length ? "Игра срещу ботове" : "Код на играта";
  const isHost = game.players[0]?.id === me();
  const inLobby = game.phase === "lobby";
  const enough = game.players.length >= settings.minPlayers;
  $("#start-btn").hidden = !(inLobby && isHost);
  $("#start-btn").disabled = !enough;
  $("#lobby-hint").hidden = !inLobby;
  $("#lobby-hint").textContent = enough
    ? isHost ? "Всички са тук – стартирай играта!" : "Изчакваме създателя да стартира играта."
    : `Изпрати кода на приятели. Нужни са ${settings.minPlayers} играчи (${game.players.length}/${settings.maxPlayers}).`;
  $("#new-game-btn").hidden = game.phase !== "finished";
  $("#copy-link-btn").hidden = game.players.some((p) => p.botLevel);
  // The code card is only needed while gathering players and after the end; mid-game the map gets the room.
  $("#game-panel").hidden = !inLobby && game.phase !== "finished";
}

function apply(view) {
  if (game && view.id === game.id && view.version < game.version) return; // stale update
  game = view;
  clockOffset = Date.parse(view.serverNow) - Date.now();
  ui.setClockOffset(clockOffset);
  render();
  questionClock();
}

conn.onUpdate((view) => {
  latest = view;
  if (session?.gameId === view.id) apply(view);
});

conn.onStatus((status) => {
  $("#conn-status").textContent = { reconnecting: "Връзката прекъсна… свързване", closed: "Няма връзка със сървъра" }[status] ?? "";
  if (status === "reconnected" && session) resume(session.gameId, session.token);
});

setInterval(() => {
  // The question appears when its intro is over – no server message marks that moment.
  if (ui.inIntro(game?.question) !== shownIntro) render();
  ui.tickTimers(clockOffset);
  questionClock();
}, 200);

// ------------------------------------------------------------------ sound

const TICK_SECONDS = 8; // the clock starts ticking this many seconds before the answer time runs out
const URGENT_SECONDS = 3;
const clock = { key: null, second: null, gong: false, seenRunning: false };

/**
 * Ticks during the last seconds of a question I still have to answer, and a gong when its time runs out.
 * The gong plays once per question: when the local countdown reaches zero, or – if the server closed the
 * question first (clocks differ a little, timers sleep in background tabs) – when the result arrives with
 * someone still unanswered.
 */
function questionClock() {
  const q = game?.question;
  if (!q) return;

  const key = `${game.id}|${q.startedAt}`;
  if (clock.key !== key) Object.assign(clock, { key, second: null, gong: false, seenRunning: false });
  if (clock.gong) return;

  if (q.revealed) {
    const timedOut = q.participants.some((pid) => !q.answers?.[pid]);
    if (timedOut && clock.seenRunning) gong();
    return;
  }
  if (ui.inIntro(q)) return;
  clock.seenRunning = true;

  const left = ui.secondsLeft(q.deadline, clockOffset);
  if (left <= 0) return gong();

  const mustAnswer = q.participants.includes(me()) && !q.answered.includes(me());
  const second = Math.ceil(left);
  if (mustAnswer && second <= TICK_SECONDS && second !== clock.second) {
    clock.second = second;
    sound.tick(second <= URGENT_SECONDS, second % 2 === 0);
  }
}

function gong() {
  clock.gong = true;
  sound.timeUp();
}

function renderSoundButton() {
  const btn = $("#sound-btn");
  btn.textContent = sound.isMuted() ? "🔇" : "🔊";
  btn.title = sound.isMuted() ? "Включи звука" : "Изключи звука";
  btn.setAttribute("aria-pressed", String(!sound.isMuted()));
}

$("#sound-btn").addEventListener("click", () => {
  sound.setMuted(!sound.isMuted());
  renderSoundButton();
});
renderSoundButton();

// Browsers allow audio only after the player interacts with the page.
for (const type of ["pointerdown", "keydown"]) document.addEventListener(type, sound.unlock, { passive: true });

// ------------------------------------------------------------------ session

function enterGame(result) {
  session = result;
  saveSession(session);
  const url = new URL(location.href);
  url.searchParams.set("game", result.gameId);
  history.replaceState(null, "", url);
  if (latest?.id === result.gameId) apply(latest);
}

function leaveGame() {
  session = null;
  game = null;
  saveSession(null);
  const url = new URL(location.href);
  url.searchParams.delete("game");
  history.replaceState(null, "", url);
  render();
}

async function resume(gameId, token) {
  try {
    enterGame(await conn.invoke("ResumeGame", gameId, token));
  } catch (err) {
    ui.toast(friendlyError(err));
    leaveGame();
  }
}

async function call(method, ...args) {
  try {
    return await conn.invoke(method, ...args);
  } catch (err) {
    ui.toast(friendlyError(err));
  }
}

function playerNameInput() {
  const name = $("#player-name").value.trim();
  if (!name) {
    ui.toast("Въведи име.");
    $("#player-name").focus();
    return null;
  }
  try {
    localStorage.setItem(NAME_KEY, name);
  } catch {
    /* ignore */
  }
  return name;
}

// ------------------------------------------------------------------ actions

function answer(kind, value) {
  if (!session) return;
  if (kind === "choice") call("AnswerChoice", session.gameId, session.token, value);
  else call("AnswerNumeric", session.gameId, session.token, value);
}

// ------------------------------------------------------------------ lifelines

/** Forget results when a new question starts; after a reload, fetch what I already used on this one. */
function syncLifelineResults() {
  const q = game?.question;
  const key = q && !q.revealed ? `${game.id}|${q.startedAt}` : null;
  if (key === lifelines.question) return;
  lifelines.question = key;
  lifelines.results = [];
  const usedByMe = q?.lifelinesUsed?.[me()]?.length;
  if (key && usedByMe && session && !lifelines.restoring) {
    lifelines.restoring = true;
    conn
      .invoke("MyLifelines", session.gameId, session.token)
      .then((results) => {
        if (lifelines.question === key) lifelines.results = results;
        render();
      })
      .catch(() => {})
      .finally(() => (lifelines.restoring = false));
  }
}

async function useLifeline(kind) {
  if (!session) return;
  if (kind === "category") {
    if (!ui.canPickTheme(game, me())) return;
    // The joker is spent only once a theme is picked; it applies to the next attack.
    return ui.showCategoryPicker(categories.choice ?? [], (category) => call("UseCategoryLifeline", session.gameId, session.token, category), render);
  }
  const key = lifelines.question;
  const result = await call("UseLifeline", session.gameId, session.token, kind);
  if (result && lifelines.question === key) {
    lifelines.results.push(result);
    render();
  }
}

// ------------------------------------------------------------------ chat

$("#chat-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const input = $("#chat-input");
  const text = input.value.trim();
  if (!text || !session) return;
  input.value = "";
  try {
    await conn.invoke("SendChat", session.gameId, session.token, text);
  } catch (err) {
    input.value = text; // keep what was typed so it can be sent again
    ui.toast(friendlyError(err));
  }
});

// ------------------------------------------------------------------ map tooltip

map.addEventListener("hover", (e) => {
  hovered = e.detail;
  ui.renderProvinceTip(game, map, hovered);
});

$("#map").addEventListener("pointermove", (e) => {
  const tip = $("#map-tip");
  if (tip.hidden) return;
  const stage = $(".map-stage").getBoundingClientRect();
  const x = Math.min(e.clientX - stage.left + 16, stage.width - tip.offsetWidth - 8);
  const y = Math.max(8, e.clientY - stage.top - tip.offsetHeight - 12);
  tip.style.transform = `translate(${x}px, ${y}px)`;
});

map.addEventListener("select", (e) => {
  const id = e.detail;
  if (!id || !game || !session) return;

  const options = game.options;
  if (options.playerId !== me() || !options.provinces.includes(id)) return;
  const method = { castle: "ChooseCastle", claim: "ClaimProvince", attack: "Attack" }[options.kind];
  if (method) call(method, session.gameId, session.token, id);
});

$("#create-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = playerNameInput();
  if (!name) return;
  try {
    enterGame(await conn.invoke("CreateGame", name));
  } catch (err) {
    ui.toast(friendlyError(err));
  }
});

$("#join-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = playerNameInput();
  const code = $("#join-code").value.trim().toUpperCase();
  if (!name) return;
  if (!code) return ui.toast("Въведи кода на играта.");
  try {
    enterGame(await conn.invoke("JoinGame", code, name));
  } catch (err) {
    ui.toast(friendlyError(err));
  }
});

$("#bot-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = playerNameInput();
  if (!name) return;
  try {
    const level = document.querySelector('input[name="bot-level"]:checked')?.value ?? "normal";
    enterGame(await conn.invoke("CreateBotGame", name, level));
  } catch (err) {
    ui.toast(friendlyError(err));
  }
});

$("#start-btn").addEventListener("click", () => session && call("StartGame", session.gameId, session.token));
$("#new-game-btn").addEventListener("click", leaveGame);

$("#leave-btn").addEventListener("click", () => {
  const running = game && game.phase !== "finished" && game.phase !== "lobby";
  if (running && !confirm("Да напуснеш ли играта? Ще можеш да се върнеш само от този раздел на браузъра.")) return;
  leaveGame();
});

// ------------------------------------------------------------------ landing

function openPlayCard(mode) {
  $("#hero-actions").hidden = true;
  $("#lobby").hidden = false;
  setMode(mode);
  $("#home").scrollIntoView({ behavior: "smooth", block: "start" });
  const name = $("#player-name");
  const target = name.value ? $(`.mode-pane[data-mode="${mode}"] button, .mode-pane[data-mode="${mode}"] input`) : name;
  target?.focus({ preventScroll: true });
}

// Highlight the pill of the section currently in view; "Играй" opens the play card.
const navLinks = [...document.querySelectorAll(".pill-nav a")];
const spy = new IntersectionObserver(
  (entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      navLinks.forEach((a) => {
        const active = a.getAttribute("href") === `#${entry.target.id}`;
        a.classList.toggle("is-active", active);
        // On narrow screens the pill scrolls sideways – keep the active item visible.
        if (active) a.parentElement.scrollTo({ left: a.offsetLeft - 16, behavior: "smooth" });
      });
    }
  },
  { rootMargin: "-45% 0px -50% 0px" }
);
navLinks.forEach((a) => {
  const section = document.querySelector(a.getAttribute("href"));
  if (section) spy.observe(section);
});
$(".nav-play").addEventListener("click", (e) => {
  e.preventDefault();
  openPlayCard("bots");
});

function closePlayCard() {
  $("#lobby").hidden = true;
  $("#hero-actions").hidden = false;
}

function setMode(mode) {
  document.querySelectorAll(".segmented button").forEach((b) => b.setAttribute("aria-selected", String(b.dataset.mode === mode)));
  document.querySelectorAll(".mode-pane").forEach((p) => (p.hidden = p.dataset.mode !== mode));
}

document.querySelectorAll("[data-open]").forEach((b) => b.addEventListener("click", () => openPlayCard(b.dataset.open)));
document.querySelectorAll(".segmented button").forEach((b) => b.addEventListener("click", () => setMode(b.dataset.mode)));
$("#close-play").addEventListener("click", closePlayCard);
document.addEventListener("keydown", (e) => e.key === "Escape" && !$("#lobby").hidden && closePlayCard());

$("#copy-link-btn").addEventListener("click", async () => {
  const url = new URL(location.href);
  url.search = `?game=${game.id}`;
  try {
    await navigator.clipboard.writeText(url.toString());
    ui.toast("Линкът е копиран.", "info");
  } catch {
    ui.toast(url.toString(), "info");
  }
});

// ------------------------------------------------------------------ start

try {
  $("#player-name").value = localStorage.getItem(NAME_KEY) ?? "";
} catch {
  /* ignore */
}

const codeFromUrl = new URLSearchParams(location.search).get("game")?.toUpperCase();
if (codeFromUrl) {
  // Invite link: open the "friends" card with the code filled in.
  $("#join-code").value = codeFromUrl;
  openPlayCard("friends");
}

render();

try {
  await conn.start();
  if (session && (!codeFromUrl || codeFromUrl === session.gameId)) await resume(session.gameId, session.token);
  else if (session) leaveGame();
} catch (err) {
  $("#conn-status").textContent = "Няма връзка със сървъра";
  console.error(err);
}
