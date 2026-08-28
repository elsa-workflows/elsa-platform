export type ApiErrorKind =
  | "Unauthorized"
  | "Forbidden"
  | "Validation"
  | "Conflict"
  | "NotFound"
  | "Unavailable"
  | "Unexpected";

export class ApiError extends Error {
  constructor(
    public readonly kind: ApiErrorKind,
    message: string,
    public readonly status?: number,
    public readonly details?: unknown
  ) {
    super(message);
  }
}

export type ApiClientOptions = {
  baseUrl?: string;
};

const defaultBaseUrl = import.meta.env.VITE_CATALOG_CLIENT_BASE_URL ?? "";

export async function apiRequest<T>(path: string, init: RequestInit = {}, options: ApiClientOptions = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(`${options.baseUrl ?? defaultBaseUrl}${path}`, { credentials: "same-origin", ...init, headers });

  if (!response.ok) {
    throw await toApiError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function toApiError(response: Response) {
  const details = await readProblem(response);
  const message = problemMessage(details) ?? response.statusText;
  switch (response.status) {
    case 400:
      return new ApiError("Validation", message, response.status, details);
    case 401:
      return new ApiError("Unauthorized", message, response.status, details);
    case 403:
      return new ApiError("Forbidden", message, response.status, details);
    case 404:
      return new ApiError("NotFound", message, response.status, details);
    case 409:
      return new ApiError("Conflict", message, response.status, details);
    case 503:
      return new ApiError("Unavailable", message, response.status, details);
    default:
      return new ApiError("Unexpected", message, response.status, details);
  }
}

async function readProblem(response: Response) {
  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

function problemMessage(details: unknown) {
  const errors = validationErrors(details);
  if (errors.length > 0) {
    return errors.join(" | ");
  }
  if (details && typeof details === "object" && "detail" in details && typeof details.detail === "string") {
    return details.detail;
  }
  if (details && typeof details === "object" && "error" in details && typeof details.error === "string") {
    return details.error;
  }
  if (details && typeof details === "object" && "title" in details && typeof details.title === "string") {
    return details.title;
  }
  return undefined;
}

function validationErrors(details: unknown) {
  if (!details || typeof details !== "object" || !("errors" in details)) {
    return [];
  }

  const { errors } = details;
  if (Array.isArray(errors)) {
    return errors.filter((error): error is string => typeof error === "string");
  }

  if (!errors || typeof errors !== "object") {
    return [];
  }

  return Object.values(errors).flatMap((value) => {
    if (Array.isArray(value)) {
      return value.filter((error): error is string => typeof error === "string");
    }
    return typeof value === "string" ? [value] : [];
  });
}
