import type { ActivityClient } from "../../shared/telemetry/activity";

type Connection = {
  connectionId: string | null;
  onreconnecting: (callback: () => void) => void;
  onreconnected: (callback: (id?: string) => void) => void;
  onclose: (callback: () => void) => void;
};

export function observeConnectionActivity(connection: Connection, isActive: () => boolean, activity?: ActivityClient) {
  let connectionId: string | undefined;
  connection.onreconnecting(() => {
    if (isActive()) activity?.track("ConnectionReconnecting", { connectionId });
  });
  connection.onreconnected((id) => {
    if (!isActive()) return;
    connectionId = id;
    activity?.track("ConnectionReconnected", { connectionId });
    activity?.flush();
  });
  connection.onclose(() => {
    if (isActive()) activity?.track("ConnectionClosed", { connectionId });
  });
  return {
    connected() {
      if (!isActive()) return;
      connectionId = connection.connectionId ?? undefined;
      activity?.track("ConnectionConnected", { connectionId });
    },
    failed() { if (isActive()) activity?.track("ConnectionFailed"); },
  };
}
