import { useEffect, useState } from "react";
import { api } from "./api";

/** Fetches `path` and refetches whenever `refreshKey` changes (e.g. a new market bar arrived). */
export function useData<T>(path: string, refreshKey: unknown) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    api
      .get<T>(path)
      .then((result) => {
        if (!cancelled) {
          setData(result);
          setError(null);
        }
      })
      .catch((e: Error) => !cancelled && setError(e.message));
    return () => {
      cancelled = true;
    };
  }, [path, refreshKey]);

  return { data, error };
}
