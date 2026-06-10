// ============================================================
// pdn — Apps launcher. Lists the node's registered apps that expose a web UI
// (GET /api/v1/apps) as a responsive grid of tiles. Each tile is a plain anchor
// to the app's reverse-proxied absolute URL (/apps/{id}/) — a server route OUTSIDE
// the SPA router, so a normal <a href>, not a react-router <Link>. Mobile-first.
// ============================================================
import { Page, PageHeader } from "@/components/layout/shell";
import { Card, EmptyState } from "@/components/ui";
import { AppIcon } from "@/components/icon";
import { listApps, useQuery } from "@/lib/api";

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
    </Page>
  );
}
