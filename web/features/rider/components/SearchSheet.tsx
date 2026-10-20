import type { Place } from "@/features/maps/types";
import { ErrorLine } from "@/shared/ui";

export function SearchSheet({
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
