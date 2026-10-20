import type { Place } from "@/features/maps/types";
import { ErrorLine } from "@/shared/ui";

export function SearchSheet({
  currentName,
  manualPickup,
  editingPickup,
  pickupQuery,
  ready,
  query,
  results,
  searching,
  quoting,
  error,
  onQuery,
  onEditPickup,
  onPickupQuery,
  onClearPickup,
  onUseCurrent,
  onFocusDropoff,
  onChoose,
  onChoosePickup,
}: {
  currentName: string;
  manualPickup: Place | null;
  editingPickup: boolean;
  pickupQuery: string;
  ready: boolean;
  query: string;
  results: Place[];
  searching: boolean;
  quoting: boolean;
  error: string | null;
  onQuery: (value: string) => void;
  onEditPickup: () => void;
  onPickupQuery: (value: string) => void;
  onClearPickup: () => void;
  onUseCurrent: () => void;
  onFocusDropoff: () => void;
  onChoose: (place: Place) => void;
  onChoosePickup: (place: Place) => void;
}) {
  const choosingPickup = editingPickup;
  const showCurrent = choosingPickup;
  const showResults = choosingPickup ? pickupQuery.trim().length > 0 : query.trim().length > 0;

  return (
    <div className="stack">
      <div className="loc-card">
        {choosingPickup ? (
          <div className="loc-stop loc-stop-focus">
            <Mark kind="pickup" />
            <input
              className="loc-input"
              data-testid="pickup-input"
              aria-label="Pickup"
              placeholder="Current location"
              value={pickupQuery}
              disabled={!ready || quoting}
              autoFocus
              autoComplete="off"
              autoCorrect="off"
              spellCheck={false}
              onChange={(event) => onPickupQuery(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter" && results[0] && !quoting) {
                  event.preventDefault();
                  onChoosePickup(results[0]);
                }
              }}
            />
            {pickupQuery ? (
              <button type="button" className="loc-clear" aria-label="Clear pickup" onClick={onClearPickup}>
                ×
              </button>
            ) : null}
          </div>
        ) : (
          <button
            type="button"
            className="loc-stop"
            data-testid="pickup-row"
            disabled={!ready || quoting}
            onClick={onEditPickup}
          >
            <Mark kind="pickup" />
            <span className="loc-copy">
              <span className="strong" translate="no">
                {manualPickup ? manualPickup.name : "Current location"}
              </span>
              <span className="muted" translate="no">
                {manualPickup ? manualPickup.address : currentName}
              </span>
            </span>
          </button>
        )}
        <span className="loc-join" aria-hidden="true" />
        <label className="loc-stop">
          <Mark kind="dropoff" />
          <input
            className="loc-where"
            data-testid="where-to"
            placeholder={ready ? "Where to?" : "Loading your city…"}
            disabled={!ready || quoting}
            aria-label="Where to?"
            value={query}
            autoComplete="off"
            autoCorrect="off"
            spellCheck={false}
            onFocus={onFocusDropoff}
            onChange={(event) => onQuery(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !choosingPickup && results[0] && !quoting) {
                event.preventDefault();
                onChoose(results[0]);
              }
            }}
          />
        </label>
      </div>
      {quoting ? <p className="muted">Getting your estimate…</p> : null}
      {searching && showResults && results.length === 0 ? <p className="muted">Searching…</p> : null}
      {!searching && showResults && results.length === 0 && !quoting ? (
        <p className="muted">No places found</p>
      ) : null}
      <ErrorLine message={error} />
      <div className="results">
        {showCurrent ? (
          <button
            type="button"
            className="place-result loc-current"
            data-testid="use-current-location"
            disabled={quoting}
            onClick={onUseCurrent}
          >
            <span>Current location</span>
            <span className="muted" translate="no">
              {currentName}
            </span>
          </button>
        ) : null}
        {showResults
          ? results.map((place) => (
              <button
                key={place.id}
                type="button"
                className="place-result"
                data-testid="place-result"
                disabled={quoting}
                onClick={() => (choosingPickup ? onChoosePickup(place) : onChoose(place))}
              >
                <span translate="no">{place.name}</span>
                {place.address && place.address !== place.name ? (
                  <span className="muted" translate="no">
                    {place.address}
                  </span>
                ) : null}
              </button>
            ))
          : null}
      </div>
    </div>
  );
}

function Mark({ kind }: { kind: "pickup" | "dropoff" }) {
  return (
    <span className="loc-mark" aria-hidden="true">
      <span className={kind === "pickup" ? "loc-dot" : "loc-square"} />
    </span>
  );
}
