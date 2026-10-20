"use client";

import { DriverHome } from "@/features/driver/DriverHome";
import { RequireUser } from "@/features/identity/RequireUser";

export default function DriverPage() {
  return <RequireUser>{(userId) => <DriverHome userId={userId} />}</RequireUser>;
}
