import type { Bild, NyBild, BildUpdate, Roll } from "./types";

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function anropa<T>(
  baseUrl: string,
  path: string,
  roll: Roll,
  init?: RequestInit,
): Promise<T> {
  const res = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",

      // Demo-roll eftersom Entra/Easy Auth inte används fullt ut.
      "X-Demo-Role": roll,

      ...(init?.headers ?? {}),
    },
  });

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new ApiError(res.status, text || res.statusText);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return (await res.json()) as T;
}

export const api = {
  // GET /bilder
  hamtaAlla: (baseUrl: string, roll: Roll) =>
    anropa<Bild[]>(baseUrl, "/bilder", roll),

  // POST /bilder
  skapa: (baseUrl: string, roll: Roll, ny: NyBild) =>
    anropa<Bild>(baseUrl, "/bilder", roll, {
      method: "POST",
      body: JSON.stringify(ny),
    }),

  // PUT /bilder/{id}
  uppdatera: (baseUrl: string, roll: Roll, id: string, update: BildUpdate) =>
    anropa<Bild>(baseUrl, `/bilder/${encodeURIComponent(id)}`, roll, {
      method: "PUT",
      body: JSON.stringify(update),
    }),

  // DELETE /bilder/{id}
  radera: (baseUrl: string, roll: Roll, id: string) =>
    anropa<void>(baseUrl, `/bilder/${encodeURIComponent(id)}`, roll, {
      method: "DELETE",
    }),
};
