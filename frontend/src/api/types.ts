// Mirrors the .NET response records exactly (SmartDoc.Api/Features/**) — see
// docs/architecture.md for the endpoints these come from. ASP.NET Core's default JSON
// options camelCase every property, so these fields match the wire format as-is.

export type DocumentStatus = "Uploaded" | "Processing" | "Ready" | "Failed";

export interface DocumentResponse {
  id: string;
  userId: string;
  fileName: string;
  contentType: string;
  storagePath: string;
  status: DocumentStatus;
  createdAt: string;
}

export interface Citation {
  fileName: string;
  pageNumber: number;
}

export interface ChatResponse {
  conversationId: string;
  answer: string;
  sources: Citation[];
}

export interface MessageResponse {
  id: string;
  role: "User" | "Assistant";
  content: string;
  createdAt: string;
}

export interface ConversationHistoryResponse {
  conversationId: string;
  messages: MessageResponse[];
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
}

// Not part of the API response — client-side validation errors (FluentValidation via
// ValidationProblem) surface as ProblemDetails with an "errors" dictionary.
export interface ValidationProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}
