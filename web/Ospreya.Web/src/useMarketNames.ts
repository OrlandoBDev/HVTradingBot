import { useEffect, useState } from "react";
import { api } from "./api";

type Names = Record<string, string>;

// Shared by every page. The list changes when the broker catalog does (and, in the Android app, fills in a few seconds
// after start while the engine loads the catalog), so a market missing from it triggers a new fetch, at most every 15 s.
const RETRY_MS = 15_000;
let cache: Names = {};
let pending: Promise<void> | null = null;
let lastFetch = 0;
const listeners = new Set<(names: Names) => void>();

function refresh() {
  if (pending || Date.now() - lastFetch < RETRY_MS) return;
  lastFetch = Date.now();
  pending = api
    .get<Names>("/api/markets/names")
    .then((names) => {
      // Names are keyed by our symbol and the broker's id in any letter case.
      cache = Object.fromEntries(Object.entries(names).map(([k, v]) => [k.toUpperCase(), v]));
      listeners.forEach((l) => l(cache));
    })
    .catch(() => undefined)
    .finally(() => {
      pending = null;
    });
}

/** Returns a function mapping a market symbol or broker id (e.g. EUR/USD, 1HZ75V) to its readable name, falling back to the symbol. */
export function useMarketNames(): (symbol: string) => string {
  const [names, setNames] = useState<Names>(cache);

  useEffect(() => {
    listeners.add(setNames);
    if (Object.keys(cache).length === 0) {
      lastFetch = 0;
      refresh();
    }
    return () => {
      listeners.delete(setNames);
    };
  }, []);

  return (symbol: string) => {
    const name = names[symbol.toUpperCase()];
    if (name === undefined && symbol) refresh(); // unknown market: the catalog may have been loaded since
    return name ?? symbol;
  };
}
