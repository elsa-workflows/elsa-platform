import { forwardRef } from "react";
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from "react";
import { cn } from "@/lib/utils";

export function buttonClassName(variant: "primary" | "secondary" = "primary", className?: string) {
  return cn(
    "inline-flex h-9 items-center justify-center gap-2 rounded-ui border border-border px-3 text-sm font-medium transition-colors hover:opacity-90 disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50",
    variant === "primary" ? "bg-foreground text-background" : "bg-background text-foreground hover:bg-muted",
    className
  );
}

export const Button = forwardRef<HTMLButtonElement, ButtonHTMLAttributes<HTMLButtonElement>>(function Button({ className, ...props }, ref) {
  return <button ref={ref} className={buttonClassName("primary", className)} {...props} />;
});

export const SecondaryButton = forwardRef<HTMLButtonElement, ButtonHTMLAttributes<HTMLButtonElement>>(function SecondaryButton({ className, ...props }, ref) {
  return <button ref={ref} className={buttonClassName("secondary", className)} {...props} />;
});

export function Input({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={cn(
        "h-9 w-full rounded-ui border border-border bg-background px-3 text-sm text-foreground placeholder:text-muted-foreground",
        className
      )}
      {...props}
    />
  );
}

export function Select({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select className={cn("h-9 rounded-ui border border-border bg-background px-3 text-sm", className)} {...props} />
  );
}

export function Badge({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <span className={cn("inline-flex items-center rounded-ui border border-border px-2 py-0.5 text-xs", className)}>
      {children}
    </span>
  );
}

export function EmptyState({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
  return (
    <div className="rounded-ui border border-dashed border-border bg-surface p-8 text-center">
      <h2 className="text-base font-medium">{title}</h2>
      <p className="mt-2 text-sm text-muted-foreground">{description}</p>
      {action ? <div className="mt-4 flex justify-center">{action}</div> : null}
    </div>
  );
}

export function Table({ children }: { children: ReactNode }) {
  return <div className="overflow-x-auto rounded-ui border border-border">{children}</div>;
}

export function DialogPanel({ children }: { children: ReactNode }) {
  return <div className="rounded-ui border border-border bg-background p-4 shadow-sm">{children}</div>;
}

export function Tabs({ children }: { children: ReactNode }) {
  return <div className="flex gap-1 border-b border-border">{children}</div>;
}
