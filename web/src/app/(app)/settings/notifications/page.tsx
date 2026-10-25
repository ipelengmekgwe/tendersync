import { NotificationSettings } from "@/components/settings/notification-settings";

export const metadata = { title: "Notification settings" };

/** Channels and preferences (PLAN.md §9, §11): every user manages their own and can send a test. */
export default function NotificationSettingsPage() {
  return <NotificationSettings />;
}
