import { useEffect, useMemo, useState } from "react";
import { api, ApiError } from "./api";
import { API_URL } from "../src/src/config";
import type { Bild, Roll } from "./types";
import { ALLA_ROLLER } from "./types";

const LAGRAD_ROLL = "mingram:roll";

function farLadda(roll: Roll) {
  return roll === "Fotograf" || roll === "Admin";
}

function farRadera(roll: Roll) {
  return roll === "Admin";
}

export default function App() {
  const [roll, setRoll] = useState<Roll>(
    () => (localStorage.getItem(LAGRAD_ROLL) as Roll) ?? "Betraktare",
  );

  const [bilder, setBilder] = useState<Bild[]>([]);
  const [laddar, setLaddar] = useState(false);
  const [fel, setFel] = useState<string | null>(null);
  const [visaFormular, setVisaFormular] = useState(false);

  const [redigerarId, setRedigerarId] = useState<string | null>(null);

  useEffect(() => {
    localStorage.setItem(LAGRAD_ROLL, roll);
  }, [roll]);

  const apiUrl = useMemo(() => API_URL!.replace(/\/$/, ""), []);

  async function hamta() {
    setLaddar(true);
    setFel(null);

    try {
      const data = await api.hamtaAlla(apiUrl, roll);

      setBilder(data);
    } catch (e) {
      setFel(tolkaFel(e));
    } finally {
      setLaddar(false);
    }
  }

  useEffect(() => {
    hamta();

    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [roll]);

  async function taBort(id: string, namn: string) {
    if (!confirm(`Radera "${namn}"?`)) return;

    setFel(null);

    try {
      await api.radera(apiUrl, roll, id);

      setBilder((b) => b.filter((x) => x.id !== id));
    } catch (e) {
      setFel(tolkaFel(e));
    }
  }

  return (
    <div className="sida">
      <header className="apphuvud">
        <span className="varumarke-ikon" aria-hidden="true" />

        <div>
          <h1>MinGram</h1>
          <p className="undertext">Where every shot tells a story</p>
        </div>
      </header>

      <div className="demorad">
        <label className="falt">
          <span>Testroll (demo)</span>

          <select
            value={roll}
            onChange={(e) => setRoll(e.target.value as Roll)}
          >
            {ALLA_ROLLER.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </label>

        <span className={`rollbricka rollbricka--${roll.toLowerCase()}`}>
          {roll}
        </span>
      </div>

      {fel && <div className="felbanner">{fel}</div>}

      <div className="verktygsrad">
        {farLadda(roll) && (
          <button
            onClick={() => setVisaFormular((v) => !v)}
            className="knapp knapp--primar"
          >
            {visaFormular ? "Avbryt" : "+ Lägg till bild"}
          </button>
        )}
      </div>

      {visaFormular && farLadda(roll) && (
        <NyBildFormular
          onSkapad={(b) => {
            setBilder((prev) => [...prev, b]);

            setVisaFormular(false);
          }}
          onFel={(e) => setFel(tolkaFel(e))}
          apiUrl={apiUrl}
          roll={roll}
        />
      )}

      <section className="galleri" data-laddar={laddar}>
        {bilder.map((b) => (
          <article className="kort" key={b.id}>
            <div className="kort-perforering" aria-hidden="true" />

            <img src={b.url} alt={b.caption} loading="lazy" />

            <div className="kort-innehall">
              {redigerarId === b.id ? (
                <RedigeraFormular
                  bild={b}
                  apiUrl={apiUrl}
                  roll={roll}
                  onKlar={(uppdaterad) => {
                    setBilder((prev) =>
                      prev.map((x) => (x.id === b.id ? uppdaterad : x)),
                    );

                    setRedigerarId(null);
                  }}
                  onAvbryt={() => setRedigerarId(null)}
                  onFel={(e) => setFel(tolkaFel(e))}
                />
              ) : (
                <>
                  <p className="kort-caption">{b.caption}</p>

                  <div className="tagg-rad">
                    {b.taggar.map((t) => (
                      <span className="tagg" key={t}>
                        #{t}
                      </span>
                    ))}
                  </div>

                  <div className="kort-atgarder">
                    {farLadda(roll) && (
                      <button
                        className="knapp knapp--liten"
                        onClick={() => setRedigerarId(b.id)}
                      >
                        Redigera
                      </button>
                    )}

                    {farRadera(roll) && (
                      <button
                        className="knapp knapp--liten knapp--fara"
                        onClick={() => taBort(b.id, b.namn)}
                      >
                        Radera
                      </button>
                    )}
                  </div>
                </>
              )}
            </div>
          </article>
        ))}
      </section>

      {!laddar && bilder.length === 0 && !fel && (
        <p className="tom-vy">
          Inga bilder än. {farLadda(roll) && "Lägg till en ovan."}
        </p>
      )}
    </div>
  );
}

function tolkaFel(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 403) {
      return "403 Forbidden — din roll saknar behörighet för det här.";
    }

    if (e.status === 401) {
      return "401 Unauthorized — inloggning krävs.";
    }

    if (e.status === 404) {
      return "404 — hittades inte.";
    }

    return `Fel ${e.status}: ${e.message}`;
  }

  if (e instanceof Error) {
    return e.message;
  }

  return "Något gick fel.";
}

function NyBildFormular({
  apiUrl,
  roll,
  onSkapad,
  onFel,
}: {
  apiUrl: string;
  roll: Roll;
  onSkapad: (b: Bild) => void;
  onFel: (e: unknown) => void;
}) {
  const [namn, setNamn] = useState("");
  const [caption, setCaption] = useState("");
  const [taggar, setTaggar] = useState("");
  const [url, setUrl] = useState("");
  const [sparar, setSparar] = useState(false);

  async function skicka(ev: React.FormEvent) {
    ev.preventDefault();

    setSparar(true);

    try {
      const ny = await api.skapa(apiUrl, roll, {
        namn,
        caption,
        url,
        taggar: taggar
          .split(",")
          .map((t) => t.trim())
          .filter(Boolean),
      });

      onSkapad(ny);

      setNamn("");
      setCaption("");
      setTaggar("");
      setUrl("");
    } catch (e) {
      onFel(e);
    } finally {
      setSparar(false);
    }
  }

  return (
    <form className="panel" onSubmit={skicka}>
      <div className="panel-grid">
        <label className="falt">
          <span>Namn</span>

          <input
            required
            value={namn}
            onChange={(e) => setNamn(e.target.value)}
          />
        </label>

        <label className="falt">
          <span>Bild-URL</span>

          <input
            required
            type="url"
            placeholder="https://…"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
          />
        </label>

        <label className="falt falt--bred">
          <span>Caption</span>

          <input
            required
            value={caption}
            onChange={(e) => setCaption(e.target.value)}
          />
        </label>

        <label className="falt falt--bred">
          <span>Taggar (kommaseparerat)</span>

          <input
            placeholder="resa, sommar, gotland"
            value={taggar}
            onChange={(e) => setTaggar(e.target.value)}
          />
        </label>
      </div>

      <button className="knapp knapp--primar" type="submit" disabled={sparar}>
        {sparar ? "Sparar…" : "Spara bild"}
      </button>
    </form>
  );
}

function RedigeraFormular({
  bild,
  apiUrl,
  roll,
  onKlar,
  onAvbryt,
  onFel,
}: {
  bild: Bild;
  apiUrl: string;
  roll: Roll;
  onKlar: (b: Bild) => void;
  onAvbryt: () => void;
  onFel: (e: unknown) => void;
}) {
  const [caption, setCaption] = useState(bild.caption);

  const [taggar, setTaggar] = useState(bild.taggar.join(", "));

  const [sparar, setSparar] = useState(false);

  async function skicka(ev: React.FormEvent) {
    ev.preventDefault();

    setSparar(true);

    try {
      const uppdaterad = await api.uppdatera(apiUrl, roll, bild.id, {
        caption,
        taggar: taggar
          .split(",")
          .map((t) => t.trim())
          .filter(Boolean),
      });

      onKlar(uppdaterad);
    } catch (e) {
      onFel(e);
    } finally {
      setSparar(false);
    }
  }

  return (
    <form className="redigera-formular" onSubmit={skicka}>
      <label className="falt">
        <span>Caption</span>

        <input value={caption} onChange={(e) => setCaption(e.target.value)} />
      </label>

      <label className="falt">
        <span>Taggar</span>

        <input value={taggar} onChange={(e) => setTaggar(e.target.value)} />
      </label>

      <div className="kort-atgarder">
        <button
          className="knapp knapp--liten knapp--primar"
          type="submit"
          disabled={sparar}
        >
          {sparar ? "Sparar…" : "Spara"}
        </button>

        <button className="knapp knapp--liten" type="button" onClick={onAvbryt}>
          Avbryt
        </button>
      </div>
    </form>
  );
}
