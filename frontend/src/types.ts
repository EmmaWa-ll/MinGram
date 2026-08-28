export interface Bild {
  id: string;
  namn: string;
  caption: string;
  taggar: string[];
  url: string;
}

export interface NyBild {
  namn: string;
  caption: string;
  taggar: string[];
  url: string;
}

export interface BildUpdate {
  caption?: string;
  taggar?: string[];
}

export type Roll = "Admin" | "Fotograf" | "Betraktare";

export const ALLA_ROLLER: Roll[] = ["Admin", "Fotograf", "Betraktare"];
