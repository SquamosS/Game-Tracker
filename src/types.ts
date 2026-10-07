export interface Achievement {
  apiName: string;
  name: string;
  description: string;
  hidden: boolean;
  icon: string;
  iconGray: string;
  achieved: boolean;
  unlockTime: number | null;
  globalPercent: number | null;
}

export interface GameAchievements {
  appId: number;
  gameName: string;
  achievements: Achievement[];
}

export interface Settings {
  apiKey: string;
  steamId: string;
  language: string;
}

export interface GuideItem {
  id: string;
  title: string;
  /** Steam display name of the achievement this item unlocks, if any. */
  achievement?: string;
  hint?: string;
  missable?: boolean;
}

export interface Guide {
  appId: number;
  title: string;
  status: "draft" | "verified";
  notes: string[];
  sections: { name: string; items: GuideItem[] }[];
}
