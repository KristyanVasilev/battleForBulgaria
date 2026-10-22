// Rendering of the panels around the map. Pure functions of the game view received from the server.

const PHASES = {
  lobby: "Чакалня",
  castleSelection: "Избор на замък",
  distribution: "Разпространение",
  conquest: "Битка",
  finished: "Край на играта",
};

const BOT_LEVELS = { easy: "лесен", normal: "нормален", hard: "труден" };

export const $ = (sel) => document.querySelector(sel);

export function h(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === "class") node.className = v;
    else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
    else if (k === "style") node.style.cssText = v;
    else node.setAttribute(k, v === true ? "" : v);
  }
  node.append(...children.flat().filter((c) => c != null && c !== false));
  return node;
}

let toastTimer;
export function toast(message, kind = "error") {
  const box = $("#toast");
  box.textContent = message;
  box.dataset.kind = kind;
  box.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (box.hidden = true), 3500);
}

/** Seconds left until an ISO deadline, corrected for the server/client clock difference. */
export function secondsLeft(deadline, clockOffsetMs) {
  if (!deadline) return null;
  return Math.max(0, (Date.parse(deadline) - (Date.now() + clockOffsetMs)) / 1000);
}

export function playerName(game, id) {
  return game.players.find((p) => p.id === id)?.name ?? "?";
}

function towers(p) {
  return h(
    "span",
    { class: "towers", title: `Кули: ${p.towers} от ${p.maxTowers}` },
    Array.from({ length: p.maxTowers }, (_, i) => h("span", { class: i < p.towers && !p.isEliminated ? "tower" : "tower lost" }, "♜"))
  );
}

let clockOffset = 0;

/** Server minus local clock, so intros and timers line up with the server. */
export function setClockOffset(ms) {
  clockOffset = ms;
}

/** True while a question is announced but not shown yet (the pause before its timer starts). */
export function inIntro(q) {
  return !!q && !q.revealed && secondsLeft(q.startedAt, clockOffset) > 0;
}

/** The player the game is waiting for – during a battle, the attacker. */
export function activePlayerId(game) {
  return game?.currentPlayerId ?? game?.battle?.attackerId ?? null;
}

const TURN_HINTS = {
  castleSelection: "Избери област за замъка си",
  distribution: "Заеми свободна област до своите",
  conquest: "Нападни съседна вражеска област",
};

/**
 * Top bar pill: whose turn it is, in that player's colour, with a pulsing crest.
 * It pops whenever the turn passes to someone else.
 */
export function renderStatus(game, me) {
  const status = $("#status");
  if (!game) {
    status.dataset.key = "";
    status.className = "status-pill";
    status.replaceChildren(h("strong", {}, "Създай игра или влез с код, за да започнеш."));
    return;
  }

  const current = game.currentPlayerId;
  const yourTurn = current && current === me;
  const active = activePlayerId(game);
  const player = game.players.find((p) => p.id === active);
  const q = game.question;
  const battle = game.battle;
  let text;
  if (game.phase === "lobby") text = `Чакаме играчи (${game.players.length}/3)…`;
  else if (game.phase === "finished") text = game.winnerId ? `Победител: ${playerName(game, game.winnerId)}!` : "Играта приключи.";
  else if (q && battle && q.purpose !== "distribution") {
    text = `⚔ ${playerName(game, battle.attackerId)} срещу ${playerName(game, battle.defenderId)}${q.revealed ? " – резултат" : ""}`;
  } else if (q) text = inIntro(q) ? "Подгответе се – идва въпрос…" : q.revealed ? "Резултат от въпроса" : "Въпрос за всички! Отговорете бързо.";
  else if (yourTurn) {
    text = {
      castleSelection: "Твой ход: избери област за замъка си.",
      distribution: `Твой ход: заеми ${game.picks[0]?.remaining ?? 1} свободна(и) област(и).`,
      conquest: game.isLastRound ? "Твой ход: последна атака – нападни която и да е вражеска област!" : "Твой ход: нападни съседна вражеска област.",
    }[game.phase];
  } else if (current) text = `Ход на ${playerName(game, current)}…`;
  else text = "";

  const running = game.phase !== "lobby" && game.phase !== "finished";
  const deadline = game.actionDeadline && !q ? game.actionDeadline : null;
  const key = `${text}|${active}|${deadline}|${running}`;
  if (status.dataset.key === key) return;

  const turnKey = `${game.id}|${game.phase}|${active}`;
  const passed = status.dataset.turn !== turnKey;
  status.dataset.turn = turnKey;
  status.dataset.key = key;
  status.className = ["status-pill", running && player && "has-turn", running && active === me && "is-mine"].filter(Boolean).join(" ");
  status.style.setProperty("--player-color", player?.color ?? "var(--amber)");
  if (passed && running && player) {
    void status.offsetWidth; // restart the pop animation
    status.classList.add("is-passing");
  }

  status.replaceChildren(
    ...[
      running && player ? h("span", { class: "turn-crest", "aria-hidden": "true" }, "♜") : null,
      h("strong", { class: yourTurn ? "your-turn" : "" }, text),
      deadline ? h("span", { class: "timer", "data-deadline": deadline }, "") : null,
    ].filter(Boolean)
  );
}

const announce = { key: null, turnKey: null, timer: 0 };

/**
 * Big banner in the middle of the map. During a question's intro it says what is coming
 * ("X напада Пловдив"); when the turn passes to another player it flashes "Ход на …" for a moment.
 */
export function renderAnnouncer(game, me, provinceName) {
  const box = $("#announce");
  const q = game?.question;
  let next = null;

  if (game && inIntro(q)) {
    const battle = game.battle;
    if (q.purpose === "battle" && battle) {
      next = {
        key: `q|${q.startedAt}`,
        color: game.players.find((p) => p.id === battle.attackerId)?.color,
        icon: "⚔",
        title: `${playerName(game, battle.attackerId)} напада ${provinceName(battle.provinceId)}`,
        sub: battle.isCastle ? `Обсада на замъка на ${playerName(game, battle.defenderId)}!` : `Защитава ${playerName(game, battle.defenderId)}`,
      };
    } else if (q.purpose === "battleTiebreak") {
      next = { key: `q|${q.startedAt}`, icon: "⚖", title: "Равенство!", sub: "И двамата са прави – решава числов въпрос" };
    } else {
      next = { key: `q|${q.startedAt}`, icon: "📜", title: "Въпрос за земите", sub: "Най-точният отговор избира пръв" };
    }
  } else if (game && !q && game.currentPlayerId && game.phase !== "finished" && game.phase !== "lobby") {
    // In the battles every turn has its own deadline, so a second attack after a fallen tower is announced too.
    const turnKey = `${game.id}|${game.phase}|${game.currentPlayerId}|${game.phase === "conquest" ? game.actionDeadline : ""}`;
    if (turnKey !== announce.turnKey) {
      announce.turnKey = turnKey;
      const mine = game.currentPlayerId === me;
      next = {
        key: `t|${turnKey}`,
        flash: true,
        color: game.players.find((p) => p.id === game.currentPlayerId)?.color,
        icon: mine ? "♛" : "♜",
        title: mine ? "Твой ход!" : `Ход на ${playerName(game, game.currentPlayerId)}`,
        sub: game.isLastRound
          ? mine ? "Последна атака: избери която и да е вражеска област" : "Последен рунд – атака навсякъде по картата"
          : mine ? TURN_HINTS[game.phase] : game.phase === "conquest" ? `Рунд ${game.round} от ${game.maxRounds}` : "",
        mine,
      };
    }
  }

  if (!game) announce.turnKey = null;
  if (next && next.key === announce.key) return;
  if (!next) {
    // Intros stay up until the question appears; flashes hide themselves.
    if (announce.key?.startsWith("q|") || (!game && announce.key)) hideAnnouncer(box);
    return;
  }

  announce.key = next.key;
  clearTimeout(announce.timer);
  box.style.setProperty("--player-color", next.color ?? "var(--amber)");
  box.className = ["announce", next.flash ? "is-flash" : "is-intro", next.mine && "is-mine"].filter(Boolean).join(" ");
  box.replaceChildren(
    h(
      "div",
      { class: "announce-card" },
      h("span", { class: "announce-icon", "aria-hidden": "true" }, next.icon),
      h("div", { class: "announce-text" }, h("strong", { class: "announce-title" }, next.title), next.sub ? h("span", { class: "announce-sub" }, next.sub) : null)
    )
  );
  box.hidden = false;
  if (next.flash) announce.timer = setTimeout(() => hideAnnouncer($("#announce")), 1900);
}

function hideAnnouncer(box) {
  announce.key = null;
  clearTimeout(announce.timer);
  box.classList.add("is-leaving");
  announce.timer = setTimeout(() => (box.hidden = true), 300);
}

/**
 * Bottom-right panel: the phase, one lamp per conquest round (the current one lit) with a tick per
 * player in that round's attack order, and below it the order of the current step as a list.
 */
export function renderRounds(game) {
  const title = $("#phase-title");
  const rounds = $("#rounds");
  const list = $("#turn-order");
  if (!game) {
    rounds.dataset.key = "";
    list.replaceChildren();
    return rounds.replaceChildren();
  }

  const finished = game.phase === "finished";
  const current = game.phase === "conquest" ? Math.min(game.round, game.maxRounds) : finished ? game.maxRounds + 1 : 0;
  const active = activePlayerId(game);
  const orders = game.roundOrders ?? [];
  const picks = game.picks.map((p) => p.playerId + p.remaining).join();
  const key = `${game.id}|${game.phase}|${current}|${active}|${orders.length}|${picks}|${game.players.map((p) => +p.isEliminated).join("")}`;
  if (rounds.dataset.key === key) return;
  rounds.dataset.key = key;

  title.replaceChildren(
    ...[
      h("span", {}, PHASES[game.phase] ?? game.phase),
      current >= 1 && current <= game.maxRounds ? h("span", { class: "round-count" }, `Рунд ${current}/${game.maxRounds}`) : null,
    ].filter(Boolean)
  );

  const players = new Map(game.players.map((p) => [p.id, p]));
  rounds.replaceChildren(
    ...Array.from({ length: game.maxRounds }, (_, i) => {
      const n = i + 1;
      const state = n < current ? "is-past" : n === current ? "is-current" : "";
      const order = orders[i];
      const turn = n === current && order ? order.indexOf(active) : -1;
      return h(
        "div",
        { class: `round ${state}`, title: order ? `Рунд ${n}: ${order.map((id) => players.get(id)?.name).join(" → ")}` : `Рунд ${n} от ${game.maxRounds}` },
        h("span", { class: "round-no" }, n),
        h("span", { class: "round-lamp" }),
        h(
          "span",
          { class: "round-turns" },
          order
            ? order.map((id, j) =>
                h("i", {
                  style: `--player-color:${players.get(id)?.color}`,
                  class: [players.get(id)?.isEliminated && "is-out", (n < current || (n === current && j < turn)) && "is-done", j === turn && "is-now"].filter(Boolean).join(" "),
                })
              )
            : game.players.filter((p) => !p.isEliminated).map(() => h("i", { class: "is-unknown" }))
        )
      );
    })
  );
  rounds.setAttribute("aria-label", current >= 1 && current <= game.maxRounds ? `Рунд ${current} от ${game.maxRounds}` : `${game.maxRounds} рунда битки`);

  // Who plays when in the current step.
  let caption = null;
  let rows = [];
  if (game.phase === "conquest" && orders[current - 1]) {
    const order = orders[current - 1];
    const turn = order.indexOf(active);
    caption = "Ред на атаките в този рунд";
    rows = order.map((id, j) => ({ id, state: players.get(id)?.isEliminated ? "out" : j < turn ? "done" : j === turn ? "now" : "next" }));
  } else if (game.phase === "castleSelection") {
    const turn = game.turnOrder.indexOf(active);
    caption = "Ред за избор на замък";
    rows = game.turnOrder.map((id, j) => ({ id, state: j < turn ? "done" : j === turn ? "now" : "next" }));
  } else if (game.phase === "distribution" && game.picks.length && !game.question) {
    caption = "Избират земи";
    rows = game.picks.map((p, j) => ({ id: p.playerId, state: j === 0 ? "now" : "next", extra: `×${p.remaining}` }));
  }
  const MARK = { done: "✓", now: "◀", next: "", out: "✕" };
  list.replaceChildren(
    ...(caption ? [h("li", { class: "turn-caption" }, caption)] : []),
    ...rows.map((r, j) =>
      h(
        "li",
        { class: `turn-row is-${r.state}`, style: `--player-color:${players.get(r.id)?.color}` },
        h("span", { class: "turn-no" }, j + 1),
        h("span", { class: "turn-name" }, players.get(r.id)?.name ?? "?"),
        h("span", { class: "turn-mark" }, r.extra ?? MARK[r.state])
      )
    )
  );
}

// Podium steps are kept between renders (keyed by player id) so a running "+200" animation is not restarted.
const plates = new Map(); // player id → { el, score }
let platesGameId = null;

/** Grid column of the n-th placed player: 1st in the middle, 2nd on the left, 3rd on the right. */
const PODIUM_COLUMNS = [2, 1, 3];

/**
 * Standings as a podium at the bottom of the map. Each step shows the name in the player's colour,
 * the place, towers and points; tied players share the place and the step height.
 */
export function renderScoreboard(game, me) {
  const board = $("#scoreboard");
  if (!game || game.id !== platesGameId) {
    plates.clear();
    board.replaceChildren();
    platesGameId = game?.id ?? null;
    if (!game) return;
  }

  const standings = [...game.players].sort((a, b) => a.isEliminated - b.isEliminated || b.score - a.score);
  const places = standings.map((p, i) => {
    const prev = standings[i - 1];
    return i && prev.score === p.score && prev.isEliminated === p.isEliminated ? null : i + 1;
  });
  places.forEach((place, i) => (places[i] = place ?? places[i - 1]));

  // Players kicked from the lobby lose their step.
  const present = new Set(standings.map((p) => p.id));
  for (const [id, plate] of plates) {
    if (present.has(id)) continue;
    plate.el.remove();
    plates.delete(id);
  }

  // FLIP: remember where each step was, so a change of places slides instead of jumping.
  const before = new Map([...plates].map(([id, plate]) => [id, plate.el.getBoundingClientRect().left]));

  standings.forEach((p, i) => {
    let plate = plates.get(p.id);
    if (!plate) {
      const el = h(
        "div",
        { class: "plate", style: `--player-color:${p.color}` },
        h("div", { class: "plate-name" }, h("span", { class: "plate-place" }), h("span", { class: "plate-label" })),
        h("div", { class: "plate-body" }, h("span", { class: "plate-score" }), h("span", { class: "plate-towers" }))
      );
      board.append(el);
      plate = { el, score: p.score };
      plates.set(p.id, plate);
    }

    const { el } = plate;
    const place = p.isEliminated ? null : places[i];
    const flash = plate.flashUntil > Date.now() ? plate.flash : null;
    el.className = ["plate", `place-${place ?? "out"}`, p.id === activePlayerId(game) && "is-current", p.id === me && "is-me", p.isEliminated && "is-out", p.id === game.winnerId && "is-winner", flash]
      .filter(Boolean)
      .join(" ");
    el.style.gridColumn = PODIUM_COLUMNS[i];
    el.title = `${place ? place + " място – " : ""}${p.name}: ${p.score} точки, ${p.provinceCount} обл.`;
    el.querySelector(".plate-label").textContent = `${p.id === game.winnerId ? "👑 " : ""}${p.name}`;
    el.querySelector(".plate-place").textContent = place ?? "✕";
    el.querySelector(".plate-towers").replaceChildren(p.castleProvinceId ? towers(p) : `${p.provinceCount} обл.`);
    el.querySelector(".plate-score").textContent = `${p.score}`;

    const delta = p.score - plate.score;
    plate.score = p.score;
    if (delta) {
      const badge = h("span", { class: delta > 0 ? "plate-delta up" : "plate-delta down" }, `${delta > 0 ? "+" : ""}${delta}`);
      badge.addEventListener("animationend", () => badge.remove());
      setTimeout(() => badge.remove(), 2000); // animations pause in hidden tabs – never leave a stale badge
      el.querySelectorAll(".plate-delta").forEach((old) => old.remove());
      el.append(badge);
      plate.flash = delta > 0 ? "gained" : "lost";
      plate.flashUntil = Date.now() + 900;
      el.classList.add(plate.flash);
      setTimeout(() => el.classList.remove("gained", "lost"), 900);
    }
  });

  for (const [id, left] of before) {
    const el = plates.get(id).el;
    const dx = left - el.getBoundingClientRect().left;
    if (Math.abs(dx) > 1) el.animate([{ transform: `translateX(${dx}px)` }, { transform: "none" }], { duration: 450, easing: "ease-out" });
  }

  // Empty steps while the lobby is filling up.
  const missing = Math.max(0, 3 - standings.length);
  board.querySelectorAll(".plate-empty").forEach((e) => e.remove());
  for (let i = 0; i < missing; i++) {
    board.append(h("div", { class: "plate plate-empty", style: `grid-column:${PODIUM_COLUMNS[standings.length + i]}` }, h("div", { class: "plate-name" }, "Чакаме играч…"), h("div", { class: "plate-body" })));
  }
}

// ------------------------------------------------------------------ chat (with game events)

/**
 * Chat messages and game events in one timeline. Events are shown as small system lines,
 * so nobody misses what happened while chatting.
 */
export function renderChat(game, me) {
  const list = $("#chat");
  if (!game) {
    list.dataset.key = "";
    return list.replaceChildren();
  }

  const key = `${game.id}|${game.chat.length}|${game.chat.at(-1)?.at}|${game.log.at(-1)?.at}`;
  if (list.dataset.key === key) return;
  list.dataset.key = key;

  const nearBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 40;
  const players = new Map(game.players.map((p) => [p.id, p]));
  const items = [
    ...game.log.map((e) => ({ at: e.at, node: h("li", { class: "chat-event" }, e.text) })),
    ...game.chat.map((m) => {
      const p = players.get(m.playerId);
      return {
        at: m.at,
        node: h(
          "li",
          { class: m.playerId === me ? "chat-msg is-me" : "chat-msg", style: `--player-color:${p?.color ?? "#999"}` },
          h("span", { class: "chat-name" }, p?.name ?? "?"),
          h("span", { class: "chat-text" }, m.text)
        ),
      };
    }),
  ].sort((a, b) => Date.parse(a.at) - Date.parse(b.at));

  list.replaceChildren(...items.slice(-80).map((i) => i.node));
  if (nearBottom || list.dataset.first !== game.id) list.scrollTop = list.scrollHeight;
  list.dataset.first = game.id;
}

// ------------------------------------------------------------------ jokers

export const JOKERS = [
  { kind: "fiftyFifty", label: "50:50", name: "50:50", only: "choice", tip: "Маха два грешни отговора" },
  { kind: "range", label: "📜", name: "Летописец", only: "numeric", tip: "Дава интервал, в който е верният отговор" },
  { kind: "friend", label: "📞", name: "Приятел", only: null, tip: "Приятел предлага отговор – обикновено е прав" },
  { kind: "category", label: "🎯", name: "Тема", only: "attack", tip: "Преди атака избираш темата на въпроса" },
];

const DIFFICULTY = { easy: "Лесен", medium: "Среден", hard: "Труден" };

/**
 * Millionaire-style joker buttons. Rendered both in the sidebar and inside the question card
 * (the card copy is what phones see). `use(kind)` spends one.
 */
export function renderJokers(container, game, me, use, { compact = false } = {}) {
  if (!container) return;
  const player = game?.players.find((p) => p.id === me);
  const q = game?.question;
  const canAsk = !!q && !q.revealed && !inIntro(q) && q.participants.includes(me) && !q.answered.includes(me);

  container.replaceChildren(
    ...JOKERS.map((j) => {
      const left = player?.lifelinesLeft?.includes(j.kind) ?? false;
      const fits = !j.only || j.only === q?.kind;
      const usable = j.only === "attack" ? left && canPickTheme(game, me) : left && canAsk && fits;
      return h(
        "button",
        {
          type: "button",
          class: ["joker", !left && "is-used", usable && "is-ready"].filter(Boolean).join(" "),
          disabled: !usable,
          title: `${j.name}: ${j.tip}${j.only === "choice" ? " (въпроси с А/Б/В/Г)" : j.only === "numeric" ? " (числови въпроси)" : j.only === "attack" ? " (в битките, на твой ход)" : ""}${left ? "" : " – използван"}`,
          onclick: (e) => {
            e.currentTarget.disabled = true;
            use(j.kind);
          },
        },
        h("span", { class: "joker-face" }, j.label),
        compact ? null : h("span", { class: "joker-name" }, j.name)
      );
    })
  );
}

/** The "Тема" joker is picked on your own turn in the conquest phase, before you choose whom to attack. */
export function canPickTheme(game, me) {
  return game?.phase === "conquest" && game.currentPlayerId === me && game.options?.kind === "attack" && !game.question && !game.battle;
}

export function jokerHint(game, me) {
  const player = game?.players.find((p) => p.id === me);
  if (!player) return "";
  if (player.themeForNextAttack) return `🎯 Следващата ти атака е с въпрос от „${player.themeForNextAttack}“.`;
  if (!player.lifelinesLeft.length) return "Използва всичките си жокери.";
  if (canPickTheme(game, me) && player.lifelinesLeft.includes("category")) return "Твой ред е: преди да нападнеш, можеш да избереш темата на въпроса с 🎯.";
  const q = game.question;
  if (!q || q.revealed) return "Всеки жокер се ползва веднъж. „Тема“ – в битките, преди атака; другите – по време на въпрос.";
  if (!q.participants.includes(me)) return "Този въпрос не е за теб.";
  if (q.answered.includes(me)) return "Вече отговори на този въпрос.";
  return q.kind === "choice" ? "Въпрос с избор: 50:50 и Приятел са на твое разположение." : "Числов въпрос: Летописец и Приятел са на твое разположение.";
}

const withUnit = (value, unit) => `${value}${unit ? " " + unit : ""}`;

/** Shows what the player's lifelines revealed, inside the question card. Safe to call on every render. */
export function applyLifelines(game, results) {
  const card = document.querySelector("#question .question-card");
  const q = game?.question;
  if (!card || !q) return;

  const removed = new Set(results.flatMap((r) => r.removedOptions ?? []));
  card.querySelectorAll(".option").forEach((btn, i) => {
    const gone = removed.has(i) && !q.revealed;
    btn.classList.toggle("is-removed", gone);
    if (gone) btn.disabled = true;
  });

  const hints = card.querySelector(".q-hints");
  if (!hints) return;
  hints.replaceChildren(
    ...results
      .filter((r) => r.kind === "range" || r.kind === "friend")
      .map((r) => {
        if (r.kind === "range") {
          return h("div", { class: "q-hint" }, h("span", { class: "q-hint-icon" }, "📜"), h("span", {}, "Летописецът: между ", h("strong", {}, `${r.rangeLow}`), " и ", h("strong", {}, withUnit(r.rangeHigh, q.unit)), q.unit?.endsWith(".") ? "" : "."));
        }
        const guess = r.friendOption != null ? `„${"АБВГ"[r.friendOption]} – ${q.options[r.friendOption]}“` : `около ${withUnit(r.friendNumber, q.unit)}`;
        return h("div", { class: "q-hint" }, h("span", { class: "q-hint-icon" }, "📞"), h("span", {}, `Приятелят: „Мисля, че е ${guess}${guess.endsWith(".") ? "" : "."} Сигурен съм на ${r.friendConfidence}%.“`));
      })
  );
}

/**
 * Theme picker for the "Тема" joker, shown in the jokers panel. `pick(category)` spends the joker;
 * `cancel()` closes the picker without using it.
 */
export function showCategoryPicker(categories, pick, cancel) {
  const box = $("#theme-picker");
  box.replaceChildren(
    h("span", { class: "q-picker-title" }, "🎯 Тема на въпроса при следващата ти атака:"),
    h(
      "div",
      { class: "q-picker-options" },
      categories.map((c) => h("button", { type: "button", class: "q-theme", onclick: () => (closeCategoryPicker(), pick(c)) }, c))
    ),
    h("button", { type: "button", class: "q-picker-cancel", onclick: () => (closeCategoryPicker(), cancel()) }, "Отказ")
  );
  box.hidden = false;
}

export function closeCategoryPicker() {
  const box = $("#theme-picker");
  box.hidden = true;
  box.replaceChildren();
}

// ------------------------------------------------------------------ map tooltip

export function renderProvinceTip(game, map, id) {
  const tip = $("#map-tip");
  if (!id) {
    tip.hidden = true;
    return;
  }
  const province = map.province(id);
  const owner = game?.players.find((p) => p.id === game?.owners?.[id]);
  const builder = game?.players.find((p) => p.castleProvinceId === id);
  const points = game?.provincePoints?.[id];
  tip.replaceChildren(
    h("strong", { class: "tip-name" }, province?.name ?? id),
    points ? h("span", { class: "tip-points" }, `${points} т.`) : null,
    h(
      "div",
      { class: "tip-row" },
      owner ? h("span", { class: "chip", style: `--player-color:${owner.color}` }, h("span", { class: "dot" }), owner.name) : h("span", { class: "chip" }, "Свободна земя"),
      builder && !builder.isEliminated ? h("span", { class: "chip castle-chip" }, "♜ Замък") : null,
      builder?.isEliminated ? h("span", { class: "chip castle-chip" }, "Стара столица") : null
    )
  );
  tip.hidden = false;
}

let questionLeaving = 0; // timer of the fade-out after a question ends

/**
 * Question overlay. `answer(kind, value)` is called when the local player answers.
 * Hidden during the intro pause; fades out when the question is over. After the reveal every
 * player's pick is shown in that player's colour.
 */
export function renderQuestion(game, me, answer, provinceName) {
  const box = $("#question");
  const q = game?.question;
  if (!q || inIntro(q)) {
    box.dataset.key = "";
    if (box.hidden || questionLeaving) return;
    box.classList.add("is-leaving");
    questionLeaving = setTimeout(() => {
      questionLeaving = 0;
      box.hidden = true;
      box.classList.remove("is-leaving");
      box.replaceChildren();
    }, 320);
    return;
  }
  clearTimeout(questionLeaving);
  questionLeaving = 0;
  box.classList.remove("is-leaving");
  box.hidden = false;

  // Rebuild only when the question or its reveal state changes, so a half-typed answer survives
  // other players answering; the "answered X/Y" line is updated in place instead.
  const key = `${q.startedAt}|${q.revealed}`;
  if (box.dataset.key === key) {
    const footer = box.querySelector(".q-footer");
    if (footer) footer.textContent = questionFooter(game, q, me);
    box.querySelector(".q-who")?.replaceWith(whoAnswered(game, q));
    return;
  }
  box.dataset.key = key;

  const isParticipant = q.participants.includes(me);
  const hasAnswered = q.answered.includes(me);
  const color = (pid) => game.players.find((p) => p.id === pid)?.color ?? "#999";
  const battle = game.battle;
  const title = {
    distribution: "Разпределение на земите",
    battle: battle ? `Битка за ${provinceName(battle.provinceId)}: ${playerName(game, battle.attackerId)} срещу ${playerName(game, battle.defenderId)}` : "Битка",
    battleTiebreak: "Равенство! Решава числов въпрос",
  }[q.purpose];

  const body = [];
  if (q.kind === "choice") {
    body.push(
      h(
        "div",
        { class: "options" },
        q.options.map((opt, i) => {
          const pickedBy = q.revealed ? q.participants.filter((pid) => q.answers?.[pid]?.choice === i) : [];
          // One colour band per player who picked this answer.
          const bands = pickedBy.map((pid, j) => `${color(pid)} ${(j * 100) / pickedBy.length}% ${((j + 1) * 100) / pickedBy.length}%`).join(", ");
          return h(
            "button",
            {
              class: ["option", q.revealed && i === q.correctIndex && "is-correct", q.revealed && i !== q.correctIndex && pickedBy.length && "is-wrong", pickedBy.length && "is-picked"]
                .filter(Boolean)
                .join(" "),
              style: pickedBy.length ? `--pick-color:${color(pickedBy[0])}; --pick-bands:linear-gradient(180deg, ${bands})` : null,
              disabled: !isParticipant || hasAnswered || q.revealed,
              onclick: (e) => {
                // Lock all options at once so a double click cannot send two answers.
                e.currentTarget.parentElement.querySelectorAll("button").forEach((b) => (b.disabled = true));
                e.currentTarget.classList.add("is-mine");
                answer("choice", i);
              },
            },
            h("span", { class: "letter" }, q.revealed ? (i === q.correctIndex ? "✓" : pickedBy.length ? "✗" : "АБВГ"[i]) : "АБВГ"[i]),
            h("span", { class: "option-text" }, opt),
            pickedBy.length
              ? h(
                  "span",
                  { class: "picked" },
                  pickedBy.map((pid) => h("span", { class: "pick-chip", style: `--player-color:${color(pid)}` }, `${"АБВГ"[i]} · ${playerName(game, pid)}${pid === me ? " (ти)" : ""}`))
                )
              : null
          );
        })
      )
    );
  } else if (!q.revealed) {
    const locked = !isParticipant || hasAnswered;
    // Whole numbers only: digits and a leading minus; "." and "," cannot even be typed.
    const input = h("input", {
      type: "text",
      inputmode: "numeric",
      autocomplete: "off",
      placeholder: "Цяло число",
      disabled: locked,
      onkeydown: (e) => [".", ",", "e", "E", "+"].includes(e.key) && e.preventDefault(),
      oninput: (e) => {
        const clean = e.target.value.replace(/[^\d-]/g, "").replace(/(?!^)-/g, "");
        if (clean !== e.target.value) e.target.value = clean;
      },
    });
    const button = h("button", { class: "btn-cta", disabled: locked }, "Отговори ", h("span", { class: "cta-arrow" }, "›"));
    const submit = () => {
      const value = Number(input.value);
      if (input.disabled) return;
      if (!/^-?\d+$/.test(input.value.trim()) || !Number.isSafeInteger(value)) return toast("Въведи цяло число.");
      input.disabled = button.disabled = true;
      answer("numeric", value);
    };
    body.push(
      h(
        "form",
        { class: "numeric", onsubmit: (e) => (e.preventDefault(), submit()) },
        input,
        q.unit ? h("span", { class: "unit" }, q.unit) : null,
        button
      )
    );
    setTimeout(() => isParticipant && !hasAnswered && input.focus(), 0);
  } else {
    body.push(
      h("p", { class: "correct-number" }, "Верен отговор: ", h("strong", {}, `${q.correctNumber}${q.unit ? " " + q.unit : ""}`)),
      h(
        "ol",
        { class: "ranking" },
        q.ranking.map((pid) => {
          const a = q.answers?.[pid];
          return h(
            "li",
            { class: "is-player", style: `--player-color:${color(pid)}` },
            h("strong", { class: "grow" }, playerName(game, pid), pid === me ? h("span", { class: "muted small" }, " · ти") : null),
            h("span", { class: "rank-answer" }, a ? `${a.number}${q.unit ? " " + q.unit : ""}` : "без отговор"),
            a ? h("span", { class: "muted small" }, `${(a.responseMs / 1000).toFixed(1)} с`) : null
          );
        })
      )
    );
  }

  const revealBar = q.revealed && q.revealUntil && q.revealedAt;
  box.replaceChildren(
    h(
      "div",
      { class: `question-card${q.revealed ? " is-revealed" : ""}` },
      h(
        "header",
        {},
        h("span", { class: "q-title" }, title),
        h("span", { class: "q-tags" }, q.category ? h("span", { class: "chip" }, q.category) : null, q.difficulty ? h("span", { class: `chip difficulty is-${q.difficulty}` }, DIFFICULTY[q.difficulty]) : null)
      ),
      q.revealed ? null : h("div", { class: "q-timer" }, h("div", { class: "bar", "data-deadline": q.deadline, "data-start": q.startedAt })),
      h("h2", {}, q.text),
      h("div", { class: "q-hints" }),
      ...body,
      q.revealed ? null : whoAnswered(game, q),
      q.revealed || !q.participants.includes(me) ? null : h("div", { class: "q-jokers" }),
      h("p", { class: "muted small q-footer" }, questionFooter(game, q, me)),
      revealBar
        ? h("div", { class: "q-timer is-next", title: "Продължаваме след малко" }, h("div", { class: "bar", "data-deadline": q.revealUntil, "data-start": q.revealedAt }))
        : null
    )
  );
}

/** A chip per participant: filled in their colour once they have answered (not what they answered). */
function whoAnswered(game, q) {
  return h(
    "div",
    { class: "q-who" },
    q.participants.map((pid) =>
      h(
        "span",
        { class: q.answered.includes(pid) ? "who-chip is-done" : "who-chip", style: `--player-color:${game.players.find((p) => p.id === pid)?.color ?? "#999"}` },
        h("span", { class: "dot" }),
        playerName(game, pid),
        q.answered.includes(pid) ? " ✓" : ""
      )
    )
  );
}

function questionFooter(game, q, me) {
  if (q.revealed) {
    if (q.kind !== "choice") return "";
    const silent = q.participants.filter((pid) => !q.answers?.[pid]);
    return `Верни: ${q.correctPlayers.map((pid) => playerName(game, pid)).join(", ") || "никой"}${silent.length ? ` · Без отговор: ${silent.map((pid) => playerName(game, pid)).join(", ")}` : ""}`;
  }
  if (!q.participants.includes(me)) {
    return `Наблюдаваш. Отговарят: ${q.participants.map((pid) => playerName(game, pid)).join(", ")}`;
  }
  const others = q.answered.filter((pid) => pid !== me).length;
  const mine = q.answered.includes(me) ? "Отговорът ти е изпратен. " : "";
  return `${mine}Отговорили: ${q.answered.length}/${q.participants.length}${others && !mine ? " – побързай!" : ""}`;
}

/**
 * End-of-game overlay with the final standings. Shown once per game until dismissed.
 * `actions.newGame()` starts over, `actions.dismiss()` hides the card to look at the map.
 */
export function renderGameOver(game, me, dismissed, actions) {
  const box = $("#game-over");
  if (game?.phase !== "finished" || dismissed) {
    box.hidden = true;
    return;
  }
  if (!box.hidden) return;
  box.hidden = false;

  const standings = [...game.players].sort((a, b) => (a.isEliminated - b.isEliminated) || b.score - a.score);
  const iWon = game.winnerId === me;
  box.replaceChildren(
    h(
      "div",
      { class: "question-card over-card" },
      h("div", { class: "crown", "aria-hidden": "true" }, iWon ? "👑" : "♜"),
      h("span", { class: "q-title" }, "Край на играта"),
      h("h2", {}, iWon ? "Победа! Царството е твое." : `Победител е ${playerName(game, game.winnerId)}`),
      h(
        "ol",
        { class: "ranking" },
        standings.map((p) =>
          h(
            "li",
            { style: `--player-color:${p.color}` },
            h("strong", { class: "grow" }, p.name, p.id === me ? h("span", { class: "muted small" }, " · ти") : null),
            h("span", {}, p.isEliminated ? "отпадна" : `${p.provinceCount} обл.`),
            h("strong", { style: "color: var(--amber)" }, `${p.score}`)
          )
        )
      ),
      h(
        "div",
        { class: "over-actions" },
        h("button", { class: "btn-cta", onclick: actions.newGame }, "Нова игра ", h("span", { class: "cta-arrow" }, "›")),
        h("button", { class: "btn-glass", onclick: actions.dismiss }, "Виж картата")
      )
    )
  );
}

/** Updates countdown texts and bars; called a few times per second. */
export function tickTimers(clockOffsetMs) {
  for (const node of document.querySelectorAll("[data-deadline]")) {
    const left = secondsLeft(node.dataset.deadline, clockOffsetMs);
    if (node.classList.contains("bar")) {
      const total = (Date.parse(node.dataset.deadline) - Date.parse(node.dataset.start)) / 1000;
      node.style.width = `${Math.max(0, Math.min(100, (left / total) * 100))}%`;
      node.classList.toggle("is-low", left < 5);
    } else {
      node.textContent = `⏱ ${Math.ceil(left)} с`;
      node.classList.toggle("is-low", left < 8);
    }
  }
}
