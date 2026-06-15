// Tests for lib/api.ts's auth behaviours in LIVE mode (VITE_API_MODE=live):
//   1. authFetch's silent renew — a 401 triggers ONE /auth/refresh; on success the original
//      request is retried once with the rotated bearer token; on a failed refresh it falls
//      back to the global logout (the pdn:unauthorized event) and throws Unauthorized.
//   2. refreshIfExpiringSoon (the tab-focus proactive refresh) — renews only when the access
//      token is near/at expiry, is a no-op when it's comfortably valid, and never throws.
//
// MODE is captured at module load, so each live-mode block stubs the env then dynamically
// imports a FRESH copy of the module (vi.resetModules). fetch + localStorage are stubbed per
// test; a window listener captures the pdn:unauthorized logout signal.
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// A JWT with a chosen `exp` (epoch seconds) — header/sig are throwaway; only the payload's
// exp is read by the proactive-refresh decoder. base64url, no padding.
function jwtWithExp(expEpochSeconds: number): string {
  const b64u = (o: unknown) =>
    btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${b64u({ alg: "HS256", typ: "JWT" })}.${b64u({ exp: expEpochSeconds })}.sig`;
}

// Minimal in-memory localStorage for jsdom-independent control of the persisted session.
function installLocalStorage(initial: Record<string, string> = {}): Record<string, string> {
  const store: Record<string, string> = { ...initial };
  vi.stubGlobal("localStorage", {
    getItem: (k: string) => (k in store ? store[k] : null),
    setItem: (k: string, v: string) => { store[k] = v; },
    removeItem: (k: string) => { delete store[k]; },
    clear: () => { for (const k of Object.keys(store)) delete store[k]; },
  });
  return store;
}

function setSession(store: Record<string, string>, s: Record<string, unknown>): void {
  store["pdn.session"] = JSON.stringify(s);
}

// A fresh live-mode module copy (MODE is read at import time).
async function loadLiveApi() {
  vi.resetModules();
  vi.stubEnv("VITE_API_MODE", "live");
  return import("@/lib/api");
}

beforeEach(() => {
  // Web Locks API isn't in jsdom; the cross-tab serializer falls back to a direct rotation
  // when navigator.locks is absent, which is exactly what we want to test deterministically.
  if (typeof navigator !== "undefined") {
    // ensure no stray locks impl leaks in (the cross-tab serializer then takes the direct path)
    delete (navigator as { locks?: unknown }).locks;
  }
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe("api.authFetch — silent refresh on 401", () => {
  it("on a 401 it refreshes once, rotates the stored pair, and retries the original request", async () => {
    const store = installLocalStorage();
    setSession(store, { token: "old.access", refreshToken: "rt-1", username: "tom", scope: "admin" });

    // 1st call (the read) → 401; the refresh → 200 with a rotated pair; the retry → 200.
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(new Response(
        JSON.stringify({ token: "new.access", refreshToken: "rt-2", scopes: "admin", username: "tom" }),
        { status: 200, headers: { "content-type": "application/json" } },
      ))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ok: true }), {
        status: 200, headers: { "content-type": "application/json" },
      }));
    vi.stubGlobal("fetch", fetchMock);

    const apiMod = await loadLiveApi();
    const status = await apiMod.api.status();
    expect(status).toEqual({ ok: true });

    // Exactly three fetches: read(401) → refresh → retry.
    expect(fetchMock).toHaveBeenCalledTimes(3);
    const refreshCall = fetchMock.mock.calls[1];
    expect(String(refreshCall[0])).toContain("/auth/refresh");
    // The body carried the CURRENT refresh token.
    expect(String((refreshCall[1] as RequestInit).body)).toContain("rt-1");
    // The retry carried the NEW access token.
    const retryHeaders = (fetchMock.mock.calls[2][1] as RequestInit).headers as Record<string, string>;
    expect(retryHeaders.authorization).toBe("Bearer new.access");
    // The rotated pair was persisted.
    const persisted = JSON.parse(store["pdn.session"]) as { token: string; refreshToken: string };
    expect(persisted.token).toBe("new.access");
    expect(persisted.refreshToken).toBe("rt-2");
  });

  it("logs out (pdn:unauthorized) and throws when the refresh itself fails", async () => {
    const store = installLocalStorage();
    setSession(store, { token: "old.access", refreshToken: "rt-1", username: "tom", scope: "admin" });

    // read → 401; refresh → 401 (expired/reused). No retry should happen.
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(new Response("", { status: 401 }));
    vi.stubGlobal("fetch", fetchMock);

    let unauthorizedFired = false;
    const onUnauth = () => { unauthorizedFired = true; };

    const apiMod = await loadLiveApi();
    window.addEventListener("pdn:unauthorized", onUnauth);
    try {
      await expect(apiMod.api.status()).rejects.toBeInstanceOf(apiMod.Unauthorized);
    } finally {
      window.removeEventListener("pdn:unauthorized", onUnauth);
    }

    // Only two fetches — the read and the failed refresh; NO retry after a failed refresh.
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(unauthorizedFired).toBe(true);
  });

  it("a burst of concurrent 401s shares ONE refresh (no token stampede)", async () => {
    const store = installLocalStorage();
    setSession(store, { token: "old.access", refreshToken: "rt-1", username: "tom", scope: "admin" });

    let refreshCalls = 0;
    // Every data request 401s on the OLD token and 200s on the NEW one; /auth/refresh rotates
    // the pair exactly once (counted). A stale-token burst should thus share ONE refresh.
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const u = String(url);
      if (u.includes("/auth/refresh")) {
        refreshCalls++;
        return Promise.resolve(new Response(
          JSON.stringify({ token: "new.access", refreshToken: "rt-2", scopes: "admin", username: "tom" }),
          { status: 200, headers: { "content-type": "application/json" } },
        ));
      }
      const auth = (init?.headers as Record<string, string> | undefined)?.authorization;
      const ok = auth === "Bearer new.access";
      return Promise.resolve(new Response(JSON.stringify({ ok }), {
        status: ok ? 200 : 401, headers: { "content-type": "application/json" },
      }));
    });
    vi.stubGlobal("fetch", fetchMock);

    const apiMod = await loadLiveApi();
    // Fire several reads at once — all 401 on the stale token simultaneously.
    const results = await Promise.all([apiMod.api.status(), apiMod.api.ports(), apiMod.api.sessions()]);
    expect(results.every((r) => (r as unknown as { ok: boolean }).ok)).toBe(true);
    // Despite three concurrent 401s, only ONE /auth/refresh happened (shared in-flight promise).
    expect(refreshCalls).toBe(1);
  });
});

describe("api.refreshIfExpiringSoon — proactive tab-focus refresh", () => {
  it("renews when the access token is within the skew of expiry", async () => {
    const store = installLocalStorage();
    const nearExpiry = Math.floor(Date.now() / 1000) + 30;   // 30s left, inside the 60s skew
    setSession(store, { token: jwtWithExp(nearExpiry), refreshToken: "rt-1", username: "tom", scope: "admin" });

    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(
      JSON.stringify({ token: jwtWithExp(nearExpiry + 3600), refreshToken: "rt-2", scopes: "admin", username: "tom" }),
      { status: 200, headers: { "content-type": "application/json" } },
    ));
    vi.stubGlobal("fetch", fetchMock);

    const apiMod = await loadLiveApi();
    const result = await apiMod.refreshIfExpiringSoon(60);
    expect(result).not.toBeNull();
    expect(result!.refreshToken).toBe("rt-2");
    // One /auth/refresh, carrying the current token.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(String(fetchMock.mock.calls[0][0])).toContain("/auth/refresh");
  });

  it("is a no-op when the access token is comfortably valid (no network call)", async () => {
    const store = installLocalStorage();
    const farExpiry = Math.floor(Date.now() / 1000) + 3600;  // an hour left
    setSession(store, { token: jwtWithExp(farExpiry), refreshToken: "rt-1", username: "tom", scope: "admin" });

    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const apiMod = await loadLiveApi();
    const result = await apiMod.refreshIfExpiringSoon(60);
    expect(result).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();   // nothing near expiry → no rotation
  });

  it("is a no-op (no throw) when there is no refresh token", async () => {
    const store = installLocalStorage();
    const nearExpiry = Math.floor(Date.now() / 1000) + 10;
    setSession(store, { token: jwtWithExp(nearExpiry), refreshToken: null, username: null, scope: "admin" });

    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const apiMod = await loadLiveApi();
    await expect(apiMod.refreshIfExpiringSoon(60)).resolves.toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
