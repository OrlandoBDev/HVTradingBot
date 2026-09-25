/** Fired when the session has ended (signed out elsewhere, password changed, expired); the app shows the login page. */
export const UNAUTHORIZED_EVENT = "hv:unauthorized";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: "same-origin",
    // The custom header marks the request as coming from this app; the API refuses changes without it.
    headers: { "Content-Type": "application/json", "X-HV-Request": "1", ...(init?.headers ?? {}) },
  });
  if (response.status === 401 && !path.startsWith("/api/auth/")) {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  }
  if (response.status === 204) {
    return undefined as T;
  }
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body = await response.json();
      const errors = body?.errors ? Object.values(body.errors).flat().join(" ") : "";
      detail = errors || body?.detail || body?.title || detail;
    } catch {
      // Non-JSON error body; keep the status text.
    }
    throw new Error(detail);
  }
  return (await response.json()) as T;
}

export const api = {
  get: <T,>(path: string) => request<T>(path),
  post: <T,>(path: string, body: unknown) => request<T>(path, { method: "POST", body: JSON.stringify(body) }),
  put: <T,>(path: string, body: unknown) => request<T>(path, { method: "PUT", body: JSON.stringify(body) }),
  del: (path: string) => request<void>(path, { method: "DELETE" }),
};
