// ============================================================
// Client auth state — the JWT from POST /auth/login (Argon2id password; passkey
// deferred) gates the app. A user is granted exactly ONE scope (read/operate/
// admin) and the implication admin⊃operate⊃read is resolved here by `has()`,
// mirroring the server's AuthScopes.Satisfies rank model.
//
// Persistence: localStorage — the session survives browser restarts; the refresh-token
// rotation (+ family revocation on reuse) bounds the blast radius of a stolen pair, and
// the panel is a control
// panel for a node you actively manage, not a "remember me" consumer app, so a
// short-lived per-tab token is the safer default. Swap KEY's backing to
// localStorage if cross-tab persistence is ever wanted.
//
// Works in BOTH server modes (the gate, in router.tsx, decides which):
//   - auth OFF  → requests 200 tokenless; the gate probes /status, gets 200,
//                 and enters the app with no token (token === null, but authed).
//   - auth ON   → an unauthorised probe/call 401s; the gate / the 401 handler
//                 drive the operator to /login.
// In mock mode there is no real auth: the gate enters directly with a synthetic
// admin session so every screen renders (the vitest smoke test relies on this).
//
// 401 handling: lib/api.ts owns the single fetch path; on a 401 it dispatches a
// window "pdn:unauthorized" event. The provider listens and runs logout() →
// token cleared, gate falls back to /login. (An event keeps api.ts free of a
// React-context import — it's plain TS.)
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

/** The granted scope on a token, lowest→highest privilege. */
export type Scope = "read" | "operate" | "admin";

/** Rank for the admin⊃operate⊃read implication (matches server AuthScopes.Rank). */
const RANK: Record<string, number> = { read: 1, operate: 2, admin: 3 };

interface Session {
  /** The JWT, or null in auth-off / pre-login states (the app still works tokenless). */
  token: string | null;
  /** The opaque refresh token (one-time-use, rotated on each /auth/refresh), or null
   *  in auth-off / pre-login / mock states. Persisted alongside the access token so a
   *  reload can silently renew an expired access token without a re-login. */
  refreshToken: string | null;
  /** The login name, or null when we entered without logging in (auth off / mock). */
  username: string | null;
  /** The granted scope, or null if unknown. `has()` treats null as "satisfies nothing". */
  scope: Scope | null;
}

interface AuthState extends Session {
  /** Whether the gate has let the operator into the app (token-backed or tokenless). */
  authed: boolean;
  /** Record a successful login (access + refresh token + granted scope + username) and
   *  enter the app. */
  login: (token: string, scope: string, username: string, refreshToken: string | null) => void;
  /** Enter the app WITHOUT a token — auth-off probe succeeded, or mock mode. */
  enterAnonymous: (scope?: Scope) => void;
  /** Clear the session (best-effort revoking the refresh token server-side) and drop
   *  back to the login gate. */
  logout: () => void;
  /** Whether the current scope satisfies `required` under admin⊃operate⊃read. */
  has: (required: Scope) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);
const KEY = "pdn.session";

const EMPTY_SESSION: Session = { token: null, refreshToken: null, username: null, scope: null };

/** The event lib/api.ts dispatches when any call comes back 401. */
export const UNAUTHORIZED_EVENT = "pdn:unauthorized";

// lib/api.ts registers a best-effort server-side revoke here (POST /auth/logout with
// the stored refresh token). A registration hook — rather than a static import of
// api.ts — keeps auth.tsx free of an import cycle (api.ts already imports from here).
let revokeOnLogout: (() => void) | null = null;
/** Called once by lib/api.ts at module load to wire the logout revoke. */
export function setLogoutRevoker(fn: () => void): void {
  revokeOnLogout = fn;
}

function load(): Session {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return EMPTY_SESSION;
    const s = JSON.parse(raw) as Session;
    return {
      token: s.token ?? null,
      refreshToken: s.refreshToken ?? null,
      username: s.username ?? null,
      scope: s.scope ?? null,
    };
  } catch {
    return EMPTY_SESSION;
  }
}

function save(s: Session): void {
  try {
    if (s.token) localStorage.setItem(KEY, JSON.stringify(s));
    else localStorage.removeItem(KEY);
  } catch {
    /* private-mode / quota — non-fatal, the in-memory state still drives the app */
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // Rehydrate any persisted token so a reload stays logged in; the gate still
  // probes it for validity (an expired token 401s the probe → relogin).
  const [session, setSession] = useState<Session>(load);
  const [authed, setAuthed] = useState(false);

  const logout = () => {
    // Best-effort server-side revoke of the refresh-token family (POST /auth/logout
    // via the registered revoker), then clear local state. The revoke reads the token
    // straight from localStorage, so it must run BEFORE we clear it.
    try { revokeOnLogout?.(); } catch { /* logout must always clear locally */ }
    save(EMPTY_SESSION);
    setSession(EMPTY_SESSION);
    setAuthed(false);
  };

  // A 401 from anywhere (lib/api.ts) clears the session and drops to the gate.
  useEffect(() => {
    const onUnauthorized = () => logout();
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
  }, []);

  const value: AuthState = {
    ...session,
    authed,
    login: (token, scope, username, refreshToken) => {
      const s: Session = { token, refreshToken, username, scope: (scope as Scope) ?? null };
      save(s);
      setSession(s);
      setAuthed(true);
    },
    enterAnonymous: (scope: Scope = "admin") => {
      // No token (auth off, or mock): enter with the given effective scope. Default
      // admin so a tokenless lab node (or mock) exposes every action — the server
      // is the real gate either way.
      const s: Session = { token: null, refreshToken: null, username: null, scope };
      setSession(s);
      setAuthed(true);
    },
    logout,
    has: (required) => {
      const granted = session.scope;
      const needed = RANK[required] ?? 0;
      return needed > 0 && (RANK[granted ?? ""] ?? 0) >= needed;
    },
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
