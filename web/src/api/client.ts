import type {
  AdminQueue,
  CityView,
  DomainRuleKind,
  Genre,
  MarkDetail,
  Me,
  PlaceResult,
  PopView,
  SaveView,
  Shape,
  Shop,
  Stage,
} from "./types";

const DEVICE_KEY = "machiaruki.deviceId";
const ADMIN_KEY = "machiaruki.adminKey";

let memoryDeviceId: string | null = null;

function randomId(): string {
  // randomUUID は安全な接続(https)でしか使えないので、なければ乱数から作る。
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
}

/**
 * 端末の ID。ログインなしで保存を続けるための鍵なので、端末に残す。
 * 保存領域が使えない環境では、このタブの間だけ有効な ID になる。
 */
export function deviceId(): string {
  try {
    const existing = localStorage.getItem(DEVICE_KEY);
    if (existing) return existing;
    const id = randomId();
    localStorage.setItem(DEVICE_KEY, id);
    return id;
  } catch {
    memoryDeviceId ??= randomId();
    return memoryDeviceId;
  }
}

export function adminKey(): string | null {
  try {
    return sessionStorage.getItem(ADMIN_KEY);
  } catch {
    return null;
  }
}

export function setAdminKey(key: string | null) {
  try {
    if (key) sessionStorage.setItem(ADMIN_KEY, key);
    else sessionStorage.removeItem(ADMIN_KEY);
  } catch {
    // 保存できなくても、この画面の間は使える。
  }
}

export class ApiError extends Error {
  constructor(
    message: string,
    readonly code?: string,
  ) {
    super(message);
  }
}

async function gql<T>(query: string, variables?: Record<string, unknown>): Promise<T> {
  const headers: Record<string, string> = {
    "content-type": "application/json",
    "X-Device-Id": deviceId(),
  };
  const key = adminKey();
  if (key) headers["X-Admin-Key"] = key;
  let res: Response;
  try {
    res = await fetch("/graphql", { method: "POST", headers, body: JSON.stringify({ query, variables }) });
  } catch {
    throw new ApiError("通信できませんでした。電波の良いところでもう一度試してください。", "NETWORK");
  }
  if (res.status === 429) throw new ApiError("操作が多すぎます。少し待ってから試してください。", "RATE_LIMIT");
  const body = (await res.json().catch(() => null)) as { data?: T; errors?: { message: string; extensions?: { code?: string } }[] } | null;
  if (!body) throw new ApiError("サーバーから応答がありませんでした。", "BAD_RESPONSE");
  if (body.errors?.length) {
    const e = body.errors[0]!;
    throw new ApiError(e.message, e.extensions?.code);
  }
  return body.data as T;
}

const CITY_MARK_FIELDS = `id x y weight shape genre stage status isMine isNew opacity popText popPhotoUrl popCount hasPr isShop shopId neighborIds createdAt`;
const POP_FIELDS = `id authorName text photoUrl stage hasPr isOriginal status createdAt`;
const MARK_FIELDS = `id shape genre stage status pendingReason isMine ownerName linkUrl domain hasLink hasPr linkBroken
  pops { ${POP_FIELDS} } preview { title siteName imageUrl } neighbors { id popText shape genre } saveId canAddPop shopId createdAt`;
const SNAPSHOT_FIELDS = `markId shape genre stage linkUrl domain hasPr title imageUrl popText popPhotoUrl popAuthorName
  neighbors { markId popText shape genre } capturedAt`;
const SAVE_FIELDS = `id savedAt presence currentX currentY inMyShop snapshot { ${SNAPSHOT_FIELDS} }`;
const ME_FIELDS = `hasDevice isRegistered userId displayName trust isKidsMode isAdmin shopId saveCount`;
const SHOP_FIELDS = `id name ownerName isPublished isMine markId markInCity
  items { id stage originalAuthorName originalMarkId originalInCity position snapshot { ${SNAPSHOT_FIELDS} } }`;

export const api = {
  city: () => gql<{ city: CityView }>(`{ city { baseRadius generatedAt marks { ${CITY_MARK_FIELDS} } } }`).then((d) => d.city),
  mark: (id: string) => gql<{ mark: MarkDetail }>(`query($id: UUID!) { mark(id: $id) { ${MARK_FIELDS} } }`, { id }).then((d) => d.mark),
  me: () => gql<{ me: Me }>(`{ me { ${ME_FIELDS} } }`).then((d) => d.me),
  saves: () => gql<{ saves: SaveView[] }>(`{ saves { ${SAVE_FIELDS} } }`).then((d) => d.saves),
  myShop: () => gql<{ myShop: Shop | null }>(`{ myShop { ${SHOP_FIELDS} } }`).then((d) => d.myShop),
  shop: (id: string) => gql<{ shop: Shop }>(`query($id: UUID!) { shop(id: $id) { ${SHOP_FIELDS} } }`, { id }).then((d) => d.shop),

  register: (displayName: string) =>
    gql<{ register: Me }>(`mutation($n: String!) { register(displayName: $n) { ${ME_FIELDS} } }`, { n: displayName }).then((d) => d.register),
  updateSettings: (isKidsMode: boolean) =>
    gql<{ updateSettings: Me }>(`mutation($k: Boolean!) { updateSettings(isKidsMode: $k) { ${ME_FIELDS} } }`, { k: isKidsMode }).then(
      (d) => d.updateSettings,
    ),
  placeMark: (input: {
    nextToMarkId: string | null;
    url: string | null;
    shape: Shape;
    genre: Genre;
    stage: Stage;
    text: string;
    photoKey: string | null;
  }) =>
    gql<{ placeMark: PlaceResult }>(
      `mutation($i: PlaceMarkInput!) { placeMark(input: $i) { markId status pendingReason message } }`,
      { i: input },
    ).then((d) => d.placeMark),
  addPop: (input: { markId: string; text: string; stage: Stage; photoKey: string | null }) =>
    gql<{ addPop: PopView }>(`mutation($i: AddPopInput!) { addPop(input: $i) { ${POP_FIELDS} } }`, { i: input }).then((d) => d.addPop),
  recordEngagements: (markIds: string[], kind: "VIEW" | "TAP") =>
    gql<{ recordEngagements: number }>(
      `mutation($ids: [UUID!]!, $k: EngagementKind!) { recordEngagements(markIds: $ids, kind: $k) }`,
      { ids: markIds, k: kind },
    ),
  saveMark: (markId: string) =>
    gql<{ saveMark: SaveView }>(`mutation($id: UUID!) { saveMark(markId: $id) { ${SAVE_FIELDS} } }`, { id: markId }).then((d) => d.saveMark),
  unsave: (saveId: string) => gql<{ unsave: boolean }>(`mutation($id: UUID!) { unsave(saveId: $id) }`, { id: saveId }),

  openShop: (name: string) =>
    gql<{ openShop: Shop }>(`mutation($n: String!) { openShop(name: $n) { ${SHOP_FIELDS} } }`, { n: name }).then((d) => d.openShop),
  renameShop: (name: string) =>
    gql<{ renameShop: Shop }>(`mutation($n: String!) { renameShop(name: $n) { ${SHOP_FIELDS} } }`, { n: name }).then((d) => d.renameShop),
  addShopItem: (saveId: string, stage: Stage) =>
    gql<{ addShopItem: Shop }>(`mutation($id: UUID!, $s: ExperienceStage!) { addShopItem(saveId: $id, stage: $s) { ${SHOP_FIELDS} } }`, {
      id: saveId,
      s: stage,
    }).then((d) => d.addShopItem),
  updateShopItem: (itemId: string, stage: Stage) =>
    gql<{ updateShopItem: Shop }>(
      `mutation($id: UUID!, $s: ExperienceStage!) { updateShopItem(itemId: $id, stage: $s) { ${SHOP_FIELDS} } }`,
      { id: itemId, s: stage },
    ).then((d) => d.updateShopItem),
  removeShopItem: (itemId: string) =>
    gql<{ removeShopItem: Shop }>(`mutation($id: UUID!) { removeShopItem(itemId: $id) { ${SHOP_FIELDS} } }`, { id: itemId }).then(
      (d) => d.removeShopItem,
    ),
  setShopPublished: (published: boolean, nextToMarkId: string | null) =>
    gql<{ setShopPublished: Shop }>(
      `mutation($p: Boolean!, $n: UUID) { setShopPublished(published: $p, nextToMarkId: $n) { ${SHOP_FIELDS} } }`,
      { p: published, n: nextToMarkId },
    ).then((d) => d.setShopPublished),

  report: (target: { markId?: string; popId?: string }, reason: string) =>
    gql<{ report: { accepted: boolean; message: string } }>(
      `mutation($m: UUID, $p: UUID, $r: String!) { report(markId: $m, popId: $p, reason: $r) { accepted message } }`,
      { m: target.markId ?? null, p: target.popId ?? null, r: reason },
    ).then((d) => d.report),
  requestTakedown: (markId: string, contact: string, reason: string) =>
    gql<{ requestTakedown: { accepted: boolean; message: string } }>(
      `mutation($m: UUID!, $c: String!, $r: String!) { requestTakedown(markId: $m, contact: $c, reason: $r) { accepted message } }`,
      { m: markId, c: contact, r: reason },
    ).then((d) => d.requestTakedown),

  adminQueue: () =>
    gql<{ adminQueue: AdminQueue }>(`{ adminQueue {
      marks { id status pendingReason reviewNote linkUrl domain ownerName ownerId popText reportWeight createdAt }
      pops { id markId authorName text reviewNote createdAt }
      reports { id kind markId popId reason contact weight createdAt }
      domains { domain kind note } } }`).then((d) => d.adminQueue),
  adminApproveMark: (id: string) => gql(`mutation($id: UUID!) { adminApproveMark(markId: $id) }`, { id }),
  adminHideMark: (id: string, note: string | null) =>
    gql(`mutation($id: UUID!, $n: String) { adminHideMark(markId: $id, note: $n) }`, { id, n: note }),
  adminApprovePop: (id: string) => gql(`mutation($id: UUID!) { adminApprovePop(popId: $id) }`, { id }),
  adminHidePop: (id: string) => gql(`mutation($id: UUID!) { adminHidePop(popId: $id) }`, { id }),
  adminResolveReport: (id: string, dismiss: boolean) =>
    gql(`mutation($id: UUID!, $d: Boolean!) { adminResolveReport(reportId: $id, dismiss: $d) }`, { id, d: dismiss }),
  adminSetDomainRule: (domain: string, kind: DomainRuleKind, note: string | null) =>
    gql(`mutation($d: String!, $k: DomainRuleKind!, $n: String) { adminSetDomainRule(domain: $d, kind: $k, note: $n) { domain } }`, {
      d: domain,
      k: kind,
      n: note,
    }),
  adminBlockUser: (userId: string) => gql(`mutation($id: UUID!) { adminBlockUser(userId: $id, blocked: true) }`, { id: userId }),

  uploadPhoto: async (file: File): Promise<{ key: string; url: string }> => {
    const form = new FormData();
    form.append("file", file);
    let res: Response;
    try {
      res = await fetch("/api/photos", { method: "POST", headers: { "X-Device-Id": deviceId() }, body: form });
    } catch {
      throw new ApiError("写真を送れませんでした。", "NETWORK");
    }
    const body = (await res.json().catch(() => null)) as { key?: string; url?: string; error?: string } | null;
    if (!res.ok || !body?.key) throw new ApiError(body?.error ?? "写真を送れませんでした。", "UPLOAD");
    return { key: body.key, url: body.url! };
  },
};
