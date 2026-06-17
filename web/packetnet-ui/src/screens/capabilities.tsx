// ============================================================
// pdn — Capabilities (the learned per-peer AX.25 capability cache): which
// neighbours speak v2.2/SABME and which answer a pre-connect XID (SREJ), so a
// dial can skip probes a known non-answerer would only stall on. Read-only,
// plus a per-row "Forget" that clears one (port, peer) so the next dial re-probes.
// Modelled on screens/routes.tsx (useQuery + a Card+table).
// ============================================================
import { useState } from "react";
import { Page, PageHeader } from "@/components/layout/shell";
import { Button, Badge, Card, Th, Td, InfoHint, EmptyState, Icon } from "@/components/ui";
import { api, useQuery } from "@/lib/api";
import type { PeerCapability } from "@/lib/types";

export function Capabilities() {
  const { data, loading, error, reload } = useQuery(api.capabilities);

  return (
    <Page>
      <PageHeader
        title="Capabilities"
        subtitle="What each neighbour speaks — learned from past dials, so we skip probes a known non-answerer would only stall on. A negative is re-probed after ~30 days."
      />

      {error && <EmptyState icon="alert" title="Couldn't load capabilities" body={error} />}
      {!error && loading && !data && (
        <div className="py-10 text-center text-sm text-muted-foreground">Loading capabilities…</div>
      )}
      {!error && data && (data.length === 0
        ? <EmptyState icon="radio" title="Nothing learned yet" body="The node remembers a neighbour's v2.2 / SREJ support after it first dials out to it. Once a dial returns, its (port, peer) record appears here." />
        : <CapabilityTable rows={data} onForget={reload} />
      )}
    </Page>
  );
}

function CapabilityTable({ rows, onForget }: { rows: PeerCapability[]; onForget: () => void }) {
  return (
    <Card className="overflow-hidden p-0">
      <div className="overflow-x-auto">
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-border">
              <Th>Port</Th>
              <Th>Peer</Th>
              <Th>
                <span className="inline-flex items-center gap-1">
                  Version
                  <InfoHint text="Whether this neighbour accepts the AX.25 v2.2 extended (SABME / mod-128) link setup. v2.2 = it does, v2.0 = it refused/degraded to mod-8, v2.2? = not yet probed." />
                </span>
              </Th>
              <Th>
                <span className="inline-flex items-center gap-1">
                  SREJ
                  <InfoHint text="Whether this neighbour answers a pre-connect XID with selective-reject (SREJ) enabled — how we discover SREJ support before committing a connect. SREJ = yes, REJ = no, SREJ? = not yet probed." />
                </span>
              </Th>
              <Th className="text-right">Last probed</Th>
              <Th className="text-right">Last refused</Th>
              <Th className="w-px" />
            </tr>
          </thead>
          <tbody>
            {rows.map((c) => (
              <CapabilityRow key={`${c.portId}:${c.peer}`} cap={c} onForget={onForget} />
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function CapabilityRow({ cap, onForget }: { cap: PeerCapability; onForget: () => void }) {
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const forget = async () => {
    setBusy(true);
    setErr(null);
    try {
      await api.clearCapability(`${cap.portId}:${cap.peer}`);
      onForget();
    } catch (e) {
      setErr(String((e as Error)?.message ?? e));
      setBusy(false);
    }
  };

  return (
    <tr className="border-b border-border/60 hover:bg-accent/40">
      <Td><Badge variant="muted">{cap.portId}</Badge></Td>
      <Td className="font-mono font-semibold">{cap.peer}</Td>
      <Td><VersionBadge value={cap.supportsExtended} /></Td>
      <Td><SrejBadge value={cap.supportsSrejViaXid} /></Td>
      <Td className="text-right font-mono text-xs text-muted-foreground">{cap.lastProbed}</Td>
      <Td className="text-right font-mono text-xs text-muted-foreground">{cap.lastRefused ?? "—"}</Td>
      <Td>
        <div className="flex items-center justify-end gap-2">
          {err && <span className="text-xs text-danger" title={err}>failed</span>}
          <Button variant="ghost" size="xs" disabled={busy} onClick={forget} title="Forget this learned record — the next dial will re-probe">
            <Icon name="trash" size={13} /> {busy ? "Forgetting…" : "Forget"}
          </Button>
        </div>
      </Td>
    </tr>
  );
}

// v2.2 (extended) / v2.0 (refused/degraded) / v2.2? (never probed) — the three-state
// rendering of supportsExtended, matching the console's CAP listing badges.
function VersionBadge({ value }: { value: boolean | null }) {
  if (value === true) return <Badge variant="success">v2.2</Badge>;
  if (value === false) return <Badge variant="muted">v2.0</Badge>;
  return <Badge variant="outline">v2.2?</Badge>;
}

// SREJ (answers XID with SREJ) / REJ (does not) / SREJ? (never probed) — the three-state
// rendering of supportsSrejViaXid, matching the console's CAP listing badges.
function SrejBadge({ value }: { value: boolean | null }) {
  if (value === true) return <Badge variant="success">SREJ</Badge>;
  if (value === false) return <Badge variant="muted">REJ</Badge>;
  return <Badge variant="outline">SREJ?</Badge>;
}
