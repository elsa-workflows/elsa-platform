import { createContext, useCallback, useContext, useMemo } from "react";
import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { Button } from "@/components/ui";
import { getCustomerAuthSession } from "@/lib/auth/authApi";
import type { CustomerAuthSession } from "@/lib/auth/authModels";
import { queryKeys } from "@/lib/query/queryClient";

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

export function RequireCustomerAuth({ children }: { children: ReactNode }) {
  const auth = useAuth();

  if (auth.isLoading) {
    return <RequestStateView state="loading" title="Loading customer session" />;
  }

  if (!auth.session?.loginEnabled) {
    return (
      <RequestStateView
        state="unauthorized"
        title="Customer login is not configured"
        description="Workspace features require a configured Valence Control identity provider."
      />
    );
  }

  if (!auth.session.authenticated) {
    return (
      <section className="max-w-xl space-y-4">
        <div className="space-y-2">
          <h1 className="font-display text-xl font-semibold">Sign in</h1>
          <p className="text-sm text-muted-foreground">Use your configured Valence Control identity provider to access workspace features.</p>
        </div>
        <Button type="button" onClick={() => auth.signIn(currentReturnUrl())}>
          Sign in
        </Button>
      </section>
    );
  }

  return children;
}

function currentReturnUrl() {
  return `${window.location.pathname}${window.location.search}${window.location.hash}`;
}

function safeReturnUrl(returnUrl: string | undefined) {
  if (!returnUrl || returnUrl.startsWith("//")) {
    return "/admin/runtime-builder";
  }

  return returnUrl.startsWith("/") ? returnUrl : "/admin/runtime-builder";
}
