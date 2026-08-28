import { createContext, useCallback, useContext, useMemo } from "react";
import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Navigate, useLocation } from "react-router-dom";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { getCustomerAuthSession } from "@/lib/auth/authApi";
import type { CustomerAuthSession } from "@/lib/auth/authModels";
import { queryKeys } from "@/lib/query/queryClient";

export const loginRoute = "/admin/login";
export const defaultReturnUrl = "/admin/overview";

type AuthContextValue = {
  session: CustomerAuthSession | undefined;
  isLoading: boolean;
  signIn: (returnUrl?: string) => void;
  signOut: (returnUrl?: string) => void;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const session = useQuery({
    queryKey: queryKeys.authSession,
    queryFn: getCustomerAuthSession,
    staleTime: 60_000,
    retry: false
  });

  const signIn = useCallback((returnUrl?: string) => {
    const loginPath = session.data?.loginPath ?? "/api/auth/login";
    window.location.assign(`${loginPath}?returnUrl=${encodeURIComponent(safeReturnUrl(returnUrl))}`);
  }, [session.data?.loginPath]);

  const signOut = useCallback((returnUrl?: string) => {
    const logoutPath = session.data?.logoutPath ?? "/api/auth/logout";
    const form = document.createElement("form");
    form.method = "post";
    form.action = `${logoutPath}?returnUrl=${encodeURIComponent(safeReturnUrl(returnUrl))}`;
    document.body.append(form);
    form.submit();
  }, [session.data?.logoutPath]);

  const value = useMemo<AuthContextValue>(() => ({
    session: session.data,
    isLoading: session.isLoading,
    signIn,
    signOut
  }), [session.data, session.isLoading, signIn, signOut]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (value === null) {
    throw new Error("useAuth must be used inside AuthProvider.");
  }

  return value;
}

/**
 * The single gate for every authenticated console route. An unauthenticated visitor is sent to the
 * login route rather than shown an inline prompt, so an expired or missing session always looks the
 * same no matter which page discovered it.
 */
export function RequireCustomerAuth({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const location = useLocation();

  if (auth.isLoading) {
    return <RequestStateView state="loading" title="Loading customer session" />;
  }

  if (!auth.session?.loginEnabled) {
    return (
      <RequestStateView
        state="unauthorized"
        title="Customer login is not configured"
        description="Workspace features require a configured Elsa Control identity provider."
      />
    );
  }

  if (!auth.session.authenticated) {
    return <Navigate to={loginUrlFor(`${location.pathname}${location.search}${location.hash}`)} replace />;
  }

  return children;
}

export function loginUrlFor(returnUrl: string) {
  return `${loginRoute}?returnUrl=${encodeURIComponent(safeReturnUrl(returnUrl))}`;
}

/** Keeps a caller-supplied return URL to same-origin console paths so it cannot be used to redirect away. */
export function safeReturnUrl(returnUrl: string | null | undefined) {
  if (!returnUrl || !returnUrl.startsWith("/") || returnUrl.startsWith("//")) {
    return defaultReturnUrl;
  }

  return returnUrl === loginRoute || returnUrl.startsWith(`${loginRoute}?`) ? defaultReturnUrl : returnUrl;
}
