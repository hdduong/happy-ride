export const USER_STORAGE_KEY = "happy-ride-user";

export function isDriverRole(role: string): boolean {
  return role.toLowerCase() === "driver";
}

export function homeForRole(role: string): "/rider" | "/driver" {
  return isDriverRole(role) ? "/driver" : "/rider";
}

export function rememberUser(userId: string): void {
  sessionStorage.setItem(USER_STORAGE_KEY, userId);
}

function queryUserId(): string | null {
  const selected = new URLSearchParams(window.location.search).get("as")?.trim();
  return selected ? selected : null;
}

export function forcedUserId(): string | null {
  try {
    const selected = queryUserId();
    if (!selected) return null;
    rememberUser(selected);
    return selected;
  } catch {
    return null;
  }
}

export function captureUserId(): string | null {
  try {
    const selected = queryUserId();
    if (selected) {
      rememberUser(selected);
      return selected;
    }
    return sessionStorage.getItem(USER_STORAGE_KEY);
  } catch {
    return null;
  }
}
