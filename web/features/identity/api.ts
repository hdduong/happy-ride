import { request } from "@/shared/api/client";
import { asUsers } from "./parse";
import type { DevUser } from "./types";

export function getUsers(userId?: string | null): Promise<DevUser[]> {
  return request<unknown>("/api/dev/users", { userId }).then((payload) => asUsers(payload));
}
