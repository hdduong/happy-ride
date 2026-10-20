export const USER_STORAGE_KEY = "happy-ride-user";
const CITY_STORAGE_KEY = "happy-ride-city";

export function isDriverRole(role: string): boolean {
  return role.toLowerCase() === "driver";
}

export function homeForRole(role: string): "/rider" | "/driver" {
  return isDriverRole(role) ? "/driver" : "/rider";
}

export function rememberUser(userId: string): void {
  const previous = sessionStorage.getItem(USER_STORAGE_KEY);
  sessionStorage.setItem(USER_STORAGE_KEY, userId);
  if (previous && previous !== userId) sessionStorage.removeItem(CITY_STORAGE_KEY);
}

export function rememberPersona(userId: string, city: string): void {
  sessionStorage.setItem(USER_STORAGE_KEY, userId);
  sessionStorage.setItem(CITY_STORAGE_KEY, city);
}

export function storedPersona(): { id: string; city: string } | null {
  try {
    const id = sessionStorage.getItem(USER_STORAGE_KEY);
    const city = sessionStorage.getItem(CITY_STORAGE_KEY);
    if (!id || !city) return null;
    return { id, city };
  } catch {
    return null;
  }
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
