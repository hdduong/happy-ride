"use client";

import { DriverHome } from "@/components/DriverHome";
import { RequireUser } from "@/components/RequireUser";

export default function DriverPage() {
  return <RequireUser>{(userId) => <DriverHome userId={userId} />}</RequireUser>;
}
