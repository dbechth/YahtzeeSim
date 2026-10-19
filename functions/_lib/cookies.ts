export const SESSION_COOKIE = "yz_session";
export const OAUTH_COOKIE = "yz_oauth";
export const SESSION_MAX_AGE_S = 30 * 24 * 60 * 60;

export function parseCookies(header: string | null): Record<string, string> {
  const result: Record<string, string> = {};
  for (const part of (header ?? "").split(";")) {
    const index = part.indexOf("=");
    if (index > 0) {
      result[part.slice(0, index).trim()] = part.slice(index + 1).trim();
    }
  }
  return result;
}

export interface CookieOptions {
  maxAge: number;
  path: string;
  secure: boolean;
}

export function serializeCookie(name: string, value: string, options: CookieOptions): string {
  const parts = [`${name}=${value}`, `Path=${options.path}`, `Max-Age=${options.maxAge}`, "HttpOnly", "SameSite=Lax"];
  if (options.secure) {
    parts.push("Secure");
  }
  return parts.join("; ");
}

export function clearCookie(name: string, path: string, secure: boolean): string {
  return serializeCookie(name, "", { maxAge: 0, path, secure });
}
