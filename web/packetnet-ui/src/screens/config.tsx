// ============================================================
// Config editor — the whole NodeConfig, validated before apply (README §8/§9).
// Forms vs Raw YAML; left sub-nav (Identity / Services / Management /
// NET/ROM + INP3 / Beacons / Ports →); edits accumulate a dirty set; a
// "Review & apply" opens the reconcile preview, which groups the pending
// changes by disruption (apply live / restart a port / reset the node) in
// plain language and applies them atomically.
// ============================================================
import { useEffect, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { Page, PageHeader } from "@/components/layout/shell";
import {
  Button, Badge, Card, Input, Label, Field, InfoHint, Switch, ImpactBadge, Tabs, Modal, Icon,
} from "@/components/ui";
import { cn } from "@/lib/utils";
import type { NodeConfig, ApplyImpact, FieldHelp, ToggleHelp } from "@/lib/types";
import { api, useQuery } from "@/lib/api";
import {
  APPLY_IMPACT, NETROM_TOGGLE_HELP, NETROM_FIELD_HELP, INP3_FIELD_HELP,
  BEACON_DEFAULT, PORT_BEACONS,
} from "@/lib/mock";

// a pending change, identified by its dotted config path + apply impact
interface DirtyEntry { path: string; impact: ApplyImpact }

type FormTab = "identity" | "services" | "management" | "netrom" | "beacons";
const TABS: { id: FormTab; label: string }[] = [
  { id: "identity", label: "Identity" },
  { id: "services", label: "Services" },
  { id: "management", label: "Management" },
  { id: "netrom", label: "NET/ROM + INP3" },
  { id: "beacons", label: "Beacons" },
];

export function Config() {
  const navigate = useNavigate();
  const { data } = useQuery(api.config, []);

  const [tab, setTab] = useState<FormTab>("identity");
  const [mode, setMode] = useState<"forms" | "raw">("forms");
  const [cfg, setCfg] = useState<NodeConfig | null>(null);
  const [dirty, setDirty] = useState<DirtyEntry[]>([]);
  const [showReconcile, setShowReconcile] = useState(false);

  // seed the editable draft from the loaded config (once)
  useEffect(() => {
    if (data && !cfg) setCfg(structuredClone(data));
  }, [data, cfg]);

  // record a changed path (impact comes from APPLY_IMPACT) — dedup by path
  const touch = (path: string, impact: ApplyImpact) =>
    setDirty((d) => (d.some((x) => x.path === path) ? d : [...d, { path, impact }]));

  // immutable nested set by dotted path + mark dirty
  const set = (path: string, val: unknown, impact: ApplyImpact) => {
    touch(path, impact);
    setCfg((c) => {
      if (!c) return c;
      const next = structuredClone(c) as unknown as Record<string, unknown>;
      const keys = path.split(".");
      let o: Record<string, unknown> = next;
      for (let i = 0; i < keys.length - 1; i++) o = o[keys[i]] as Record<string, unknown>;
      o[keys[keys.length - 1]] = val;
      return next as unknown as NodeConfig;
    });
  };

  return (
    <Page>
      <PageHeader
        title="Config"
        subtitle="Edit the whole node configuration — checked before apply, every write through the reconcile path"
        actions={
          <div className="flex items-center gap-2">
            <Tabs active={mode} onChange={(m) => setMode(m as "forms" | "raw")} tabs={[{ id: "forms", label: "Forms" }, { id: "raw", label: "Raw YAML" }]} />
            <Button size="sm" disabled={dirty.length === 0} onClick={() => setShowReconcile(true)}>
              <Icon name="check" size={14} /> Review &amp; apply
              {dirty.length > 0 && <Badge variant="secondary" className="ml-1">{dirty.length}</Badge>}
            </Button>
          </div>
        }
      />

      {!cfg ? null : mode === "forms" ? (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[200px_1fr]">
          {/* left sub-nav */}
          <div className="flex gap-1 overflow-x-auto lg:flex-col">
            {TABS.map((t) => (
              <button
                key={t.id}
                onClick={() => setTab(t.id)}
                className={cn(
                  "whitespace-nowrap rounded-md px-3 py-2 text-left text-sm font-medium transition-colors",
                  tab === t.id ? "bg-primary/10 text-primary" : "text-muted-foreground hover:bg-accent hover:text-foreground",
                )}
              >
                {t.label}
              </button>
            ))}
            <button
              onClick={() => navigate("/ports")}
              title="Ports are edited on the Ports screen"
              className="flex items-center justify-between gap-2 whitespace-nowrap rounded-md px-3 py-2 text-left text-sm font-medium text-muted-foreground hover:bg-accent hover:text-foreground"
            >
              Ports <Icon name="external" size={13} />
            </button>
          </div>

          <Card className="p-5">
            {tab === "identity" && (
              <section className="max-w-md space-y-4">
                <Field label="Callsign (required)" impact="node-reset" hint="Changing identity resets the node.">
                  <Input value={cfg.identity.callsign} onChange={(e) => set("identity.callsign", e.target.value, "node-reset")} className="font-mono" />
                </Field>
                <Field label="Alias" impact="node-reset">
                  <Input value={cfg.identity.alias ?? ""} onChange={(e) => set("identity.alias", e.target.value, "node-reset")} className="font-mono" />
                </Field>
                <Field label="Locator (grid)" impact="live">
                  <Input value={cfg.identity.grid ?? ""} onChange={(e) => set("identity.grid", e.target.value, "live")} className="font-mono" />
                </Field>
              </section>
            )}

            {tab === "services" && (
              <section className="max-w-xl space-y-4">
                <Field label="Banner" hint="{node} and {call} are templated." impact="live">
                  <Input value={cfg.services.banner} onChange={(e) => set("services.banner", e.target.value, "live")} className="font-mono text-xs" />
                </Field>
                <Field label="Prompt" impact="live">
                  <Input value={cfg.services.prompt} onChange={(e) => set("services.prompt", e.target.value, "live")} className="font-mono" />
                </Field>
              </section>
            )}

            {tab === "management" && (
              <section className="max-w-xl space-y-5">
                <div className="rounded-lg border border-border p-3">
                  <div className="mb-3 flex items-center justify-between">
                    <Label className="text-foreground">HTTP (this UI)</Label>
                    <ImpactBadge impact={APPLY_IMPACT["management.http"]} />
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <Field label="Bind"><Input value={cfg.management.http.bind} onChange={(e) => set("management.http.bind", e.target.value, "node-reset")} className="font-mono" /></Field>
                    <Field label="Port"><Input type="number" value={cfg.management.http.port} onChange={(e) => set("management.http.port", +e.target.value, "node-reset")} className="font-mono" /></Field>
                  </div>
                </div>
                <div className="rounded-lg border border-border p-3">
                  <div className="mb-3 flex items-center justify-between">
                    <Label className="text-foreground">Telnet console</Label>
                    <ImpactBadge impact={APPLY_IMPACT["management.telnet"]} />
                  </div>
                  <div className="grid grid-cols-3 gap-3">
                    <Field label="Enabled"><div className="flex h-9 items-center"><Switch checked={cfg.management.telnet.enabled} onChange={(v) => set("management.telnet.enabled", v, "port-restart")} /></div></Field>
                    <Field label="Bind"><Input value={cfg.management.telnet.bind} onChange={(e) => set("management.telnet.bind", e.target.value, "port-restart")} className="font-mono" /></Field>
                    <Field label="Port"><Input type="number" value={cfg.management.telnet.port} onChange={(e) => set("management.telnet.port", +e.target.value, "port-restart")} className="font-mono" /></Field>
                  </div>
                </div>
              </section>
            )}

            {tab === "netrom" && <NetRomSection cfg={cfg} set={set} />}

            {tab === "beacons" && <BeaconsSection />}
          </Card>
        </div>
      ) : (
        <RawYaml cfg={cfg} onValidate={() => setShowReconcile(true)} />
      )}

      <ReconcilePreview
        open={showReconcile}
        dirty={dirty}
        onClose={() => setShowReconcile(false)}
        onApply={() => { setShowReconcile(false); setDirty([]); }}
      />
    </Page>
  );
}

// ---------- NET/ROM + INP3: guidance, not jargon ------------
function NetRomSection({ cfg, set }: { cfg: NodeConfig; set: (path: string, val: unknown, impact: ApplyImpact) => void }) {
  const nr = cfg.netRom;
  const inp3 = nr.inp3;
  const toggleKeys = ["enabled", "broadcast", "connect", "forward"] as const;
  const numKeys = ["defaultNeighbourQuality", "minQuality", "sweepIntervalSeconds", "timeToLive", "window"] as const;
  const inp3Keys = ["l3RttInterval", "l3RttResetWindow", "rifInterval", "positiveDebounce"] as const;
  const nrRec = nr as unknown as Record<string, number | undefined>;
  const inp3Rec = inp3 as unknown as Record<string, number>;

  return (
    <section className="space-y-5">
      <p className="max-w-2xl text-sm text-muted-foreground">
        NET/ROM is the layer that turns your node from a single radio link into part of a routed network — it learns which stations are reachable and relays traffic between them. The switches below decide how much your node takes part.
      </p>

      <div className="space-y-2">
        {toggleKeys.map((k) => {
          const help: ToggleHelp = NETROM_TOGGLE_HELP[k];
          return (
            <ToggleRow
              key={k}
              label={help.label}
              desc={help.desc}
              checked={nr[k]}
              onChange={(v) => set("netRom." + k, v, "live")}
            />
          );
        })}
      </div>

      <div className="max-w-xs">
        <Field label={NETROM_FIELD_HELP.alias.label} info={NETROM_FIELD_HELP.alias.help}>
          <Input value={nr.alias ?? ""} onChange={(e) => set("netRom.alias", e.target.value, "live")} className="font-mono" />
        </Field>
      </div>

      <AdvancedDetails title="Advanced routing tuning">
        <p className="mb-3 text-xs text-muted-foreground">Most nodes never touch these — the defaults are sensible. Adjust only if you understand the trade-off.</p>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {numKeys.map((k) => (
            <GuidedNum key={k} meta={NETROM_FIELD_HELP[k]} value={nrRec[k] ?? 0} onChange={(v) => set("netRom." + k, v, "live")} />
          ))}
        </div>
      </AdvancedDetails>

      <div className="rounded-lg border border-primary/30 bg-primary/5 p-4">
        <Label className="flex items-center gap-1.5 text-primary"><Icon name="signal" size={14} /> INP3 time-routing</Label>
        <p className="mt-1.5 max-w-2xl text-xs text-muted-foreground">
          An overlay that measures the <strong>actual round-trip time</strong> to each destination. Plain NET/ROM picks routes by a static quality score; INP3 lets the node prefer the route that&apos;s genuinely fastest right now.
        </p>
        <div className="mt-3 space-y-2">
          <ToggleRow
            label="Use INP3 time-routing"
            desc="Measure and track real path times across the network."
            checked={inp3.enabled}
            onChange={(v) => set("netRom.inp3.enabled", v, "live")}
          />
          <ToggleRow
            label="Prefer the faster route"
            desc="When a measured time is available, choose routes by speed ahead of the static quality score."
            checked={inp3.preferInp3Routes}
            onChange={(v) => set("netRom.inp3.preferInp3Routes", v, "live")}
          />
        </div>
        <div className="mt-3">
          <AdvancedDetails title="INP3 timing intervals">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              {inp3Keys.map((k) => (
                <GuidedNum key={k} meta={INP3_FIELD_HELP[k]} value={inp3Rec[k]} onChange={(v) => set("netRom.inp3." + k, v, "live")} />
              ))}
            </div>
          </AdvancedDetails>
        </div>
      </div>
    </section>
  );
}

// labelled on/off choice with a one-line plain-English description
function ToggleRow({ label, desc, checked, onChange }: { label: string; desc?: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="flex items-start justify-between gap-4 rounded-lg border border-border p-3">
      <div className="min-w-0">
        <p className="text-sm font-medium text-foreground">{label}</p>
        {desc && <p className="mt-0.5 text-xs leading-snug text-muted-foreground">{desc}</p>}
      </div>
      <div className="shrink-0 pt-0.5"><Switch checked={checked} onChange={onChange} /></div>
    </div>
  );
}

// numeric field driven by a {label, unit, help} descriptor (tooltip + unit suffix)
function GuidedNum({ meta, value, onChange }: { meta: FieldHelp; value: number; onChange: (v: number) => void }) {
  const suffix = meta.unit && !meta.unit.includes("–") ? meta.unit : null;
  return (
    <Field label={meta.label} info={meta.help}>
      <div className="relative">
        <Input type="number" value={value} onChange={(e) => onChange(+e.target.value)} className={cn("font-mono", suffix && "pr-16")} />
        {suffix && <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-[11px] text-muted-foreground">{suffix}</span>}
      </div>
    </Field>
  );
}

// collapsible "advanced" panel (closed by default)
function AdvancedDetails({ title, children }: { title: string; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="rounded-lg border border-border">
      <button onClick={() => setOpen((o) => !o)} className="flex w-full items-center justify-between p-3 text-sm font-medium text-foreground">
        <span className="flex items-center gap-2"><Icon name="config" size={14} className="text-muted-foreground" /> {title}</span>
        <Icon name="chevDown" size={15} className={cn("text-muted-foreground transition-transform", open && "rotate-180")} />
      </button>
      {open && <div className="border-t border-border p-3">{children}</div>}
    </div>
  );
}

// ---------- Beacons (README §9): system default + per-port ----
function BeaconsSection() {
  const [def, setDef] = useState(BEACON_DEFAULT);
  const [ports, setPorts] = useState(PORT_BEACONS);
  const setPort = (id: string, patch: Partial<typeof ports[string]>) =>
    setPorts((p) => ({ ...p, [id]: { ...p[id], ...patch } }));

  return (
    <section className="space-y-5">
      <div className="rounded-lg border border-border p-4">
        <div className="mb-3 flex items-center gap-1.5">
          <Label className="text-foreground">System default beacon</Label>
          <InfoHint text="The ID beacon pdn sends on a port unless that port overrides it. {node} and {call} are filled in automatically." />
        </div>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-[160px_1fr]">
          <Field label="Every" info="How often the ID beacon is transmitted.">
            <div className="relative">
              <Input type="number" value={def.intervalMinutes} onChange={(e) => setDef((d) => ({ ...d, intervalMinutes: +e.target.value }))} className="pr-16 font-mono" />
              <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-[11px] text-muted-foreground">minutes</span>
            </div>
          </Field>
          <Field label="Text"><Input value={def.text} onChange={(e) => setDef((d) => ({ ...d, text: e.target.value }))} className="font-mono text-xs" /></Field>
        </div>
      </div>

      <div>
        <p className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">Per-port</p>
        <div className="space-y-2">
          {Object.keys(ports).map((id) => {
            const b = ports[id];
            const overriding = b.text != null;
            return (
              <div key={id} className="rounded-lg border border-border p-3">
                <div className="flex items-center justify-between">
                  <span className="flex items-center gap-2.5">
                    <Switch checked={b.enabled} onChange={(v) => setPort(id, { enabled: v })} />
                    <span className="font-mono text-sm font-semibold">{id}</span>
                    {!b.enabled && <Badge variant="muted">no beacon</Badge>}
                  </span>
                  {b.enabled && (
                    <span className="flex items-center gap-2 text-xs text-muted-foreground">
                      every
                      <Input type="number" value={b.intervalMinutes} onChange={(e) => setPort(id, { intervalMinutes: +e.target.value })} className="h-7 w-16 font-mono text-xs" />
                      min
                    </span>
                  )}
                </div>
                {b.enabled && (
                  <div className="mt-3">
                    {overriding ? (
                      <Field
                        label="Custom text"
                        badge={<button onClick={() => setPort(id, { text: null })} className="text-[11px] text-muted-foreground hover:text-primary">use default</button>}
                      >
                        <Input value={b.text ?? ""} onChange={(e) => setPort(id, { text: e.target.value })} className="font-mono text-xs" />
                      </Field>
                    ) : (
                      <div className="flex items-center justify-between rounded-md bg-muted/40 px-2.5 py-2 text-xs">
                        <span className="text-muted-foreground">Uses default — <span className="font-mono text-foreground/70">{def.text}</span></span>
                        <button onClick={() => setPort(id, { text: def.text })} className="shrink-0 font-medium text-primary hover:underline">Override</button>
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}

// ---------- Raw YAML view -----------------------------------
function RawYaml({ cfg, onValidate }: { cfg: NodeConfig; onValidate: () => void }) {
  const yaml = toYaml(cfg as unknown as Record<string, unknown>);
  return (
    <Card className="overflow-hidden p-0">
      <div className="flex items-center justify-between border-b border-border bg-muted/30 px-4 py-2">
        <span className="flex items-center gap-2 text-xs text-muted-foreground"><Icon name="config" size={13} /> node-config.yaml · advanced</span>
        <div className="flex items-center gap-2">
          <span className="flex items-center gap-1.5 text-xs text-success"><Icon name="check" size={13} /> valid</span>
          <Button variant="outline" size="xs" onClick={onValidate}>Validate &amp; preview</Button>
        </div>
      </div>
      <textarea
        spellCheck={false}
        defaultValue={yaml}
        className="h-[calc(100vh-20rem)] w-full resize-none bg-background/40 p-4 font-mono text-xs leading-relaxed text-foreground/90 focus:outline-none"
      />
    </Card>
  );
}

// minimal YAML serialiser for the raw view (display only)
function toYaml(obj: Record<string, unknown>, indent = 0): string {
  const pad = "  ".repeat(indent);
  let out = "";
  for (const [k, v] of Object.entries(obj)) {
    if (v === null || v === undefined) out += `${pad}${k}: null\n`;
    else if (Array.isArray(v)) {
      out += `${pad}${k}:\n`;
      v.forEach((item) => {
        if (item && typeof item === "object") {
          out += `${pad}  - ${toYaml(item as Record<string, unknown>, indent + 2).replace(/^\s+/, "").replace(/\n {0,}(\S)/g, (m, c: string, o: number) => (o === 0 ? m : `\n${pad}    ${c}`))}`;
        } else out += `${pad}  - ${String(item)}\n`;
      });
    } else if (typeof v === "object") out += `${pad}${k}:\n` + toYaml(v as Record<string, unknown>, indent + 1);
    else out += `${pad}${k}: ${String(v)}\n`;
  }
  return out;
}

// ---------- Reconcile preview: the safety story -------------
function ReconcilePreview({ open, dirty, onClose, onApply }: { open: boolean; dirty: DirtyEntry[]; onClose: () => void; onApply: () => void }) {
  const live = dirty.filter((d) => d.impact === "live");
  const restart = dirty.filter((d) => d.impact === "port-restart");
  const reset = dirty.filter((d) => d.impact === "node-reset");
  return (
    <Modal
      open={open}
      onClose={onClose}
      width="max-w-lg"
      title="Review & apply"
      footer={
        <>
          <Button variant="outline" size="sm" onClick={onClose}>Cancel</Button>
          <Button size="sm" onClick={onApply} className={reset.length ? "bg-danger text-danger-foreground hover:bg-danger/90" : undefined}>
            {reset.length ? <><Icon name="alert" size={14} /> Apply — resets the node</> : <><Icon name="check" size={14} /> Apply all at once</>}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Your changes are checked before anything is applied — a bad edit never reaches the running node. Valid changes are then applied all at once.
        </p>
        <ReconcileGroup variant="success" icon="check" title={`${live.length} applies live`} items={live} desc="hot-applied while the node keeps running — nothing drops." />
        <ReconcileGroup variant="warning" icon="restart" title={`${restart.length} restarts a port`} items={restart} desc="the console port bounces; sessions on that port drop." />
        <ReconcileGroup variant="danger" icon="alert" title={`${reset.length} resets the node`} items={reset} desc="a node-wide restart — every session on every port drops." />
      </div>
    </Modal>
  );
}

function ReconcileGroup({ variant, icon, title, items, desc }: { variant: "success" | "warning" | "danger"; icon: string; title: string; items: DirtyEntry[]; desc: string }) {
  if (items.length === 0) return null;
  const c = {
    success: "border-success/30 bg-success/5 text-success",
    warning: "border-warning/30 bg-warning/5 text-warning",
    danger: "border-danger/30 bg-danger/5 text-danger",
  }[variant];
  return (
    <div className={cn("rounded-lg border p-3", c)}>
      <p className="flex items-center gap-2 text-sm font-semibold"><Icon name={icon} size={14} /> {title}</p>
      <p className="mt-0.5 text-xs opacity-80">{desc}</p>
      <ul className="mt-2 space-y-0.5">
        {items.map((i) => <li key={i.path} className="font-mono text-[11px] text-foreground/70">· {i.path}</li>)}
      </ul>
    </div>
  );
}
