// Smoke test for the Console screen (the browser node command console): mounts it against a
// mocked api, asserts it opens a console session + mounts the xterm container, that streamed
// output is written into the terminal, and that a keystroke (xterm onData) fires the input POST.
// Kept light but real — the api is spied so no node is needed, but the real <Console> + a real
// xterm Terminal run (the verification gate in lieu of a headless browser).
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { AuthProvider } from "@/app/auth";
import { Console } from "@/screens/console";
import * as apiModule from "@/lib/api";

function seedAdmin() {
  localStorage.setItem(
    "pdn.session",
    JSON.stringify({ token: "test.jwt", refreshToken: null, username: "tom", scope: "admin" }),
  );
}

function mount() {
  return render(
    <MemoryRouter initialEntries={["/console"]}>
      <AuthProvider>
        <Console />
      </AuthProvider>
    </MemoryRouter>,
  );
}

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

describe("Console screen", () => {
  it("opens a console session and mounts the terminal", async () => {
    seedAdmin();
    const open = vi.spyOn(apiModule.api, "openConsole").mockResolvedValue("console:test");
    vi.spyOn(apiModule, "subscribeConsoleOutput").mockReturnValue(() => {});
    vi.spyOn(apiModule.api, "closeConsole").mockResolvedValue();

    mount();

    // The terminal host always renders; the session open fires on mount.
    expect(screen.getByTestId("console-terminal")).toBeInTheDocument();
    await waitFor(() => expect(open).toHaveBeenCalledTimes(1));
    // Once open, the status chip reads "connected".
    await waitFor(() => expect(screen.getByText(/connected/i)).toBeInTheDocument());
  });

  it("writes streamed output into the terminal and POSTs typed input back", async () => {
    seedAdmin();
    vi.spyOn(apiModule.api, "openConsole").mockResolvedValue("console:test");
    const input = vi.spyOn(apiModule.api, "consoleInput").mockResolvedValue();
    vi.spyOn(apiModule.api, "closeConsole").mockResolvedValue();

    // Capture the onChunk callback the screen registers so the test can push output, and
    // return a no-op unsubscribe (the real EventSource never opens under jsdom).
    let pushChunk: ((text: string) => void) | null = null;
    vi.spyOn(apiModule, "subscribeConsoleOutput").mockImplementation((_id, onChunk) => {
      pushChunk = onChunk;
      return () => {};
    });

    const { container } = mount();
    await waitFor(() => expect(pushChunk).not.toBeNull());

    // Push a banner chunk → it must reach the terminal's rendered rows.
    act(() => { pushChunk!("PDN-CONSOLE-BANNER\r\n"); });
    await waitFor(() => expect(container.textContent).toContain("PDN-CONSOLE-BANNER"));

    // Simulate a keystroke: xterm raises onData for typed input. The host textarea is the
    // input sink; firing an input event drives onData, which the screen forwards to consoleInput.
    const textarea = container.querySelector("textarea.xterm-helper-textarea") as HTMLTextAreaElement | null;
    expect(textarea).not.toBeNull();
    act(() => {
      textarea!.value = "?";
      textarea!.dispatchEvent(new InputEvent("input", { data: "?", inputType: "insertText", bubbles: true }));
    });

    await waitFor(() => expect(input).toHaveBeenCalled());
    expect(input.mock.calls[0][0]).toBe("console:test");
  });

  it("shows an admin-required state and does not open a session for a non-admin", async () => {
    localStorage.setItem(
      "pdn.session",
      JSON.stringify({ token: "test.jwt", refreshToken: null, username: "ro", scope: "read" }),
    );
    const open = vi.spyOn(apiModule.api, "openConsole").mockResolvedValue("console:test");

    mount();

    await waitFor(() => expect(screen.getByText(/requires the admin scope/i)).toBeInTheDocument());
    expect(open).not.toHaveBeenCalled();
  });
});
