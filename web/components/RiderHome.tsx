"use client";

import { useEffect, useRef, useState } from "react";
import {
  autocomplete,
  cancelRide,
  confirmArrival,
  createQuote,
  createRide,
  getCurrentRide,
  getPlace,
  getRide,
  getUsers,
  messageFrom,
} from "@/lib/api";
import { formatEta, formatKm, formatMinutes, formatVnd, initials, minutesFromSeconds, shortPlace } from "@/lib/format";
import { bearing, haversineMeters, isPlotted, linePoints, pickupForCity } from "@/lib/geo";
import { rememberPersona, storedPersona } from "@/lib/user";
import { openRideConnection } from "@/lib/signalr";
import { applyRiderUpdate, preferForward, withEpoch } from "@/lib/rides";
import type { DriverLocation, Place, Quote, RideView } from "@/lib/types";
import { MapCanvas, type MapMarker } from "@/components/MapCanvas";
import { ErrorLine, PrimaryButton, Stage, TextButton } from "@/components/ui";

export function RiderHome({ userId }: { userId: string }) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<Place[]>([]);
  const [searching, setSearching] = useState(false);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [destination, setDestination] = useState<Place | null>(null);
  const [ride, setRide] = useState<RideView | null>(null);
  const [live, setLive] = useState<DriverLocation | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [quoting, setQuoting] = useState(false);
  const [homeReady, setHomeReady] = useState(false);
  const [home, setHome] = useState(() => pickupForCity(undefined));
  const [cityError, setCityError] = useState<string | null>(null);
  const [cityAttempt, setCityAttempt] = useState(0);
  const searchSeq = useRef(0);
  const epoch = useRef(0);
  const ignored = useRef(new Set<string>());
  const allowSearch = !ride && homeReady;

  useEffect(() => {
    let cancel = false;
    const cached = storedPersona();
    if (cached?.id === userId) {
      setHome(pickupForCity(cached.city));
      setHomeReady(true);
    }
    getUsers(userId)
      .then((users) => {
        if (cancel) return;
        const person = users.find((user) => user.id === userId);
        if (!person) return;
        rememberPersona(person.id, person.city);
        setHome(pickupForCity(person.city));
        setCityError(null);
        setHomeReady(true);
      })
      .catch((reason: unknown) => {
        if (!cancel && !homeReady) setCityError(messageFrom(reason));
      });
    return () => {
      cancel = true;
    };
  }, [homeReady, userId, cityAttempt]);

  useEffect(() => {
    return openRideConnection(userId, {
      onRide: (incoming) => {
        if (ignored.current.has(incoming.rideId)) return;
        setRide((previous) => preferForward(previous, incoming));
        setQuote(null);
        setDestination(null);
      },
      onLocation: (location) => setLive(location),
    });
  }, [userId]);

  useEffect(() => {
    let stop = false;
    const tick = async () => {
      const stamp = epoch.current;
      try {
        const incoming = await getCurrentRide(userId);
        if (stop || stamp !== epoch.current) return;
        const hidden = incoming !== null && ignored.current.has(incoming.rideId);
        setRide((previous) => applyRiderUpdate(previous, hidden ? null : incoming, ignored.current));
        if (!hidden && incoming) {
          setQuote(null);
          setDestination(null);
        }
      } catch {
        // Polling is the backup when a hub event is missed.
      }
    };
    void tick();
    const id = window.setInterval(() => void tick(), 2000);
    return () => {
      stop = true;
      window.clearInterval(id);
    };
  }, [userId]);

  useEffect(() => {
    if (!allowSearch) return;
    const seq = ++searchSeq.current;
    const text = query.trim();
    if (!text) {
      setResults([]);
      setSearching(false);
      return;
    }
    setSearching(true);
    const handle = window.setTimeout(() => {
      void autocomplete(userId, text, home.lat, home.lng)
        .then((places) => {
          if (searchSeq.current === seq) setResults(places);
        })
        .catch((reason: unknown) => {
          if (searchSeq.current !== seq) return;
          setResults([]);
          setError(messageFrom(reason));
        })
        .finally(() => {
          if (searchSeq.current === seq) setSearching(false);
        });
    }, 300);
    return () => window.clearTimeout(handle);
  }, [allowSearch, home.lat, home.lng, query, userId]);

  async function choosePlace(suggestion: Place) {
    if (!homeReady) return;
    const seq = ++searchSeq.current;
    setError(null);
    setQuoting(true);
    setQuote(null);
    try {
      const place = await getPlace(userId, suggestion.id);
      if (searchSeq.current !== seq) return;
      const nextQuote = await createQuote(
        userId,
        { lat: home.lat, lng: home.lng, address: home.name },
        { lat: place.lat, lng: place.lng, address: place.name || place.address },
      );
      if (searchSeq.current !== seq) return;
      setDestination(place);
      setQuote(nextQuote);
      setQuery("");
      setResults([]);
    } catch (reason: unknown) {
      if (searchSeq.current === seq) setError(messageFrom(reason));
    } finally {
      if (searchSeq.current === seq) setQuoting(false);
    }
  }

  async function book() {
    if (!quote || !destination) return;
    const currentQuote = quote;
    const currentDestination = destination;
    setBusy(true);
    setError(null);
    try {
      const rideId = await withEpoch(epoch, () => createRide(userId, currentQuote.quoteId));
      let next: RideView | null = null;
      try {
        next = await getRide(userId, rideId);
      } catch {
        next = null;
      }
      const fallback: RideView = {
        rideId,
        status: "Searching",
        pickup: { lat: home.lat, lng: home.lng, address: home.name },
        round: 1,
        dropoff: {
          lat: currentDestination.lat,
          lng: currentDestination.lng,
          address: currentDestination.name || currentDestination.address,
        },
        distanceM: currentQuote.distanceM,
        durationS: currentQuote.durationS,
        fareVnd: currentQuote.fareVnd,
        route: currentQuote.route,
        driver: null,
      };
      setQuote(null);
      setRide((previous) => preferForward(previous, next ?? fallback));
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  async function onCancel() {
    if (!ride) return;
    const current = ride;
    setBusy(true);
    setError(null);
    try {
      const updated = await withEpoch(epoch, () => cancelRide(userId, current.rideId));
      setRide((previous) => preferForward(previous, updated ?? { ...current, status: "Cancelled" }));
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  async function onConfirm() {
    if (!ride) return;
    const current = ride;
    setBusy(true);
    setError(null);
    try {
      const updated = await withEpoch(epoch, () => confirmArrival(userId, current.rideId));
      setRide((previous) => preferForward(previous, updated ?? { ...current, status: "Completed" }));
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  function resetToSearch() {
    if (ride) ignored.current.add(ride.rideId);
    epoch.current += 1;
    setRide(null);
    setQuote(null);
    setDestination(null);
    setQuery("");
    setResults([]);
    setError(null);
    setLive(null);
  }

  const pickup = ride?.pickup ?? {
    lat: home.lat,
    lng: home.lng,
    address: home.name,
  };
  const dropoff =
    ride?.dropoff ??
    (destination
      ? { lat: destination.lat, lng: destination.lng, address: destination.name || destination.address }
      : null);
  const quotedLine = ride ? linePoints(ride.route) : quote ? linePoints(quote.route) : [];
  const route =
    quotedLine.length >= 2 ? quotedLine : dropoff && (ride || quote) && isPlotted(dropoff) ? [pickup, dropoff] : [];
  const driverLive = live && ride && live.rideId === ride.rideId ? live : null;
  const driverPoint = driverLive
    ? { lat: driverLive.lat, lng: driverLive.lng }
    : ride?.driver
      ? { lat: ride.driver.lat, lng: ride.driver.lng }
      : null;
  const headingTarget =
    ride && (ride.status === "InProgress" || ride.status === "DroppedOff" || ride.status === "Completed")
      ? ride.dropoff
      : pickup;
  const heading =
    driverLive?.heading != null ? driverLive.heading : driverPoint ? bearing(driverPoint, headingTarget) : 0;
  const showBike = Boolean(
    driverPoint &&
      ride &&
      ride.status !== "Searching" &&
      ride.status !== "NoDriver" &&
      ride.status !== "Cancelled",
  );
  const markers: MapMarker[] = [];
  if (isPlotted(pickup)) markers.push({ ...pickup, kind: "pickup" });
  if (dropoff && isPlotted(dropoff) && (ride || quote)) markers.push({ ...dropoff, kind: "dropoff" });
  if (showBike && driverPoint && isPlotted(driverPoint)) {
    markers.push({ ...driverPoint, kind: "bike", heading });
  }

  let sheet = !homeReady && cityError ? (
    <div className="stack">
      <h1>Can't load your city</h1>
      <ErrorLine message={cityError} />
      <PrimaryButton
        onClick={() => {
          setCityError(null);
          setCityAttempt((attempt) => attempt + 1);
        }}
      >
        Retry
      </PrimaryButton>
    </div>
  ) : (
    <SearchSheet
      pickupName={home.name}
      ready={homeReady}
      query={query}
      results={results}
      searching={searching}
      quoting={quoting}
      error={error}
      onQuery={(value) => {
        searchSeq.current += 1;
        setQuoting(false);
        setQuery(value);
        setError(null);
        setSearching(value.trim().length > 0);
      }}
      onChoose={(place) => void choosePlace(place)}
    />
  );

  if (ride?.status === "Completed") {
    sheet = (
      <div className="stack">
        <h1 data-testid="ride-complete">Ride complete</h1>
        <p className="muted">
          {formatKm(ride.distanceM)} {"\u00b7"} {formatMinutes(ride.durationS)} {"\u00b7"}{" "}
          <span translate="no">
            {shortPlace(ride.pickup.address)} {"\u2192"} {shortPlace(ride.dropoff.address)}
          </span>
        </p>
        <PrimaryButton onClick={resetToSearch}>Book another ride</PrimaryButton>
      </div>
    );
  } else if (ride?.status === "NoDriver") {
    sheet = (
      <div className="stack">
        <h1>No driver nearby</h1>
        <p className="muted">No driver was free within 3 km.</p>
        <ErrorLine message={error} />
        <PrimaryButton onClick={resetToSearch}>Search again</PrimaryButton>
      </div>
    );
  } else if (ride?.status === "Cancelled") {
    sheet = (
      <div className="stack">
        <h1>Ride cancelled</h1>
        <ErrorLine message={error} />
        <PrimaryButton onClick={resetToSearch}>Book another ride</PrimaryButton>
      </div>
    );
  } else if (ride?.status === "DroppedOff") {
    sheet = (
      <div className="stack">
        <h1>Have you arrived?</h1>
        <p className="muted" translate="no">
          {shortPlace(ride.dropoff.address)}
        </p>
        <p className="muted">Driver confirmed drop-off</p>
        <ErrorLine message={error} />
        <PrimaryButton data-testid="confirm-arrival" disabled={busy} onClick={() => void onConfirm()}>
          Confirm arrival
        </PrimaryButton>
      </div>
    );
  } else if (ride && (ride.status === "Accepted" || ride.status === "DriverArrived" || ride.status === "InProgress")) {
    sheet = (
      <AssignedSheet
        ride={ride}
        live={driverLive}
        error={error}
        busy={busy}
        onCancel={() => void onCancel()}
      />
    );
  } else if (ride?.status === "Searching") {
    sheet = (
      <div className="stack">
        <h1 data-testid="finding-driver">Finding a driver</h1>
        <p className="muted">Round {ride.round || 1} of 3 · within 3 km</p>
        <ErrorLine message={error} />
        <TextButton data-testid="cancel-ride" disabled={busy} onClick={() => void onCancel()}>
          Cancel ride
        </TextButton>
      </div>
    );
  } else if (quote && destination) {
    const minutes = minutesFromSeconds(quote.durationS);
    sheet = (
      <div className="stack">
        <p className="route-line" translate="no">
          {home.name} {"\u2192"} {destination.name}
        </p>
        <p className="muted">
          {formatKm(quote.distanceM)} {"\u00b7"} {formatMinutes(quote.durationS)}
        </p>
        <div className="service">
          <BikeIcon />
          <div className="service-copy">
            <div className="strong">Motorbike</div>
            <div className="muted">Pickup in {minutes} min</div>
          </div>
          <div className="service-fare">
            <div className="strong">{formatVnd(quote.fareVnd)}</div>
            <div className="muted">Estimate</div>
          </div>
        </div>
        <ErrorLine message={error} />
        <PrimaryButton data-testid="book-motorbike" disabled={busy} onClick={() => void book()}>
          Book motorbike
        </PrimaryButton>
        <TextButton
          onClick={() => {
            setQuote(null);
            setDestination(null);
            setError(null);
          }}
        >
          Change destination
        </TextButton>
      </div>
    );
  }

  return (
    <Stage
      map={
        <MapCanvas
          center={pickup}
          route={route}
          markers={markers}
          pulse={ride?.status === "Searching" ? pickup : null}
        />
      }
      sheet={sheet}
    />
  );
}

function AssignedSheet({
  ride,
  live,
  error,
  busy,
  onCancel,
}: {
  ride: RideView;
  live: DriverLocation | null;
  error: string | null;
  busy: boolean;
  onCancel: () => void;
}) {
  const driver = ride.driver;
  const title =
    ride.status === "Accepted"
      ? "Driver is on the way"
      : ride.status === "DriverArrived"
        ? "Driver has arrived"
        : `Heading to ${shortPlace(ride.dropoff.address)}`;
  const eta = riderEta(ride, live);
  return (
    <div className="stack">
      {driver ? (
        <section className="stack" data-testid="driver-card">
          <div className="title-row">
            <h1>{title}</h1>
            <div className="eta">{eta}</div>
          </div>
          <div className="who">
            <div className="avatar" aria-hidden="true">
              {initials(driver.name)}
            </div>
            <div>
              <div className="strong" translate="no">
                {driver.name}
              </div>
              <div className="muted">Motorbike</div>
            </div>
          </div>
          <div className="plate" translate="no">
            {driver.plate}
          </div>
        </section>
      ) : (
        <div className="title-row">
          <h1>{title}</h1>
          <div className="eta">{eta}</div>
        </div>
      )}
      <ErrorLine message={error} />
      {ride.status === "Accepted" || ride.status === "DriverArrived" ? (
        <TextButton data-testid="cancel-ride" disabled={busy} onClick={onCancel}>
          Cancel ride
        </TextButton>
      ) : null}
    </div>
  );
}

function riderEta(ride: RideView, live: DriverLocation | null): string {
  if (ride.status === "DriverArrived") return "Waiting";
  const position = live ?? (ride.driver ? { lat: ride.driver.lat, lng: ride.driver.lng } : null);
  if (!position) return formatMinutes(ride.durationS);
  const target = ride.status === "InProgress" ? ride.dropoff : ride.pickup;
  const speed = live?.speedKmh != null && live.speedKmh >= 5 ? live.speedKmh : 20;
  return formatEta(haversineMeters(position, target), speed);
}

function SearchSheet({
  pickupName,
  ready,
  query,
  results,
  searching,
  quoting,
  error,
  onQuery,
  onChoose,
}: {
  pickupName: string;
  ready: boolean;
  query: string;
  results: Place[];
  searching: boolean;
  quoting: boolean;
  error: string | null;
  onQuery: (value: string) => void;
  onChoose: (place: Place) => void;
}) {
  return (
    <div className="stack">
      <div>
        <div className="kicker">Your location</div>
        <div className="strong" translate="no">
          {pickupName}
        </div>
      </div>
      <input
        className="where"
        data-testid="where-to"
        placeholder={ready ? "Where to?" : "Loading your city…"}
        disabled={!ready || quoting}
        aria-label="Where to?"
        value={query}
        autoComplete="off"
        autoCorrect="off"
        spellCheck={false}
        onChange={(event) => onQuery(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter" && results[0] && !quoting) {
            event.preventDefault();
            onChoose(results[0]);
          }
        }}
      />
      {quoting ? <p className="muted">Getting your estimate…</p> : null}
      {searching && results.length === 0 ? <p className="muted">Searching…</p> : null}
      {!searching && query.trim() && results.length === 0 && !quoting ? (
        <p className="muted">No places found</p>
      ) : null}
      <ErrorLine message={error} />
      <div className="results">
        {results.map((place) => (
          <button
            key={place.id}
            type="button"
            className="place-result"
            data-testid="place-result"
            disabled={quoting}
            onClick={() => onChoose(place)}
          >
            <span translate="no">{place.name}</span>
            {place.address && place.address !== place.name ? (
              <span className="muted" translate="no">
                {place.address}
              </span>
            ) : null}
          </button>
        ))}
      </div>
    </div>
  );
}

function BikeIcon() {
  return (
    <svg className="bike-icon" viewBox="0 0 48 48" aria-hidden="true">
      <circle cx="14" cy="32" r="7" fill="none" stroke="currentColor" strokeWidth="2" />
      <circle cx="34" cy="32" r="7" fill="none" stroke="currentColor" strokeWidth="2" />
      <path
        d="M14 32 L22 18 H32 L34 32 M22 18 L28 32"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinejoin="round"
      />
    </svg>
  );
}
