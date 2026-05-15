/**
 * Quick-start example from README.md — used as a `tsc --noEmit` check that
 * the public API matches the documented surface.
 *
 * This file is not part of the published bundle. It will NOT run in Node
 * (no `navigator.serial`), but it does typecheck and is the canonical
 * shape of consumer code.
 */
import {
  Ax25Stack,
  Callsign,
  WebSerialKissTransport,
} from "../src/index.js";

declare const navigator: {
  serial: {
    requestPort(options?: object): Promise<import("../src/webserial-transport.js").WebSerialLikePort>;
  };
};

async function main(): Promise<void> {
  const port = await navigator.serial.requestPort();
  const transport = new WebSerialKissTransport(port, { baudRate: 9600 });
  const stack = new Ax25Stack(transport);
  await stack.start();

  const session = await stack.connect({
    from: Callsign.parse("M0LTE-2"),
    to: "GB7CIP",
  });
  session.onData((chunk) => {
    console.log(new TextDecoder().decode(chunk));
  });
  session.onDisconnected(() => {
    console.log("link closed");
  });

  await session.write(new TextEncoder().encode("hello\r"));

  // Later, after the user clicks "disconnect":
  await session.disconnect();
  await stack.stop();
}

void main;
