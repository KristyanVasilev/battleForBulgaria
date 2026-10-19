// Thin wrapper around the SignalR hub at /hubs/game.
// The session (game code + secret player token) is kept in sessionStorage, so a page refresh
// resumes the game, while separate tabs can play as different players during local testing.

const SESSION_KEY = "bc.session";

export function loadSession() {
  try {
    return JSON.parse(sessionStorage.getItem(SESSION_KEY) ?? "null");
  } catch {
    return null;
  }
}

export function saveSession(session) {
  try {
    if (session) sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
    else sessionStorage.removeItem(SESSION_KEY);
  } catch {
    /* storage unavailable – the game still works until refresh */
  }
}

/** Removes SignalR's "An unexpected error occurred invoking ... HubException:" prefix. */
export function friendlyError(err) {
  const msg = err?.message ?? String(err);
  return msg.includes("HubException:") ? msg.split("HubException:").pop().trim() : msg;
}

export class GameConnection {
  #hub;
  #onUpdate = () => {};
  #onStatus = () => {};

  constructor() {
    this.#hub = new signalR.HubConnectionBuilder()
      .withUrl("hubs/game")
      .withAutomaticReconnect()
      .build();
    this.#hub.on("GameUpdated", (view) => this.#onUpdate(view));
    this.#hub.onreconnecting(() => this.#onStatus("reconnecting"));
    this.#hub.onreconnected(() => this.#onStatus("reconnected"));
    this.#hub.onclose(() => this.#onStatus("closed"));
  }

  onUpdate(handler) {
    this.#onUpdate = handler;
  }

  onStatus(handler) {
    this.#onStatus = handler;
  }

  start() {
    return this.#hub.start();
  }

  invoke(method, ...args) {
    return this.#hub.invoke(method, ...args);
  }
}
