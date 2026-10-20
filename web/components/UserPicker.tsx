"use client";

import { getUsers, messageFrom } from "@/lib/api";
import type { DevUser } from "@/lib/types";
import { forcedUserId, homeForRole, isDriverRole, rememberUser } from "@/lib/user";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

export function UserPicker() {
  const router = useRouter();
  const [users, setUsers] = useState<DevUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const forced = forcedUserId();
    let cancel = false;
    getUsers(forced)
      .then((list) => {
        if (cancel) return;
        setUsers(list);
        if (!forced) return;
        const match = list.find((user) => user.id === forced);
        if (match) router.replace(homeForRole(match.role));
      })
      .catch((reason: unknown) => {
        if (!cancel) setError(messageFrom(reason));
      })
      .finally(() => {
        if (!cancel) setLoading(false);
      });
    return () => {
      cancel = true;
    };
  }, [router]);

  function choose(user: DevUser) {
    rememberUser(user.id);
    router.push(homeForRole(user.role));
  }

  const groups = groupByCity(users);

  return (
    <main className="screen">
      <div className="screen-inner">
        <h1>Happy Ride</h1>
        <p className="lead">Choose a rider or a driver</p>
        {loading ? <p className="muted">Loading people…</p> : null}
        {error ? (
          <p className="sheet-error" role="alert">
            {error}
          </p>
        ) : null}
        {!loading && users.length === 0 && !error ? (
          <p className="muted">No riders or drivers are available.</p>
        ) : null}
        {groups.map((group) => (
          <section key={group.city}>
            <h2 className="city" translate="no">
              {group.city}
            </h2>
            {group.users.map((user) => (
              <button
                key={user.id}
                type="button"
                className="user-row"
                data-testid="user"
                data-userid={user.id}
                onClick={() => choose(user)}
              >
                <span className="user-copy">
                  <span className="user-name" translate="no">
                    {user.name}
                  </span>
                  <span className="user-meta" translate="no">
                    {roleLabel(user)}
                  </span>
                </span>
              </button>
            ))}
          </section>
        ))}
      </div>
    </main>
  );
}

function roleLabel(user: DevUser): string {
  if (!isDriverRole(user.role)) return "Rider";
  return user.plate ? `Driver \u00b7 ${user.plate}` : "Driver";
}

function groupByCity(users: DevUser[]): Array<{ city: string; users: DevUser[] }> {
  const groups: Array<{ city: string; users: DevUser[] }> = [];
  for (const user of users) {
    const existing = groups.find((group) => group.city === user.city);
    if (existing) existing.users.push(user);
    else groups.push({ city: user.city, users: [user] });
  }
  return groups;
}
