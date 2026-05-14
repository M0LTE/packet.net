using Packet.Kiss;

namespace Packet.Kiss.NinoTnc;

/// <summary>
/// An over-air NinoTNC TX-Test frame heard via our own modem (i.e. some
/// *other* NinoTNC operator pressed their button and we received the
/// resulting UI frame). The synthetic host-side counterpart fires as
/// <see cref="NinoTncTxTestFrameReceivedEvent"/> on the *transmitting*
/// modem; this is what you see on the receiving side of a link.
/// </summary>
public sealed record NinoTncAirTestFrameReceivedEvent(KissFrame Raw, NinoTncAirTestFrame AirTest) : KissInboundEvent(Raw);
