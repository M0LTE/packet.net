# NinoTNC hardware repros

Self-contained scripts intended to be shared upstream with the relevant
hardware / firmware authors. Each is a single file with minimal
dependencies so it can be dropped onto someone else's host without
pulling Packet.NET source.

## `ninotnc_mode12_repro.py`

Reproduction harness for the **mode 12 (300 AFSK AX.25) intermittent
RX-lockup** observed on the back-to-back NinoTNC pair on 2026-05-14.

```sh
pip install pyserial
python3 ninotnc_mode12_repro.py /dev/ttyACM0 /dev/ttyACM1
# or on Windows:
python3 ninotnc_mode12_repro.py COM6 COM8 --txdelays 20,50,100
```

What it shows when the bug fires: at one of the TXDELAY values in the
ladder, one direction transitions from "everything succeeds" to "every
subsequent frame is lost in a contiguous run" — a clear, machine-
checkable pattern. Modes 13 (`300 AFSKPLL IL2P`) and 14
(`300 AFSKPLL IL2P+CRC`) do not exhibit the same behaviour over the
same N=50/100 sample sizes, which points the finger at mode 12's
plain-AFSK demodulator rather than 300-baud air time or AX.25 framing.

Full background and observed data are in
[`docs/nino-tnc-characterisation.md`](../../docs/nino-tnc-characterisation.md)
under the *Mode 12 deep dive* section. The script is independent of
the C# driver and only uses the published KISS protocol — anyone
with two NinoTNCs and pyserial should be able to run it.
