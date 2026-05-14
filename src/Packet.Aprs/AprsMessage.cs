namespace Packet.Aprs;

/// <summary>
/// A decoded APRS message (DTI <c>:</c>) per APRS101 §14.
/// </summary>
/// <remarks>
/// <para>
/// Covers all the on-wire forms that share the message envelope:
/// person-to-person messages, bulletins (addressee starts with
/// <c>BLN</c>), announcements, telemetry parameter definitions
/// (<c>PARM.</c> / <c>UNIT.</c> / <c>EQNS.</c> / <c>BITS.</c> from
/// §13), acks (<c>ackNNN</c>) and rejects (<c>rejNNN</c>). The
/// decoder doesn't interpret the body; it surfaces the addressee,
/// the text, and any trailing message-ID, and lets higher-layer
/// callers route by content.
/// </para>
/// </remarks>
/// <param name="Addressee">
/// 9-character right-space-padded addressee callsign field as it
/// appears on the wire (trailing spaces stripped here for caller
/// convenience).
/// </param>
/// <param name="Text">
/// Message body. For person-to-person messages this is free text;
/// for telemetry definition messages this is e.g.
/// <c>"PARM.RxTraffic,TxTraffic,..."</c>; for acks/rejects this is
/// e.g. <c>"ackNNN"</c> / <c>"rejNNN"</c>. The trailing
/// <c>{messageId}</c> suffix (if present) is parsed out and exposed
/// separately in <see cref="MessageId"/>.
/// </param>
/// <param name="MessageId">
/// Sender-chosen message identifier from a trailing <c>{NNN…}</c>
/// suffix, used for retransmission tracking. <c>null</c> if no
/// suffix was present. Up to 5 chars per §14.
/// </param>
public readonly record struct AprsMessage(
    string Addressee,
    string Text,
    string? MessageId);
