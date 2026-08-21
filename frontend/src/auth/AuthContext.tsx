import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { login as loginRequest } from "../api/client";
import { decodeJwtPayload } from "./jwt";

const STORAGE_KEY = "smartdoc.auth";

interface StoredAuth {
  token: string;
  expiresAt: string;
}

interface AuthContextValue {
  token: string | null;
  email: string | null;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function readStoredAuth(): StoredAuth | null {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as StoredAuth;
    // Expired tokens are dropped eagerly rather than sent and rejected by the Api — the
    // seed user's session (Jwt:ExpirationMinutes) is short enough in dev that this matters.
    if (new Date(parsed.expiresAt).getTime() <= Date.now()) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<StoredAuth | null>(() => readStoredAuth());

  const login = useCallback(async (email: string, password: string) => {
    const response = await loginRequest(email, password);
    const next: StoredAuth = { token: response.token, expiresAt: response.expiresAt };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    setAuth(next);
  }, []);

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY);
    setAuth(null);
  }, []);

  const email = useMemo(() => {
    if (!auth?.token) return null;
    const payload = decodeJwtPayload(auth.token);
    return typeof payload?.email === "string" ? payload.email : null;
  }, [auth]);

  const value = useMemo<AuthContextValue>(
    () => ({ token: auth?.token ?? null, email, login, logout }),
    [auth, email, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider.");
  }
  return context;
}
