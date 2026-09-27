export type Shape = "PRODUCT" | "VINTAGE" | "GOODS" | "STORE" | "ARTICLE";
export type Genre = "FASHION" | "LIVING" | "FOOD" | "BOOKS" | "HOBBY" | "TECH" | "BEAUTY" | "PLACE";
export type Stage = "USED" | "CURIOUS" | "WANT";
export type MarkStatus = "PENDING" | "PUBLIC" | "HIDDEN" | "GONE";
export type PendingReason = "AUTO_REVIEW" | "UNKNOWN_DOMAIN" | "AI_FLAGGED";
export type PopStatus = "PENDING" | "VISIBLE" | "HIDDEN";
export type TrustLevel = "NEW" | "NORMAL" | "TRUSTED";
export type DomainRuleKind = "ALLOW" | "REVIEW" | "DENY";

export type CityMark = {
  id: string;
  x: number;
  y: number;
  weight: number;
  shape: Shape;
  genre: Genre;
  stage: Stage;
  status: MarkStatus;
  isMine: boolean;
  isNew: boolean;
  opacity: number;
  popText: string;
  popPhotoUrl: string | null;
  popCount: number;
  hasPr: boolean;
  isShop: boolean;
  shopId: string | null;
  neighborIds: string[];
  createdAt: string;
};

export type CityView = { marks: CityMark[]; baseRadius: number; generatedAt: string };

export type PopView = {
  id: string;
  authorName: string;
  text: string;
  photoUrl: string | null;
  stage: Stage;
  hasPr: boolean;
  isOriginal: boolean;
  status: PopStatus;
  createdAt: string;
};

export type MarkDetail = {
  id: string;
  shape: Shape;
  genre: Genre;
  stage: Stage;
  status: MarkStatus;
  pendingReason: PendingReason | null;
  isMine: boolean;
  ownerName: string;
  linkUrl: string | null;
  domain: string | null;
  hasLink: boolean;
  hasPr: boolean;
  linkBroken: boolean;
  pops: PopView[];
  preview: { title: string | null; siteName: string | null; imageUrl: string | null } | null;
  neighbors: { id: string; popText: string; shape: Shape; genre: Genre }[];
  saveId: string | null;
  canAddPop: boolean;
  shopId: string | null;
  createdAt: string;
};

export type Snapshot = {
  markId: string;
  shape: Shape;
  genre: Genre;
  stage: Stage;
  linkUrl: string | null;
  domain: string | null;
  hasPr: boolean;
  title: string | null;
  imageUrl: string | null;
  popText: string;
  popPhotoUrl: string | null;
  popAuthorName: string;
  neighbors: { markId: string; popText: string; shape: Shape; genre: Genre }[];
  capturedAt: string;
};

export type SaveView = {
  id: string;
  savedAt: string;
  snapshot: Snapshot;
  presence: "IN_CITY" | "GONE";
  currentX: number | null;
  currentY: number | null;
  inMyShop: boolean;
};

export type Me = {
  hasDevice: boolean;
  isRegistered: boolean;
  userId: string | null;
  displayName: string | null;
  trust: TrustLevel;
  isKidsMode: boolean;
  isAdmin: boolean;
  shopId: string | null;
  saveCount: number;
};

export type ShopItem = {
  id: string;
  stage: Stage;
  snapshot: Snapshot;
  originalAuthorName: string;
  originalMarkId: string;
  originalInCity: boolean;
  position: number;
};

export type Shop = {
  id: string;
  name: string;
  ownerName: string;
  isPublished: boolean;
  isMine: boolean;
  markId: string | null;
  markInCity: boolean;
  items: ShopItem[];
};

export type PlaceResult = { markId: string; status: MarkStatus; pendingReason: PendingReason | null; message: string };

export type AdminQueue = {
  marks: {
    id: string;
    status: MarkStatus;
    pendingReason: PendingReason | null;
    reviewNote: string | null;
    linkUrl: string | null;
    domain: string | null;
    ownerName: string;
    ownerId: string;
    popText: string;
    reportWeight: number;
    createdAt: string;
  }[];
  pops: { id: string; markId: string; authorName: string; text: string; reviewNote: string | null; createdAt: string }[];
  reports: {
    id: string;
    kind: "ABUSE" | "TAKEDOWN";
    markId: string | null;
    popId: string | null;
    reason: string;
    contact: string | null;
    weight: number;
    createdAt: string;
  }[];
  domains: { domain: string; kind: DomainRuleKind; note: string | null }[];
};
