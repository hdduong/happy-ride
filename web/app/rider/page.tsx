"use client";

import { RequireUser } from "@/components/RequireUser";
import { RiderHome } from "@/components/RiderHome";

export default function RiderPage() {
  return <RequireUser>{(userId) => <RiderHome userId={userId} />}</RequireUser>;
}
