"use client";

import { getUsers, returnHome } from "./api";
import { messageFrom } from "@/shared/api/client";
import type { DevUser } from "./types";
import { forcedUserId, homeForRole, isDriverRole, rememberPersona } from "./storage";
import { MapCanvas } from "@/features/maps/MapCanvas";
import { HANOI_CENTER } from "@/features/maps/geo";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

const cityOrder = ["Hanoi", "HCMC"];

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
        if (match) {
          void openAtHome(match).then(() => {
            if (!cancel) router.replace(homeForRole(match.role));
          });
        }
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
    void openAtHome(user).then(() => router.push(homeForRole(user.role)));
  }

  const cities = citiesInOrder(users);

  return (
    <main className="screen">
      <div className="screen-inner picker">
        <h1>Happy Ride</h1>
        <p className="lead">Choose a rider or a driver</p>
        <div className="picker-map" data-testid="hanoi-map">
          <MapCanvas center={HANOI_CENTER} zoom={12} />
        </div>
        {loading ? <p className="muted">Loading people…</p> : null}
        {error ? (
          <p className="sheet-error" role="alert">
            {error}
          </p>
        ) : null}
        {!loading && users.length === 0 && !error ? (
          <p className="muted">No riders or drivers are available.</p>
        ) : null}
        <div className="persona-columns">
          <section aria-label="Riders" data-testid="rider-column">
            <h2>Riders</h2>
            {cities.map((city) => (
              <CityPeople
                key={city}
                city={city}
                users={users.filter((user) => user.city === city && !isDriverRole(user.role))}
                onChoose={choose}
              />
            ))}
          </section>
          <section aria-label="Drivers" data-testid="driver-column">
            <h2>Drivers</h2>
            {cities.map((city) => (
              <CityPeople
                key={city}
                city={city}
                users={users.filter((user) => user.city === city && isDriverRole(user.role))}
                onChoose={choose}
              />
            ))}
          </section>
        </div>
      </div>
    </main>
  );
}

function CityPeople({
  city,
  users,
  onChoose,
}: {
  city: string;
  users: DevUser[];
  onChoose: (user: DevUser) => void;
}) {
  if (users.length === 0) return null;
  return (
    <div>
      <h3 className="city" translate="no">
        {cityLabel(city)}
      </h3>
      {users.map((user) => (
        <button
          key={user.id}
          type="button"
          className="user-row"
          data-testid="user"
          data-userid={user.id}
          onClick={() => onChoose(user)}
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
    </div>
  );
}

async function openAtHome(user: DevUser) {
  rememberPersona(user.id, user.city);
  try {
    await returnHome(user.id);
  } catch {
    // The screen still opens at the last saved place if the reset cannot be saved.
  }
}

function roleLabel(user: DevUser): string {
  if (!isDriverRole(user.role)) return "Rider";
  return user.plate ? `Driver \u00b7 ${user.plate}` : "Driver";
}

function cityLabel(city: string): string {
  return city === "HCMC" ? "Ho Chi Minh City" : city;
}

function citiesInOrder(users: DevUser[]): string[] {
  const present = new Set(users.map((user) => user.city));
  const ordered = cityOrder.filter((city) => present.has(city));
  for (const city of present) {
    if (!ordered.includes(city)) ordered.push(city);
  }
  return ordered;
}
