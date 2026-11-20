/** RFC 4122 v4 UUID; crypto.randomUUID is unavailable outside secure contexts, so fall back to getRandomValues. */
export function uuid(): string {
  const c = globalThis.crypto;
  if (typeof c?.randomUUID === "function") {
    return c.randomUUID();
  }
  const b = new Uint8Array(16);
  c.getRandomValues(b);
  b[6] = (b[6]! & 0x0f) | 0x40;
  b[8] = (b[8]! & 0x3f) | 0x80;
  const h = Array.from(b, (x) => x.toString(16).padStart(2, "0")).join("");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

/** JSON with non-ASCII characters escaped, so it is a valid (ISO-8859-1 safe) HTTP header value. */
export function asciiJson(value: unknown): string {
  return JSON.stringify(value).replace(/[\u007f-￿]/g, (ch) => `\\u${ch.charCodeAt(0).toString(16).padStart(4, "0")}`);
}

/** Display name from an id_token payload (name, given_name, email). No signature check: display only. */
export function nameFromIdToken(idToken: string | undefined): string | undefined {
  if (!idToken) return undefined;
  const part = idToken.split(".")[1];
  if (!part) return undefined;
  try {
    const b64 = part.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(part.length / 4) * 4, "=");
    const bytes = Uint8Array.from(atob(b64), (ch) => ch.charCodeAt(0));
    const claims = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
    for (const key of ["name", "given_name", "email"]) {
      const v = claims[key];
      if (typeof v === "string" && v.trim()) return v.trim();
    }
  } catch {
    // malformed token: no name
  }
  return undefined;
}
