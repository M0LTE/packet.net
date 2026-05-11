# A minimal SDL primer for AX.25 transcribers

This is a working reference for reading the SDL diagrams in the AX.25 v2.2
specification. It is **not** an introduction to SDL as a language — just
enough vocabulary to transcribe AX.25 diagrams into our YAML DSL
(see `docs/adr/0001-sdl-dsl.md`) without misreading shapes.

If you're staring at one of the figures in
`https://github.com/packethacking/ax25spec/blob/main/doc/ax.25.2.2.4_Oct_25.md`
and wondering whether a particular box is meaningful — start here.

## The seven shapes you'll actually see

### 1. State

Both ends concave inward — looks like a banner or rolled-edge flag:

```
   ╭───────────╮
  ╱   3        ╲
 (    Connected )
  ╲             ╱
   ╰───────────╯
```

A **state** is a resting point. The machine sits here until an input arrives.

Every input handler chain in a diagram **starts** at a state symbol (which
the SDL convention places at the top) and **ends** at a state symbol (which
may be the same state — "stay where you are" — or a different one). The
text inside is the state name, often prefixed with a state number
("3 Connected", "2 Awaiting Release").

### 2. Input

Rectangle with a **concave notch on the left side**, pointing into the box.
Think "incoming arrow eaten into the rectangle":

```
 ⟩───────────╮
 │ DL-DATA   │
 │ Request   │
 ╰───────────╯
```

An **input** is the event that wakes a state up — a primitive from the
upper layer, a frame received from the peer, or a timer expiry. The text
inside identifies the event:

- Upper-layer primitives: `DL-DATA Request`, `DL-CONNECT Request`,
  `DL-FLOW-OFF Request`, `DL-UNIT-DATA Request`, `DL-DISCONNECT Request`,
  `DL-FLOW-ON Request`
- Frames from the peer: `I Frame`, `RR`, `RNR`, `REJ`, `SREJ`, `SABM`,
  `SABME`, `DISC`, `UA`, `DM`, `FRMR`, `XID`, `UI`, `TEST`
- Timer expiries: `T1 Expiry`, `T2 Expiry`, `T3 Expiry`
- Internal: `I Frame Pops Off Queue`, `Control Field Error`, `All Other
  Primitives`

### 3. Output

Rectangle with a **convex tab on the right side**. Think "arrow squeezed
out of the rectangle":

```
 ╭───────────⟩
 │ RR        │
 │ Command   │
 ╰───────────╯
```

An **output** is a signal we *send* — a frame to the peer, or a primitive
to the upper layer. The most common ones:

- Frames TX'd to the peer: `RR Command`, `RR Response`, `RNR Command`,
  `RNR Response`, `REJ`, `SREJ`, `SABM`, `SABME`, `DISC`, `UA`, `DM`,
  `FRMR`, `I` (numbered information)
- Indications to upper layer: `DL-CONNECT Indication`, `DL-DISCONNECT
  Indication`, `DL-DATA Indication`, `DL-ERROR Indication (X)` where `X`
  is the error code

For RR/RNR/REJ/SREJ in particular, whether the frame is sent as a
**command** or a **response** matters (it sets the C/R bit in the address
field). The SDL always disambiguates.

### 4. Task

Plain rectangle, **no notch, no tab, no special edges**:

```
 ╭───────────╮
 │ V(S) ←    │
 │ V(S) + 1  │
 ╰───────────╯
```

A **task** is an action with side effects: assignment to a state variable,
setting/clearing a flag, starting/stopping a timer, manipulating a queue.

Examples:

- Assignments: `V(S) ← V(S) + 1`, `RC ← 0`, `N(S) ← V(S)`, `P ← 0`
- Flag operations: `Set Own Receiver Busy`, `Clear Acknowledge Pending`,
  `Set Layer 3 Initiated`
- Timer operations: `Start T1`, `Stop T1`, `Stop T3`, `Restart T1`
- Queue operations: `Push I Frame on Queue`, `Discard I Frame Queue`

### 5. Decision

Diamond. **The question text lives inside the diamond. The answers live
on the arrows leaving it**:

```
        ╱╲
       ╱  ╲
      ╱    ╲
     ╱ Own  ╲
    ╱Receiver╲
    ╲ Busy?  ╱
     ╲      ╱
      ╲    ╱
       ╲  ╱
        ╲╱
       ╱  ╲
    No╱    ╲Yes
     ╱      ╲
```

A **decision** is a branch on a boolean condition. Crucial details:

- The text inside the diamond is the **question**, not the answer.
- Each outgoing arrow has a **label** — typically `Yes`/`No`, sometimes
  `True`/`False`, occasionally an arithmetic comparison result.
- Read shapes connected to each branch arrow as the *body* of that branch.

When you're transcribing a decision into our YAML DSL, every outgoing arm
becomes a separate transition with a mutually-exclusive guard. Compound
decisions (one diamond feeding into another) become AND-ed guards.

### 6. Procedure call

Rectangle with **vertical bars on each side** (looks like a rectangle with
a thinner rectangle on either edge):

```
 ║───────────║
 ║ Establish ║
 ║ Data Link ║
 ║───────────║
```

A **procedure call** is a named subroutine defined elsewhere in the spec
(usually in the appendix as `Procedure X.Y.Z`). For our purposes treat it
as a single opaque action — encode it in the YAML as a single
`actions:` entry by name, and let the orchestrator expand it.

Common ones in AX.25:

- `Establish Data Link` — used in the Awaiting Connection chain
- `Establish Extended Data Link` — same for v2.2 / mod-128
- `Nr Error Recovery` — handles bad N(R) values
- `Transmit Enquiry` — sends RR/RNR with P=1
- `Check I Frame Acked`
- `Invoke Retransmission`
- `Select T1 Value`

### 7. Connector

Small circle with a letter or number inside (e.g. ⓐ or ①). A **connector**
is a labelled jump point — control flows from one connector to another
that carries the same label, used to avoid drawing long crossing arrows.

Treat connectors as "see also" markers. The transition continues at the
matching connector elsewhere on the page or on another page (e.g. fig
C4.4a connects into fig C4.4b via labelled circles).

## How a transition chain reads

A typical chain in an AX.25 SDL reads top-to-bottom, like so:

```
   [State]          ← we sit here
       │
   ⟩[Input]         ← this is what woke us up
       │
   ⟨Decision?⟩      ← maybe a branch
   No│      │Yes
     │      │
  [Task]  [Task]    ← actions on each branch
     │      │
  [Out⟩  [Out⟩      ← frames or primitives we emit
     │      │
   [State][State]   ← where we end up
```

When encoding into our YAML DSL, each chain from start-state through
inputs/decisions/tasks/outputs to end-state becomes **one transition
entry**. A decision with N outgoing branches becomes N entries on the
same input, each carrying a mutually-exclusive guard.

## When the figure surprises you

The figures are the source of truth. Several of the AX.25 transitions are
counter-intuitive on first read — especially around busy-state handling
and the timer-recovery state — and most of those surprises are deliberate.
The spec author's notes describe these as having "taken quite a bit of
study to understand" even for experienced practitioners.

**Encode what the figure shows, even if it surprises you.** Do not flip a
branch label, swap a Yes/No, or substitute a "correct-looking" action just
because the figure looks wrong to you. The figure is almost certainly
right and you (or I, or any of the agents helping with this) are almost
certainly mis-reading the SDL convention, mis-inferring the semantics of
the variables, or applying intuition from a different protocol.

When you do encounter genuine surprise, the workflow is:

1. **Re-read the shapes.** Look at this primer's seven shapes and confirm
   you've identified each box correctly. Most "spec bugs" are
   shape-confusion in disguise.
2. **Transcribe faithfully.** Encode what the figure literally shows.
3. **Flag for review.** Add a `verification_pending:` block in the
   transition's `notes:` that captures *what* surprised you, *why*, and
   the alternative interpretation you considered. The PR review picks it
   up; the upstream maintainers can confirm.
4. **Only after explicit upstream confirmation of a spec bug** would we
   consider deviating from the figure. That is the rare path, not the
   default.

We need our implementation to work against real LinBPQ / Xrouter peers,
*and* those peers were themselves built against this spec, so faithful
transcription is the path to interoperability — not "fixing" the figures.

[upstream]: https://github.com/packethacking/ax25spec/issues

## Where this primer is invoked

- `CONTRIBUTING.md` references this file under "Working with SDL diagrams".
- `docs/adr/0001-sdl-dsl.md` references it under "Worked example".
- Every SDL transcription PR description should link here.
