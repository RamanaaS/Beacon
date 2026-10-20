/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Beacon.Api base URL for a deployed build, e.g. https://ramanaas-beacon-api.onrender.com */
  readonly VITE_API_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
