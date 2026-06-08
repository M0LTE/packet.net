# A minimal container for the self-contained Packet.Node host (the `app/` publish
# output produced by: dotnet publish src/Packet.Node -c Release -r linux-x64
# --self-contained true -o docker/inp3lab/app). Globalization is run in invariant
# mode so no libicu is needed; libssl3 + ca-certs cover the runtime's lazy TLS link.
FROM debian:stable-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends libssl3 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    PACKETNET_CONFIG=/config/packetnet.yaml \
    PACKETNET_DB=/tmp/pdn.db

WORKDIR /app
COPY app/ /app/
RUN chmod +x /app/packetnet

ENTRYPOINT ["/app/packetnet"]
