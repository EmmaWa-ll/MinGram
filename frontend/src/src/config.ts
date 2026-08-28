export const API_URL = import.meta.env.VITE_API_URL as string | undefined;

if (!API_URL) {
  throw new Error(
    "VITE_API_URL saknas — lägg till den i .env (se .env.example för hur).",
  );
}
