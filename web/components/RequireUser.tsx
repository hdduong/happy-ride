"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { captureUserId } from "@/lib/user";

export function RequireUser({ children }: { children: (userId: string) => ReactNode }) {
  const router = useRouter();
  const [userId, setUserId] = useState<string | null>(null);

  useEffect(() => {
    const id = captureUserId();
    if (!id) {
      router.replace("/");
      return;
    }
    setUserId(id);
  }, [router]);

  if (!userId) return <main className="stage" />;
  return children(userId);
}
