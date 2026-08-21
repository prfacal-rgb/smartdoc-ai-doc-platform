/**
 * Decodes a JWT payload client-side, for display only (e.g. showing who's logged in). JWTs
 * are signed, not encrypted - the payload is just base64url JSON, readable by anyone who has
 * the token anyway, so this isn't a trust boundary. Never used to authorize anything; the Api
 * is what actually validates the signature on every request.
 */
export function decodeJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const payload = token.split(".")[1];
    const base64 = payload.replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => `%${c.charCodeAt(0).toString(16).padStart(2, "0")}`)
        .join(""),
    );
    return JSON.parse(json);
  } catch {
    return null;
  }
}
