export function BikeIcon() {
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
