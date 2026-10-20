"use client";

import type { ButtonHTMLAttributes, ReactNode } from "react";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement>;

export function PrimaryButton({ className, ...props }: ButtonProps) {
  return <button {...props} type="button" className={className ? `primary ${className}` : "primary"} />;
}

export function TextButton({ className, ...props }: ButtonProps) {
  return <button {...props} type="button" className={className ? `text-action ${className}` : "text-action"} />;
}

export function ErrorLine({ message }: { message: string | null }) {
  if (!message) return null;
  return (
    <p className="sheet-error" role="alert">
      {message}
    </p>
  );
}

export function Stage({
  map,
  sheet,
  banner,
}: {
  map: ReactNode;
  sheet: ReactNode;
  banner?: ReactNode;
}) {
  return (
    <main className="stage">
      <div className="map-layer">{map}</div>
      {banner ? <div className="banner-layer">{banner}</div> : null}
      <div className="sheet-layer">
        <section className="sheet">{sheet}</section>
      </div>
    </main>
  );
}
