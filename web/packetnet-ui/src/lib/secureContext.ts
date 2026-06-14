// ============================================================
// Secure-context probe for passkeys (network-access S1).
//
// WebAuthn/passkeys only work in a "potentially trustworthy" origin: HTTPS with a
// cert the browser already trusts, OR http://localhost / 127.0.0.1 (the loopback
// exemption). On a plain-HTTP LAN deployment (http://<ip>) the ceremony cannot
// succeed, so the UI must HIDE the passkey affordances rather than offer a button
// that errors on tap — password (+ over-RF TOTP) login remains the LAN path.
//
// This is the single source of truth for "could a passkey ceremony run here?". It
// looks only at the platform (`window.isSecureContext` + the credentials API), not
// the API mode — the api.ts `webauthnSupported()` wrapper layers the mock-mode guard
// on top (a mock backend has no node to run a real ceremony against).
// ============================================================

/** True iff the browser is in a secure context AND exposes the WebAuthn API — i.e. a
 *  passkey register/assert ceremony could actually run. False on plain-HTTP LAN
 *  (no secure context) and in any non-browser environment (SSR / jsdom default). */
export const passkeysAvailable = (): boolean =>
  typeof window !== "undefined" &&
  window.isSecureContext === true &&
  typeof window.PublicKeyCredential !== "undefined";
