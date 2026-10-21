type Connection = { start: () => Promise<void>; stop: () => Promise<void> };

// SignalR can be connecting or reconnecting when React disposes the effect.
// Stop unconditionally, suppress callbacks immediately, and stop once more if
// an in-flight start resolves after disposal.
export function openConnection(
  build: (isActive: () => boolean) => { connection: Connection },
  reportError: (error: unknown) => void,
  onConnected: () => void = () => {},
): () => void {
  let active = true;
  const { connection } = build(() => active);
  const stop = () => connection.stop().catch(() => undefined);
  void connection.start()
    .then(() => { if (!active) return stop(); onConnected(); })
    .catch((error: unknown) => { if (active) reportError(error); });
  return () => {
    if (!active) return;
    active = false;
    void stop();
  };
}
