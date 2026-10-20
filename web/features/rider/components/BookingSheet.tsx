import { formatKm, formatMinutes, formatVnd, minutesFromSeconds, shortPlace } from "@/shared/format";
import { ErrorLine, PrimaryButton, TextButton } from "@/shared/ui";
import { AssignedSheet } from "./AssignedSheet";
import { SearchSheet } from "./SearchSheet";
import { BikeIcon } from "./BikeIcon";
import type { RiderBooking } from "../useRiderBooking";
import type { DriverLocation } from "@/features/rides/types";

export function BookingSheet({ booking, driverLive }: { booking: RiderBooking; driverLive: DriverLocation | null }) {
  const {
    query, results, searching, quote, destination, ride, error, busy, quoting,
    homeReady, home, cityError, choosePlace, book, onCancel, onConfirm,
    resetToSearch, changeQuery, changeDestination, retryCity,
  } = booking;
  let sheet = !homeReady && cityError ? (
    <div className="stack">
      <h1>Can't load your city</h1>
      <ErrorLine message={cityError} />
      <PrimaryButton
        onClick={retryCity}
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
      onQuery={changeQuery}
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
          onClick={changeDestination}
        >
          Change destination
        </TextButton>
      </div>
    );
  }

  return sheet;
}
