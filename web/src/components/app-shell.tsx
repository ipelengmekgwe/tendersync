"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Building2, FileCheck2, FileSearch, Gauge, Inbox, Menu, Settings, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { cn } from "@/lib/utils";
import { hasRole, type User } from "@/lib/auth/cookies";
import { ThemeToggle } from "@/components/theme-toggle";
import { UserMenu } from "@/components/user-menu";

interface NavItem {
  href: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  role?: "Reviewer" | "Admin";
  soon?: string;
}

/** Navigation (PLAN.md §11). Items a role cannot use are hidden; the pages and the API check the role again. */
const NAV: NavItem[] = [
  { href: "/", label: "Overview", icon: Gauge },
  { href: "/matches", label: "Matches", icon: Inbox },
  { href: "/tenders", label: "Tenders", icon: FileSearch },
  { href: "/applications", label: "Applications", icon: FileCheck2, role: "Reviewer", soon: "Phase 2" },
  { href: "/company", label: "Company", icon: Building2, role: "Reviewer" },
  { href: "/settings", label: "Settings", icon: Settings },
  { href: "/admin", label: "Admin", icon: ShieldCheck, role: "Admin" },
];

function NavLinks({ user, onNavigate }: { user: User; onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <nav className="flex flex-col gap-1" aria-label="Main">
      {NAV.filter((item) => !item.role || hasRole(user, item.role)).map((item) => {
        const active = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);
        return (
          <Link
            key={item.href}
            href={item.href}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-accent hover:text-accent-foreground",
              active ? "bg-accent text-accent-foreground" : "text-muted-foreground",
            )}
          >
            <item.icon className="size-4" />
            <span className="flex-1">{item.label}</span>
            {item.soon ? <span className="text-[10px] uppercase tracking-wide text-muted-foreground">{item.soon}</span> : null}
          </Link>
        );
      })}
    </nav>
  );
}

export function AppShell({ user, children }: { user: User; children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen">
      <aside className="hidden w-60 shrink-0 flex-col border-r bg-sidebar p-4 md:flex">
        <Link href="/" className="mb-6 px-3 text-lg font-semibold tracking-tight">
          TenderSync
        </Link>
        <NavLinks user={user} />
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center gap-2 border-b px-4">
          <Sheet>
            <SheetTrigger
              render={<Button variant="ghost" size="icon" className="md:hidden" aria-label="Open navigation" />}
            >
              <Menu className="size-5" />
            </SheetTrigger>
            <SheetContent side="left" className="w-64 p-4">
              <SheetTitle className="mb-4 px-3">TenderSync</SheetTitle>
              <NavLinks user={user} />
            </SheetContent>
          </Sheet>
          <div className="flex-1" />
          <ThemeToggle />
          <UserMenu user={user} />
        </header>
        <main className="flex-1 p-4 md:p-6">{children}</main>
      </div>
    </div>
  );
}
