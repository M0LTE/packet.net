// ============================================================
// pdn — Apps. Top: the launcher — the node's registered apps that expose a web UI
// (GET /api/v1/apps) as a responsive grid of tiles. Each tile is a plain anchor
// to the app's reverse-proxied absolute URL (/apps/{id}/) — a server route OUTSIDE
// the SPA router, so a normal <a href>, not a react-router <Link>. Mobile-first.
//
// Below the grid: the management section (GET /api/v1/apps/packages) — every
// discovered package + inline config-authored app, with its supervisor state.
// Enable/disable/restart are admin-gated (the server is the real gate; this is the
// light-touch UI mirror, like users.tsx). Enabling shows a confirm listing the
// manifest's declared capabilities — the owner sees what they are trusting before
// the POST fires. Disabling needs no confirm. Inline entries are read-only here
// (their enabled flag is config-authored; the API answers 404 for them); a broken
// package (error != null) renders its error and can never be enabled.
// ============================================================
import { useState } from "react";
import { Page, PageHeader } from "@/components/layout/shell";
import { Badge, Button, Card, EmptyState, Icon, Modal, Switch, type BadgeVariant } from "@/components/ui";
import { AppIcon } from "@/components/icon";
import { api, listApps, useQuery } from "@/lib/api";
import { useAuth } from "@/app/auth";
import { cn } from "@/lib/utils";
import type { AppPackage, AppPackageService, AppPackageState } from "@/lib/types";

export function Apps() {
  const { data, loading, error } = useQuery(listApps);
  const apps = data ?? [];

  return (
    <Page>
      <PageHeader
        title="Apps"
        subtitle="Apps published on this node — tap one to open it"
      />

      {error && <EmptyState icon="alert" title="Couldn't load apps" body={error} />}

      {!error && loading && !data && (
        <div className="py-10 text-center text-sm text-muted-foreground">Loading apps…</div>
      )}

      {!error && data && apps.length === 0 && (
        <EmptyState
          icon="apps"
          title="No apps yet"
          body="Apps the node owner registers with a web UI appear here."
        />
      )}

      {!error && apps.length > 0 && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {apps.map((app) => (
            // Absolute, same-origin server route the node reverse-proxies — NOT a SPA
            // route, so a plain <a> (target="_self" keeps it in this tab).
            <a
              key={app.id}
              href={app.url}
              target="_self"
              className="block rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:ring-offset-background"
            >
              <Card className="flex h-full flex-col items-center gap-3 p-5 text-center transition-colors hover:border-primary/40 hover:bg-accent/40">
                <div className="grid h-12 w-12 place-items-center rounded-lg bg-primary/15 text-primary">
                  <AppIcon name={app.icon} size={24} />
                </div>
                <div className="min-w-0">
                  <p className="truncate text-sm font-semibold">{app.name}</p>
                  <p className="truncate font-mono text-[11px] text-muted-foreground">{app.id}</p>
                </div>
              </Card>
            </a>
          ))}
        </div>
      )}

      <PackageManager />
    </Page>
  );
}

// ---- the management section: every package + inline app, with controls --------
function PackageManager() {
  const { has } = useAuth();
  const isAdmin = has("admin");
  const { data, loading, error, reload } = useQuery(api.appPackages, []);
  // The package awaiting its capability confirm (null = no confirm open).
  const [confirming, setConfirming] = useState<AppPackage | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  // A banner-style notice for a failed mutation (mirrors the ports screen's
  // `notice` surface — there is no toast primitive).
  const [notice, setNotice] = useState<string | null>(null);

  const pkgs = data ?? [];

  // Run one mutation, then refetch the whole list — the server is the source of
  // truth (the same reload-after-mutation idiom as the ports/users screens).
  const run = async (id: string, fn: () => Promise<unknown>, fallback: string) => {
    if (busy) return;
    setBusy(id);
    setNotice(null);
    try {
      await fn();
      reload();
    } catch (e) {
      setNotice(e instanceof Error ? e.message : fallback);
    } finally {
      setBusy(null);
    }
  };

  // The enable POST only fires from the capability confirm (below). Disable is
  // immediate — there is no trust decision in turning something off.
  const onToggle = (p: AppPackage, next: boolean) => {
    if (next) setConfirming(p);
    else void run(p.id, () => api.appPackageDisable(p.id), "Could not disable the app.");
  };

  return (
    <section className="mt-8">
      <h2 className="text-sm font-semibold">Manage apps</h2>
      <p className="mt-0.5 text-xs text-muted-foreground">
        Every app package on this node — enable, disable or restart them. Enabling an app grants it the capabilities its manifest declares.
      </p>

      {notice && (
        <div className="mt-3 flex items-start gap-2 rounded-md border border-danger/30 bg-danger/5 px-3 py-2 text-sm text-danger">
          <Icon name="alert" size={15} className="mt-0.5 shrink-0" />
          <span className="flex-1">{notice}</span>
          <button onClick={() => setNotice(null)} className="shrink-0 opacity-70 hover:opacity-100"><Icon name="x" size={14} /></button>
        </div>
      )}

      {error && (
        <div className="mt-3"><EmptyState icon="alert" title="Couldn't load app packages" body={error} /></div>
      )}

      {!error && loading && !data && (
        <div className="py-6 text-center text-sm text-muted-foreground">Loading app packages…</div>
      )}

      {!error && data && pkgs.length === 0 && (
        <div className="mt-3">
          <EmptyState icon="apps" title="No app packages" body="Packages dropped into the node's apps directory appear here." />
        </div>
      )}

      {pkgs.length > 0 && (
        <div className="mt-3 space-y-3">
          {pkgs.map((p) => (
            <div key={p.id} data-pkg={p.id}>
              <PackageRow
                p={p}
                isAdmin={isAdmin}
                busy={busy === p.id}
                onToggle={(next) => onToggle(p, next)}
                onRestart={() => void run(p.id, () => api.appPackageRestart(p.id), "Could not restart the app.")}
              />
            </div>
          ))}
        </div>
      )}

      {/* The capability confirm — the owner sees what the manifest asks for
          before the enable POST fires. Cancelling fires nothing. */}
      <Modal
        open={confirming !== null}
        onClose={() => setConfirming(null)}
        title={`Enable ${confirming?.name ?? ""}?`}
        width="max-w-md"
        footer={
          <>
            <Button variant="outline" size="sm" onClick={() => setConfirming(null)}>Cancel</Button>
            <Button
              size="sm"
              onClick={() => {
                const p = confirming;
                setConfirming(null);
                if (p) void run(p.id, () => api.appPackageEnable(p.id), "Could not enable the app.");
              }}
            >
              <Icon name="check" size={14} /> Enable
            </Button>
          </>
        }
      >
        {confirming && (
          <div className="space-y-3">
            <p className="text-sm text-muted-foreground">
              Enabling <strong className="text-foreground">{confirming.name}</strong> lets it run on this node with the capabilities its manifest declares:
            </p>
            {confirming.capabilities.length > 0 ? (
              <ul className="space-y-1.5">
                {confirming.capabilities.map((c) => (
                  <li key={c} className="flex items-center gap-2 rounded-md bg-muted/40 px-2.5 py-1.5 text-xs">
                    <Icon name="check" size={13} className="shrink-0 text-primary" />
                    <span className="font-mono">{c}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="rounded-md bg-muted/40 px-2.5 py-1.5 text-xs text-muted-foreground">No declared capabilities.</p>
            )}
          </div>
        )}
      </Modal>
    </section>
  );
}

// ---- one package row: identity + state, then the admin controls ----------------
function PackageRow({ p, isAdmin, busy, onToggle, onRestart }: {
  p: AppPackage;
  isAdmin: boolean;
  busy: boolean;
  onToggle: (next: boolean) => void;
  onRestart: () => void;
}) {
  const broken = p.error !== null;
  const inline = p.source === "inline";
  // Why the toggle is read-only, in priority order — the title explains it.
  const toggleTitle = !isAdmin ? "Requires admin"
    : inline ? "Inline apps are managed in the node's config file — edit them there."
    : broken ? "A broken package can't be enabled — fix the error below first."
    : busy ? "Working…"
    : p.enabled ? "Disable this app" : "Enable this app";
  const toggleDisabled = !isAdmin || inline || broken || busy;
  // Restart only makes sense for a managed service that is enabled (a Faulted one
  // included — restarting is exactly how you recover it).
  const showRestart = p.service === "managed" && p.enabled && !broken;

  return (
    <Card className={cn("p-4", (broken || p.state === "Faulted") && "border-danger/40")}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="grid h-9 w-9 shrink-0 place-items-center rounded-md bg-primary/15 text-primary">
            <AppIcon name={p.icon} size={18} />
          </div>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-semibold">{p.name}</span>
              <span className="font-mono text-[11px] text-muted-foreground">{p.id}{p.version ? ` · v${p.version}` : ""}</span>
              <Badge variant={inline ? "muted" : "secondary"}>{p.source}</Badge>
              <StatePill state={p.state} service={p.service} />
              {p.pid !== null && <span className="font-mono text-[11px] text-muted-foreground">pid {p.pid}</span>}
            </div>
            {p.description && <p className="mt-0.5 text-xs text-muted-foreground">{p.description}</p>}
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {showRestart && (
            <Button
              variant="ghost"
              size="sm"
              disabled={!isAdmin || busy}
              title={isAdmin ? "Restart the app's service (teardown + bring-up)" : "Requires admin"}
              onClick={onRestart}
            >
              <Icon name="restart" size={14} /> Restart
            </Button>
          )}
          <Switch checked={p.enabled} disabled={toggleDisabled} title={toggleTitle} onChange={onToggle} />
        </div>
      </div>

      {broken && (
        <div className="mt-3 flex items-center gap-2 rounded-md bg-danger/10 px-2.5 py-1.5 text-xs text-danger">
          <Icon name="alert" size={13} className="shrink-0" /> {p.error}
        </div>
      )}
      {!broken && p.detail && (
        <div className={cn(
          "mt-3 flex items-center gap-2 rounded-md px-2.5 py-1.5 text-xs",
          p.state === "Faulted" ? "bg-danger/10 text-danger" : "bg-warning/10 text-warning",
        )}>
          <Icon name="alert" size={13} className="shrink-0" /> {p.detail}
        </div>
      )}
    </Card>
  );
}

// ---- service-state pill (service "none" = nothing to run → a neutral dash) -----
const STATE_BADGE: Record<AppPackageState, BadgeVariant> = {
  Running: "success",
  Starting: "warning",
  Stopped: "muted",
  Backoff: "warning",
  Faulted: "danger",
  External: "default",
};

function StatePill({ state, service }: { state: AppPackageState | null; service: AppPackageService }) {
  if (service === "none" || state === null) {
    return <span className="text-xs text-muted-foreground">—</span>;
  }
  return <Badge variant={STATE_BADGE[state]}>{state.toLowerCase()}</Badge>;
}
