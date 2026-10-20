"use client";

import { useEffect, useRef, useState } from "react";
import { autocomplete, createQuote, getPlace } from "@/features/maps/api";
import { cancelRide, confirmArrival, createRide, getRide } from "@/features/rides/api";
import { getUsers } from "@/features/identity/api";
import { rememberPersona, storedPersona } from "@/features/identity/storage";
import { pickupForCity } from "@/features/maps/geo";
import { useRideSession } from "@/features/rides/useRideSession";
import { messageFrom } from "@/shared/api/client";
import type { Place, Quote } from "@/features/maps/types";
import type { RideView } from "@/features/rides/types";

export function useRiderBooking(userId: string) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<Place[]>([]);
  const [searching, setSearching] = useState(false);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [destination, setDestination] = useState<Place | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [quoting, setQuoting] = useState(false);
  const [homeReady, setHomeReady] = useState(false);
  const [home, setHome] = useState(() => pickupForCity(undefined));
  const [cityError, setCityError] = useState<string | null>(null);
  const [cityAttempt, setCityAttempt] = useState(0);
  const searchSeq = useRef(0);
  const { ride, live, session } = useRideSession(userId, "rider");
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
    if (!ride) return;
    searchSeq.current += 1;
    setQuote(null);
    setDestination(null);
    setQuoting(false);
  }, [ride]);

  useEffect(() => {
    if (!allowSearch) return;
    const seq = ++searchSeq.current;
    setResults([]);
    const text = query.trim();
    if (!text) {
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
      await session.command(async () => {
        const rideId = await createRide(userId, currentQuote.quoteId);
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
        session.receiveRide(next ?? fallback);
      });
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
      await session.command(async () => {
        const updated = await cancelRide(userId, current.rideId);
        session.receiveRide(updated ?? { ...current, status: "Cancelled" });
      });
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
      await session.command(async () => {
        const updated = await confirmArrival(userId, current.rideId);
        session.receiveRide(updated ?? { ...current, status: "Completed" });
      });
    } catch (reason: unknown) {
      setError(messageFrom(reason));
    } finally {
      setBusy(false);
    }
  }

  function resetToSearch() {
    session.dismissRide();
    setQuote(null);
    setDestination(null);
    setQuery("");
    setResults([]);
    setError(null);
  }

  function changeQuery(value: string) {
    searchSeq.current += 1;
    setQuoting(false);
    setQuery(value);
    setError(null);
    setSearching(value.trim().length > 0);
  }

  function changeDestination() {
    setQuote(null);
    setDestination(null);
    setError(null);
  }

  function retryCity() {
    setCityError(null);
    setCityAttempt((attempt) => attempt + 1);
  }

  return {
    query, results, searching, quote, destination, ride, live, error, busy,
    quoting, homeReady, home, cityError, choosePlace, book, onCancel,
    onConfirm, resetToSearch, changeQuery, changeDestination, retryCity,
  };
}

export type RiderBooking = ReturnType<typeof useRiderBooking>;
