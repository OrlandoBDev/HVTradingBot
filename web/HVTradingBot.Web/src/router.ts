import { useEffect, useState } from "react";
import { PAGES, type PageId } from "./pages";

export interface Route {
  page: PageId;
  section?: string;
}

function parse(hash: string): Route {
  const [page, section] = hash.replace(/^#\/?/, "").split("/");
  return PAGES.some((p) => p.id === page) ? { page: page as PageId, section } : { page: "overview" };
}

/** Hash-based routing (#/settings/markets) so a browser refresh keeps the current page. */
export function useRoute(): [Route, (page: PageId, section?: string) => void] {
  const [route, setRoute] = useState<Route>(() => parse(window.location.hash));

  useEffect(() => {
    const onChange = () => setRoute(parse(window.location.hash));
    window.addEventListener("hashchange", onChange);
    return () => window.removeEventListener("hashchange", onChange);
  }, []);

  const navigate = (page: PageId, section?: string) => {
    window.location.hash = section ? `/${page}/${section}` : `/${page}`;
    window.scrollTo(0, 0);
  };

  return [route, navigate];
}
