import type {
  ChatResponse,
  ConversationHistoryResponse,
  DocumentResponse,
  LoginResponse,
  ValidationProblemDetails,
} from "./types";

// Falls back to the Api's docker-compose default (see .env.example / ADR 0024) — override
// with a .env.local (VITE_API_BASE_URL) when the Api runs on a different host/port.
const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? "http://localhost:8080";

/**
 * Thrown for any non-2xx response. `status` lets callers distinguish 401 (bad/expired
 * token — the caller should log out) from a validation error worth showing inline, without
 * parsing the message string.
 */
export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

async function extractErrorMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as ValidationProblemDetails;
    if (problem.errors) {
      return Object.values(problem.errors).flat().join(" ");
    }
    return problem.detail ?? problem.title ?? response.statusText;
  } catch {
    return response.statusText || `Request failed with status ${response.status}`;
  }
}

async function request<T>(
  path: string,
  options: RequestInit & { token?: string } = {},
): Promise<T> {
  const { token, headers, ...rest } = options;

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...rest,
    headers: {
      ...(rest.body && !(rest.body instanceof FormData) ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
  });

  if (!response.ok) {
    throw new ApiError(response.status, await extractErrorMessage(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export function login(email: string, password: string): Promise<LoginResponse> {
  return request<LoginResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
}

export function getDocuments(token: string): Promise<DocumentResponse[]> {
  return request<DocumentResponse[]>("/api/documents", { token });
}

export function uploadDocument(token: string, file: File): Promise<DocumentResponse> {
  const formData = new FormData();
  formData.append("file", file);

  return request<DocumentResponse>("/api/documents", {
    method: "POST",
    body: formData,
    token,
  });
}

export function deleteDocument(token: string, id: string): Promise<void> {
  return request<void>(`/api/documents/${id}`, { method: "DELETE", token });
}

export function getConversation(token: string, conversationId: string): Promise<ConversationHistoryResponse> {
  return request<ConversationHistoryResponse>(`/api/chat/${conversationId}`, { token });
}

export function postChat(
  token: string,
  question: string,
  conversationId?: string,
): Promise<ChatResponse> {
  return request<ChatResponse>("/api/chat", {
    method: "POST",
    body: JSON.stringify({ question, conversationId: conversationId ?? null }),
    token,
  });
}
