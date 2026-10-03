// Small crypto helpers (Web Crypto, available in Deno and in Edge Functions).

export function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** base64url(SHA-256(text)): the PKCE "S256" transform, also used to store one-time codes hashed. */
export async function sha256Base64Url(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return base64Url(new Uint8Array(digest));
}

/** A random token of `bytes` bytes, base64url-encoded. */
export function randomToken(bytes = 32): string {
  const buffer = new Uint8Array(bytes);
  crypto.getRandomValues(buffer);
  return base64Url(buffer);
}

/** Constant-time string comparison (for secrets of equal, public length). */
export function safeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}
