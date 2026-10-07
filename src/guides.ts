import type { Guide } from "./types";

const modules = import.meta.glob<Guide>("../guides/*.json", { eager: true, import: "default" });

export const guides: Guide[] = Object.values(modules);

export function guideFor(appId: number | null): Guide | undefined {
  return guides.find((g) => g.appId === appId);
}
