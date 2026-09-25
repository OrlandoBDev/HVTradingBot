import { useEffect, useState } from "react";
import { api } from "./api";

type Names = Record<string, string>;

// Shared by every page: the list only changes when the broker catalog does, so one fetch per page load is enough.
let cache: Names | null = null;
let pending: Promise<Names> | null = null;

function load(): Promise<Names> {
  pending ??= api
    .get<Names>("/api/markets/names")
    .then((names) => (cache = names))
    .catch(() => {
      pending = null; // try again on the next page that needs names
      return {};
    });
  return pending;
}

/** Returns a function mapping a market symbol (e.g. 1HZ75V) to its readable name, falling back to the symbol. */
export function useMarketNames(): (symbol: string) => string {
  const [names, setNames] = useState<Names>(cache ?? {});

  useEffect(() => {
    if (cache) return;
    let cancelled = false;
    load().then((n) => !cancelled && setNames(n));
    return () => {
      cancelled = true;
    };
  }, []);

  return (symbol: string) => names[symbol] ?? symbol;
}
