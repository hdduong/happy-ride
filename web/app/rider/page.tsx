"use client";

import { RequireUser } from "@/features/identity/RequireUser";
import { RiderHome } from "@/features/rider/RiderHome";

export default function RiderPage() {
  return <RequireUser>{(userId) => <RiderHome userId={userId} />}</RequireUser>;
}
