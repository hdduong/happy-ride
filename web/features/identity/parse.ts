import { num, record, str } from "@/shared/api/parse";
import type { DevUser } from "./types";

export function asUsers(value: unknown): DevUser[] {
  if (!Array.isArray(value)) throw new Error("User list was not an array");
  return value.flatMap((item) => {
    const row = record(item);
    if (!row) return [];
    const id = str(row, "id", "Id");
    const name = str(row, "name", "Name");
    const role = str(row, "role", "Role");
    const city = str(row, "city", "City");
    if (!id || !name || !role || !city) return [];
    return [{ id, name, role, city, plate: str(row, "plate", "Plate"), lat: num(row, "lat", "Lat"), lng: num(row, "lng", "Lng") }];
  });
}
