# 05 — Jira Cloud REST API shapes (docs-only research, read 2026-11-12)

This is "research note 05", which the tickets, the spec and the fake Jira's comments cite. It was written during the implementation run and copied into the repository so that ticket 13 can compare a real site against it.

Nothing here is `verified`: no live Jira call was made. Every claim carries one of these tags.

- `U[Sx]` = **unverified (docs only, <url of Sx>)** — read in this session from the source listed below.
- `U[snippet]` = unverified (search-result snippet of an Atlassian community page; the page body could not be read).
- `MEM` = **unverified (memory, not found in docs)**. All `MEM` claims are repeated in the last section.

Sources (all fetched 2026-11-12):

| Key | URL |
|---|---|
| S1 | https://developer.atlassian.com/cloud/jira/platform/swagger-v3.v3.json (spec version `1001.0.0-SNAPSHOT-73c38b88...`; human page: https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issue-search/) |
| S2 | https://developer.atlassian.com/cloud/jira/software/swagger.v3.json (human page: https://developer.atlassian.com/cloud/jira/software/rest/) |
| S3 | https://developer.atlassian.com/cloud/jira/platform/rest/v3/intro/ |
| S4 | https://developer.atlassian.com/cloud/jira/platform/rate-limiting/ |
| S5 | https://developer.atlassian.com/cloud/jira/platform/basic-auth-for-rest-apis/ |
| S6 | https://developer.atlassian.com/cloud/jira/platform/search-and-reconcile/ |
| S7 | https://support.atlassian.com/jira-software-cloud/docs/jql-fields/ (Atlassian official product docs, not developer.atlassian.com) |
| S8 | https://developer.atlassian.com/cloud/jira/platform/deprecation-notice-tostring-representation-of-sprints-in-get-issue-response/ |
| S9 | https://developer.atlassian.com/changelog/#CHANGE-2046 — **could not be read** (JS-rendered, data API returns 401). Only its existence is known, from S1. |

## What to do

1. Search with `POST /rest/api/3/search/jql`. Always send `fields` explicitly; the default is `id` only. `U[S1]`
2. Page with `nextPageToken`. Stop when `isLast` is true **or** `nextPageToken` is absent **or** null. Never expect `total` or `startAt`. `U[S1]`
3. JQL must be bounded (have a search restriction), e.g. `project = FAKE AND updated >= "2026-01-05 09:30" ORDER BY updated ASC`. `U[S1]`
4. JQL dates have minute precision and are read in the **calling user's configured time zone**, not UTC. Subtract an overlap window and de-duplicate by issue `id`. `U[S7]`
   Owner decision D26 (2026-11-12, applied in ticket 09): `sync` sends `updated >= "yyyy-MM-dd HH:mm"`, the watermark (newest `updated` fetched) less 48 hours, formatted in UTC with the invariant culture. The 48 hours covers any account time zone (UTC-12 to UTC+14) and search-index lag. The unquoted epoch-millisecond form is not used. The fake Jira reads the quoted value in a settable account time zone (`FakeJira.AccountTimeZone`) and understands no other form. Still docs only: `unverified` against a real site.
5. Parse response timestamps with an offset-aware parser that accepts both `+0000` and `+10:00`. The offset is not guaranteed to be UTC. `U[S3]`
6. For `changelog/bulkfetch`, parse `created` as **either** an ISO string **or** an integer. The schema and the example disagree. `U[S1]`
7. Send at most 1000 issue ids and 10 field ids per bulkfetch request. `U[S1]`
8. Find the Sprint field in `GET /rest/api/3/field` by `schema.custom == "com.pyxis.greenhopper.jira:gh-sprint"`, never by name. `U[S2]`
9. Find the story points field from configuration or board configuration. The docs give no `schema.custom` value for it. `U[S2]`
10. On 429, wait at least `Retry-After` seconds, then back off exponentially with jitter. `U[S4]`
11. The fake Jira should serve both variants where docs are ambiguous: last page with the token absent and with it null, and `created` as string and as number.

## 1. JQL search — `GET` / `POST /rest/api/3/search/jql`

- It is the current endpoint: `deprecated: false`, summary "Search for issues using JQL enhanced search". `U[S1]`
- Old `GET`/`POST /rest/api/3/search`: `deprecated: true`, summary "Currently being removed", pointing to CHANGE-2046. `U[S1]`
  - The spec still lists only 200/400/401 for it and does not say what it returns now. `U[S1]`
  - Removal dates and the 410 status are not in any official page I could read. Community threads report `410` with "The requested API has been removed. Please migrate to the /rest/api/3/search/jql API." and a shutdown window of 1 Aug – 31 Oct 2025. `U[snippet]`
- Results are eventually consistent: "Recent updates might not be immediately visible". Most changes appear within seconds, bulk operations take longer. `U[S1]` `U[S6]`

Request (POST body = `SearchAndReconcileRequestBean`; GET uses the same names as query parameters) `U[S1]`:

| Name | Type | Notes |
|---|---|---|
| `jql` | string | Must be bounded. Unbounded example: `order by key desc`. `ORDER BY` takes at most 7 fields. |
| `nextPageToken` | string | Omit on the first page. |
| `maxResults` | int32 | Default 50. No `maximum` in the schema. Text says "It returns max 5000 issues" and that the API "may return fewer items per page" when many fields are requested; the largest pages come with `id` or `key` only. |
| `fields` | string[] | Default `id`. Accepts `*all`, `*navigable`, field ids, `-field` to exclude. In GET, a comma-separated list. |
| `expand` | string | Comma-delimited **string**, not array: `renderedFields,names,schema,transitions,operations,editmeta,changelog,versionedRepresentations`. |
| `properties` | string[] | Up to 5 issue properties. |
| `fieldsByKeys` | boolean | Default false. |
| `failFast` | boolean | GET only in the spec. Default false. |
| `reconcileIssues` | int64[] | Up to 50 issue ids for read-after-write. Keep the list the same across pages. |
| `includeArchivedProjects` | boolean | Default false. |

Response (`SearchAndReconcileResults`, `additionalProperties: false`) `U[S1]`:

| Field | Type | Notes |
|---|---|---|
| `issues` | IssueBean[] | |
| `nextPageToken` | string | Conflict inside the spec: the parameter text says it is "**not included** in the response for the last page"; the schema text says it "will be null". Expires in 7 days. |
| `isLast` | boolean | "Indicates whether this is the last page". |
| `names`, `schema` | object | Only with `expand`. |
| `warnings` | array | Experimental, behind a feature flag, may be absent. Items: `type`, `message`, `details`. |

- There is no `total`, `startAt` or `maxResults` in the response. `U[S1]`
- For a count use `POST /rest/api/3/search/approximate-count` with `{"jql": "..."}`; it returns `{"count": 153}` and also needs bounded JQL. `U[S1]`
- The docs do not say whether pages can be fetched in parallel. Tokens come from the previous page, so paging is sequential. `U[S6]`
- A JQL error returns 400 "Returned if the search request is invalid". The 400 body shape is not in the spec. `U[S1]`

JQL `updated` / `created` `U[S7]`:

- Formats: `"yyyy/MM/dd HH:mm"`, `"yyyy-MM-dd HH:mm"`, `"yyyy/MM/dd"`, `"yyyy-MM-dd"`, or relative (`-1d`, units `w d h m`, default `m`).
- Precision is minutes; no seconds format is listed. Without a time, midnight is assumed.
- "search results will be relative to your configured time zone (which is by default the Jira server's time zone)".
- The value must be quoted; unquoted numbers are read as epoch milliseconds.
- Operators: `= != > >= < <=`, `IS`, `IN`.

Fixture, page 1 (request body `{"jql":"project = FAKE AND updated >= \"2026-01-05 09:30\" ORDER BY updated ASC","maxResults":2,"fields":["issuetype","status","resolution","assignee","created","updated","customfield_10020","customfield_10016"]}`):

```json
{
  "issues": [
    {
      "expand": "",
      "id": "900001",
      "key": "FAKE-1",
      "self": "https://example.invalid/rest/api/3/issue/900001",
      "fields": {
        "issuetype": { "self": "https://example.invalid/rest/api/3/issuetype/800001", "id": "800001", "name": "Story", "description": "synthetic", "iconUrl": "https://example.invalid/icon.png", "subtask": false, "avatarId": 700001, "hierarchyLevel": 0 },
        "status": {
          "self": "https://example.invalid/rest/api/3/status/600002", "id": "600002", "name": "Fake In Progress", "description": "synthetic", "iconUrl": "https://example.invalid/icon.png",
          "statusCategory": { "self": "https://example.invalid/rest/api/3/statuscategory/4", "id": 4, "key": "indeterminate", "colorName": "yellow", "name": "In Progress" }
        },
        "resolution": null,
        "assignee": { "self": "https://example.invalid/rest/api/3/user?accountId=fake-account-0001", "accountId": "fake-account-0001", "accountType": "atlassian", "active": true, "displayName": "Fake Person One", "emailAddress": "fake.one@example.invalid", "timeZone": "Etc/UTC" },
        "created": "2026-01-05T09:30:00.000+0000",
        "updated": "2026-01-06T10:15:00.000+0000",
        "customfield_10020": [
          { "id": 500001, "name": "Fake Sprint 1", "state": "closed", "boardId": 400001, "goal": "synthetic goal", "startDate": "2025-12-22T09:00:00.000Z", "endDate": "2026-01-05T09:00:00.000Z", "completeDate": "2026-01-05T09:05:00.000Z" },
          { "id": 500002, "name": "Fake Sprint 2", "state": "active", "boardId": 400001, "startDate": "2026-01-05T09:10:00.000Z", "endDate": "2026-01-19T09:00:00.000Z" }
        ],
        "customfield_10016": 3.0
      }
    },
    {
      "expand": "",
      "id": "900002",
      "key": "FAKE-2",
      "self": "https://example.invalid/rest/api/3/issue/900002",
      "fields": {
        "issuetype": { "id": "800002", "name": "Sub-task", "subtask": true, "hierarchyLevel": -1 },
        "status": { "id": "600003", "name": "Fake Done", "statusCategory": { "id": 3, "key": "done", "colorName": "green", "name": "Done" } },
        "resolution": { "self": "https://example.invalid/rest/api/3/resolution/300001", "id": "300001", "name": "Done", "description": "synthetic" },
        "assignee": null,
        "created": "2026-01-05T11:00:00.000+0000",
        "updated": "2026-01-07T08:00:00.000+0000",
        "customfield_10020": null,
        "customfield_10016": null
      }
    }
  ],
  "nextPageToken": "FAKE_TOKEN_PAGE_2",
  "isLast": false
}
```

Fixture, page 2 = last page (request is the same body plus `"nextPageToken":"FAKE_TOKEN_PAGE_2"`). Variant A omits the token; also test variant B with `"nextPageToken": null`:

```json
{
  "issues": [
    { "expand": "", "id": "900003", "key": "FAKE-3", "self": "https://example.invalid/rest/api/3/issue/900003",
      "fields": { "issuetype": { "id": "800003", "name": "Epic", "subtask": false, "hierarchyLevel": 1 },
        "status": { "id": "600001", "name": "Fake To Do", "statusCategory": { "id": 2, "key": "new", "colorName": "blue-gray", "name": "To Do" } },
        "resolution": null, "assignee": { "accountId": "fake-account-0002", "displayName": "Fake Person Two" },
        "created": "2026-01-06T09:00:00.000+0000", "updated": "2026-01-08T09:00:00.000+0000",
        "customfield_10020": null, "customfield_10016": null } }
  ],
  "isLast": true
}
```

With no `fields` in the request, each issue is expected to carry only `id` (default "returns IDs only"). `U[S1]` Whether `key`, `self` and an empty `fields` object still appear is not stated.

**Correction from ticket 01's review (2026-11-12, finding 24), `unverified`:** asking for `maxResults` 5000 does not give
5000 issues per page when fields are named. The docs' "max 5000" applies to pages carrying the ID or key alone; with
named fields a real Jira Cloud site is reported to cap a page near 100 (review claim, not found in the pages read here).
The collector sends 5000 as a request, not a promise, and stops with "more than one page" when a `nextPageToken` comes
back. A board of ordinary size therefore needs ticket 10's paging before `run` can finish against a real site.

## 2. Bulk changelog — `POST /rest/api/3/changelog/bulkfetch`

Request (`BulkChangelogRequestBean`) `U[S1]`:

| Name | Type | Notes |
|---|---|---|
| `issueIdsOrKeys` | string[] | **Required.** 1 to 1000 items. More than 1000, or none, returns 400. |
| `fieldIds` | string[] | 0 to 10 items, unique. Filters histories to these field ids (e.g. `status`, `customfield_10020`). |
| `maxResults` | int32 | Default 1000, min 1, max 10000. "items to return per page"; the docs do not say whether an item is a history or a change item. |
| `nextPageToken` | string | "The cursor for pagination". |

Response (`BulkChangelogResponseBean`) `U[S1]`:

- `issueChangeLogs[]`: each has `issueId` (string; the **id**, even when keys were sent) and `changeHistories[]`.
- `changeHistories[]` item (`Changelog`): `id` (string), `author` (UserDetails), `created`, `items[]`, optional `historyMetadata`.
- `items[]` (`ChangeDetails`), all typed string: `field`, `fieldId`, `fieldtype`, `from`, `fromString`, `to`, `toString`. The spec's example for the per-issue changelog shows `from` and `to` as `null`.
- `nextPageToken` (string): "If this result represents the last or the only page, this token will be null." No `isLast`, no `total`.
- Order: "sorted by changelog date and issue IDs, starting from the oldest changelog and smallest issue ID". One issue can therefore appear on several pages; merge by `issueId`.
- **`created` type is ambiguous.** The schema says `type: string, format: date-time`. The endpoint's own example shows a bare number: `"created":1492070429`. The per-issue endpoint (`GET /issue/{id}/changelog`) example shows `"created":"1970-01-18T06:27:50.429+0000"`, which is that same number read as epoch milliseconds. The docs do not settle it.
- Which form a real site returns: number of epoch milliseconds. `MEM`
- No expiry is stated for this token (the 7-day statement is for search only). `U[S1]`

Fixture, page 1 (request `{"issueIdsOrKeys":["900001","900002"],"fieldIds":["status","customfield_10020"],"maxResults":2}`), shown with the schema's string form:

```json
{
  "issueChangeLogs": [
    {
      "issueId": "900001",
      "changeHistories": [
        {
          "id": "200001",
          "author": { "self": "https://example.invalid/rest/api/3/user?accountId=fake-account-0001", "accountId": "fake-account-0001", "displayName": "Fake Person One", "emailAddress": "fake.one@example.invalid", "active": true, "timeZone": "Etc/UTC" },
          "created": "2026-01-05T10:00:00.000+0000",
          "items": [
            { "field": "status", "fieldtype": "jira", "fieldId": "status", "from": "600001", "fromString": "Fake To Do", "to": "600002", "toString": "Fake In Progress" }
          ]
        }
      ]
    },
    {
      "issueId": "900002",
      "changeHistories": [
        {
          "id": "200002",
          "author": { "accountId": "fake-account-0002", "displayName": "Fake Person Two", "active": true },
          "created": "2026-01-05T12:00:00.000+0000",
          "items": [
            { "field": "Sprint", "fieldtype": "custom", "fieldId": "customfield_10020", "from": "", "fromString": "", "to": "500002", "toString": "Fake Sprint 2" }
          ]
        }
      ]
    }
  ],
  "nextPageToken": "FAKE_CHANGELOG_TOKEN_2"
}
```

Fixture, page 2 = last page (request adds `"nextPageToken":"FAKE_CHANGELOG_TOKEN_2"`), shown with the example's numeric form; also test the token as explicit `null`:

```json
{
  "issueChangeLogs": [
    {
      "issueId": "900001",
      "changeHistories": [
        { "id": "200003", "author": { "accountId": "fake-account-0001", "displayName": "Fake Person One", "active": true },
          "created": 1767780000000,
          "items": [ { "field": "status", "fieldtype": "jira", "fieldId": "status", "from": "600002", "fromString": "Fake In Progress", "to": "600003", "toString": "Fake Done" } ] }
      ]
    }
  ]
}
```

The Sprint item values (`from`/`to` as comma-separated sprint ids, `fieldtype` `custom`) are `MEM`.

## 3. Sprints — `GET /rest/agile/1.0/board/{boardId}/sprint`

- Query: `startAt` (int64, base 0), `maxResults` (int32, no default or max stated), `state` (`future`, `active`, `closed`; comma-separated, e.g. `state=active,closed`). `U[S2]`
- Envelope in the example: `isLast`, `maxResults`, `startAt`, `total`, `values[]`. `U[S2]`
- `total` may be missing "if it is too expensive to calculate"; "consider the field to be optional". A page can be empty. Page on `isLast`, with `values.length == 0` as a fallback stop. `U[S2]`
- The server may return fewer than requested. `U[S2]` Default page size 50. `MEM`
- Order: "first by state (i.e. closed, active, future) then by their position in the backlog". `U[S2]`
- Sprint (`SprintBean`): `id` int64, `self` uri, `state` string, `name`, `startDate`, `endDate`, `completeDate`, `createdDate` (all plain `string`, no format given), `originBoardId` int64, `goal`. `U[S2]`
- Future sprints in the example have no `startDate`, `endDate`, `completeDate` or `originBoardId` (fields absent, not null). `U[S2]`
- Date format in the example: `2015-04-11T15:22:00.000+10:00` (offset **with** colon). `createdDate` is in the schema but not in the example. `U[S2]`
- Real sites return these as UTC with `Z`, e.g. `2026-01-05T09:10:00.000Z`. `MEM`
- Errors: 400 invalid, 401 not logged in, 403 no licence, 404 board missing or not visible. `U[S2]`

```json
{
  "maxResults": 2,
  "startAt": 0,
  "total": 3,
  "isLast": false,
  "values": [
    { "id": 500001, "self": "https://example.invalid/rest/agile/1.0/sprint/500001", "state": "closed", "name": "Fake Sprint 1",
      "startDate": "2025-12-22T09:00:00.000Z", "endDate": "2026-01-05T09:00:00.000Z", "completeDate": "2026-01-05T09:05:00.000Z",
      "createdDate": "2025-12-20T09:00:00.000Z", "originBoardId": 400001, "goal": "synthetic goal" },
    { "id": 500002, "self": "https://example.invalid/rest/agile/1.0/sprint/500002", "state": "active", "name": "Fake Sprint 2",
      "startDate": "2026-01-05T09:10:00.000Z", "endDate": "2026-01-19T09:00:00.000Z",
      "createdDate": "2026-01-02T09:00:00.000Z", "originBoardId": 400001, "goal": "" }
  ]
}
```

Last page: `{"maxResults":2,"startAt":2,"total":3,"isLast":true,"values":[{"id":500003,"self":"https://example.invalid/rest/agile/1.0/sprint/500003","state":"future","name":"Fake Sprint 3","originBoardId":400001}]}`

## 4. Reference data

### `GET /rest/api/3/status`

- Returns a bare JSON **array** of `StatusDetails`, not paged; "all statuses associated with active workflows". Not deprecated. `U[S1]`
- Fields: `id` (**string**), `name`, `description`, `iconUrl`, `self`, `statusCategory`, `scope`. `U[S1]`
- `statusCategory`: `id` (**int64 number**), `key`, `name`, `colorName`, `self`. `U[S1]`
- `scope`: `type` (`PROJECT` or `TEMPLATE`) and `project` (`{ "id": "..." }`). `U[S1]` Present only for team-managed project statuses. `MEM`
- The doc example uses category keys `in-flight` and `completed`, and omits `name` on one. `U[S1]` Real keys are `new` (id 2), `indeterminate` (id 4), `done` (id 3), `undefined` (id 1). `MEM`
- `GET /rest/api/3/statuses/search` is paged (`startAt`, `maxResults` default 200, `projectId`, `searchString`, `statusCategory`), but needs *Administer projects* or *Administer Jira*, and its `statusCategory` is a plain string enum `TODO | IN_PROGRESS | DONE`. Do not use it for a normal user token. `U[S1]`

```json
[
  { "self": "https://example.invalid/rest/api/3/status/600001", "id": "600001", "name": "Fake To Do", "description": "synthetic", "iconUrl": "https://example.invalid/icon.png",
    "statusCategory": { "self": "https://example.invalid/rest/api/3/statuscategory/2", "id": 2, "key": "new", "colorName": "blue-gray", "name": "To Do" } },
  { "self": "https://example.invalid/rest/api/3/status/600004", "id": "600004", "name": "Fake Team Done", "description": "", "iconUrl": "https://example.invalid/icon.png",
    "statusCategory": { "self": "https://example.invalid/rest/api/3/statuscategory/3", "id": 3, "key": "done", "colorName": "green", "name": "Done" },
    "scope": { "type": "PROJECT", "project": { "id": "100001" } } }
]
```

### `GET /rest/api/3/resolution`

- **Deprecated** (`deprecated: true`) but still documented. Returns a bare array of `Resolution`: `id` (string), `name`, `description`, `self`. `U[S1]`
- Replacement: `GET /rest/api/3/resolution/search`, paged: query `startAt` (default "0"), `maxResults` (default "50"), `id[]`, `onlyDefault`; envelope `isLast`, `maxResults`, `startAt`, `total`, `values[]`, optional `nextPage`, `self`. `U[S1]`
- Item fields there: `id`, `name`, `description`, `iconUrl`, `self`, and a default flag. The schema names it `default`; the example names it `isDefault`. Read neither. `U[S1]`

```json
{ "maxResults": 50, "startAt": 0, "total": 2, "isLast": true,
  "values": [
    { "id": "300001", "name": "Done", "description": "synthetic", "isDefault": false },
    { "id": "300002", "name": "Won't Do", "description": "synthetic", "isDefault": false }
  ] }
```

Old array form: `[{"self":"https://example.invalid/rest/api/3/resolution/300001","id":"300001","name":"Done","description":"synthetic"}]`

### `GET /rest/api/3/field`

- Bare array of `FieldDetails`, not paged, not deprecated. Only returns fields the user may see. `U[S1]`
- Fields: `id`, `key`, `name`, `custom` (bool), `orderable`, `navigable`, `searchable`, `clauseNames` (string[]), `schema`, `scope`. `U[S1]`
- `schema`: `type`, `items` (when `type` is `array`), `system` (system fields), `custom` (custom field type URI), `customId` (int64), `configuration`. `U[S1]`
- Sprint field: "identified by `com.pyxis.greenhopper.jira:gh-sprint`", id is like `customfield_10007`; "custom field names are mutable". `U[S2]`
- Story points: "Jira Software provides a `Story Points` custom field, however the field is just a regular numeric field. The type of estimation and field used for estimation is determined by the board configuration" (`GET /rest/agile/1.0/board/{boardId}/configuration` → `estimation.field.fieldId`, `estimation.field.displayName`). `U[S2]`
- The docs give no `schema.custom` for story points and do not mention "Story point estimate" at all. `U[S1]` `U[S2]`
- "Story Points" (company-managed) has `schema.custom` `com.atlassian.jira.plugin.system.customfieldtypes:float`; "Story point estimate" (team-managed) has `com.pyxis.greenhopper.jira:jsw-story-points`. `MEM`

```json
[
  { "id": "status", "key": "status", "name": "Status", "custom": false, "orderable": false, "navigable": true, "searchable": true,
    "clauseNames": ["status"], "schema": { "type": "status", "system": "status" } },
  { "id": "customfield_10020", "key": "customfield_10020", "name": "Sprint", "custom": true, "orderable": true, "navigable": true, "searchable": true,
    "clauseNames": ["cf[10020]", "Sprint"],
    "schema": { "type": "array", "items": "json", "custom": "com.pyxis.greenhopper.jira:gh-sprint", "customId": 10020 } },
  { "id": "customfield_10016", "key": "customfield_10016", "name": "Story point estimate", "custom": true, "orderable": true, "navigable": true, "searchable": true,
    "clauseNames": ["cf[10016]", "Story point estimate"],
    "schema": { "type": "number", "custom": "com.pyxis.greenhopper.jira:jsw-story-points", "customId": 10016 } },
  { "id": "customfield_10026", "key": "customfield_10026", "name": "Story Points", "custom": true, "orderable": true, "navigable": true, "searchable": true,
    "clauseNames": ["cf[10026]", "Story Points"],
    "schema": { "type": "number", "custom": "com.atlassian.jira.plugin.system.customfieldtypes:float", "customId": 10026 } }
]
```

In this fixture `"items": "json"` and the `cf[...]` clause names are `MEM`.

## 5. Issue fields in a search result

`IssueBean.fields` is a free-form object in the spec (`additionalProperties: {}`), and the spec's search example contains none of the fields below. Shapes come from the component schemas. `U[S1]`

| Path | Shape | Tag |
|---|---|---|
| `id` | string | `U[S1]` |
| `key` | string | `U[S1]` |
| `fields.issuetype` | `IssueTypeDetails`: `id` string, `name`, `description`, `iconUrl`, `self`, `subtask` boolean, `avatarId` int64, `hierarchyLevel` int32, optional `entityId` (uuid), `scope` | `U[S1]` |
| `hierarchyLevel` values | `-1` Subtask, `0` Base, `1` Epic — documented for the `level` parameter of `GET /rest/api/3/issuetype/project`; elsewhere "0, 1, 2, 3 .. n; Negative values for subtasks". Levels above 1 can exist. | `U[S1]` |
| `fields.status` | `StatusDetails` as in section 4: `id` string, `statusCategory.id` number | `U[S1]` |
| `fields.resolution` | `{ id, name, description, self }` when resolved | `U[S1]` |
| `fields.resolution` unresolved | `null` | `MEM` |
| `fields.assignee` | `UserDetails`: `accountId`, `displayName`, `active`, `accountType`, `avatarUrls`, `self`, `emailAddress`, `timeZone`. `emailAddress` and `timeZone` "may be returned as null" depending on privacy settings; `displayName` "may return an alternative value". | `U[S1]` |
| `fields.assignee` unassigned | `null`; `emailAddress` often absent rather than null | `MEM` |
| `fields.created`, `fields.updated` | "ISO 8601 format, in the system default user time zone". Doc examples of other timestamps look like `2021-01-17T12:34:00.000+0000` (milliseconds, offset **without** colon). The spec's example for `fields.updated` is the junk value `1`. | `U[S3]` `U[S1]` |
| Sprint field | array of `{ id (number), name, state (lower case), boardId (number), goal?, startDate?, endDate?, completeDate? }`; unset dates are absent. Since 1 Sept 2020 it is JSON, not a `toString` text. Doc example dates: `2016-06-06T21:30:53.537+10:00`. | `U[S8]` |
| Sprint field when never in a sprint | `null` | `MEM` |
| Story points | "a regular numeric field" → JSON number; `null` when unset is `MEM` | `U[S2]` |

The Sprint field value uses `boardId`; the Agile sprint endpoint uses `originBoardId`. They are different shapes. `U[S8]` `U[S2]`

## 6. Basic auth (email + API token)

- Build `useremail:api_token`, base64-encode it, send `Authorization: Basic <base64>`. `U[S5]`
- The doc's example: `fred:fred` → `Authorization: Basic ZnJlZDpmcmVk`. `U[S5]`
- Send the header on the first request; Jira does not issue an authentication challenge. In .NET set the header directly; do not rely on `Credentials`. `U[S5]`
- Password basic auth is deprecated; an API token is required. Tokens work with 2FA and SAML. `U[S5]`
- Repeated failures trigger CAPTCHA; the sign is header `X-Seraph-LoginReason: AUTHENTICATION_DENIED`. `U[S5]`
- The page says nothing about token expiry, scoped tokens or the `api.atlassian.com` URL form. `U[S5]`
- Wrong or missing credentials on search return 401. `U[S1]`
- Fixture: `fake.one@example.invalid:fake-token-0001` → `Authorization: Basic ZmFrZS5vbmVAZXhhbXBsZS5pbnZhbGlkOmZha2UtdG9rZW4tMDAwMQ==`

## 7. Rate limiting

All `U[S4]`:

- Over the limit returns **429**. Some transient 503s may also carry `Retry-After`.
- `Retry-After`: **seconds** (integer), only on 429. Use it as the minimum delay.
- `X-RateLimit-Limit`, `X-RateLimit-Remaining`; `X-RateLimit-Reset` is an **ISO 8601 timestamp** (e.g. `2026-01-01T01:01:01Z`), only on 429; `X-RateLimit-NearLimit: true` below 20% left.
- `RateLimit-Reason` (only on 429): `jira-quota-global-based`, `jira-quota-tenant-based`, `jira-burst-based`, `jira-per-issue-on-write`.
- Informational beta headers may appear: `Beta-RateLimit-Policy`, `Beta-RateLimit`, `Beta-Retry-After`, `X-Beta-RateLimit-*`. Ignore them.
- Backoff: exponential with jitter; double each time; jitter factor 0.7–1.3; about 4 retries; cap 30 s (prose suggests starting near 2 s, the pseudocode starts at 5 s). Retry only idempotent calls.
- By reason: burst → slow that endpoint only; quota → pause all calls until reset.
- 2025–2026 change: points-based hourly quotas (65,000 points/hour default global pool; 1 point per request plus 1 per object, 2 per identity object; reset at the top of each UTC hour). Enforcement "will begin on March 2, 2026", for Forge, Connect and OAuth 2.0 (3LO) apps.
- "API token-based traffic is not affected by this change, and will continue to be governed by existing burst rate limits." A collector on basic auth sees burst limits only.
- Burst limits are per tenant, per endpoint, per second, token-bucket. Defaults: GET 100/s, POST 100/s, PUT 50/s, DELETE 50/s; `POST .../search/approximate-count` 150/s.
- The page shows no 429 response **body**. The body below is `MEM`; do not parse it.

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 1
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 2026-01-05T09:30:01Z
RateLimit-Reason: jira-burst-based

{"errorMessages":["Rate limit exceeded."],"errors":{}}
```

## Pitfalls

- Asking for no `fields` gives ids only; code written for the old endpoint silently gets empty issues. `U[S1]`
- `expand` is a comma-separated string in `search/jql`, but `fields` is an array in POST. `U[S1]`
- Ids are strings (`issue.id`, `status.id`, `issuetype.id`, `resolution.id`, history `id`) except `statusCategory.id`, sprint `id`, `boardId`, `originBoardId`, `schema.customId`, `avatarId`, which are numbers. `U[S1]` `U[S2]`
- Three date styles appear in the docs: `...+0000` (platform), `...+10:00` (sprints), and a bare number (bulkfetch example). Use one tolerant parser. `U[S1]` `U[S2]`
- JQL `updated >=` is in the user's time zone with minute precision, while responses are in the "system default user time zone". Never build the JQL literal by formatting a UTC instant without converting. `U[S7]` `U[S3]`
- Search is eventually consistent; a just-updated issue can be missing. Use an overlap window on the incremental cursor. `U[S6]`
- `nextPageToken` for search expires in 7 days; do not persist it as a resume cursor. `U[S1]`
- Bulkfetch output is ordered by date then issue id, not grouped per issue across pages. `U[S1]`
- Bulkfetch returns `issueId` only, no key; keep an id→key map from search. `U[S1]`
- Page sizes are caps the server may lower "without notice"; never compute page count from the requested `maxResults`. `U[S3]`
- `GET /rest/api/3/resolution` is deprecated; prefer `/resolution/search`. `U[S1]`
- User fields can be withheld by privacy settings; treat everything except `accountId` as optional. `U[S1]`

## Memory-only and ambiguous claims

Memory-only (`MEM`, not found in any page or spec read in this session):

1. Bulkfetch `created` is an epoch-milliseconds **number** on real sites.
2. Sprint changelog items carry sprint ids in `from`/`to` (comma-separated) with `fieldtype` `custom`.
3. Default sprint page size is 50.
4. Agile sprint dates come back as UTC with `Z`.
5. Real status category keys/ids: `new`=2, `indeterminate`=4, `done`=3, `undefined`=1, and their `colorName` values.
6. `scope` on a status appears only for team-managed projects.
7. Story points `schema.custom` values: `...customfieldtypes:float` ("Story Points") and `com.pyxis.greenhopper.jira:jsw-story-points` ("Story point estimate").
8. Sprint field `schema.items` is `json`; `clauseNames` include `cf[NNNNN]`.
9. `fields.resolution`, `fields.assignee`, the Sprint field and story points are `null` when unset; `emailAddress` is often absent.
10. The 429 response body shape.

Ambiguous or unread in the docs:

1. Old `/rest/api/3/search`: official text says only "currently being removed". CHANGE-2046 could not be read, so the removal date and the 410 response are known from community snippets only.
2. Search last page: token "not included" versus "will be null". Handle both, plus `isLast`.
3. Search `maxResults` maximum: "It returns max 5000 issues" with no schema maximum; the real per-page cap with many fields is not stated.
4. Bulkfetch `created`: string `date-time` in the schema, number in the example.
5. Bulkfetch `maxResults` unit (histories or items) is not stated.
6. Whether a default search issue (no `fields`) still includes `key`, `self`, `fields`.
7. Exact `fields.created`/`fields.updated` format: only "ISO 8601, system default user time zone"; the `+0000` no-colon form is seen in other examples, not for these fields.
8. `hierarchyLevel` meanings on an issue's `issuetype` are documented only via the `level` query parameter of another endpoint.
9. Resolution default flag name: `default` (schema) versus `isDefault` (example).
10. Sprint `createdDate` format and sprint `maxResults` default/max are not stated.
11. JQL time zone: "your configured time zone (by default the Jira server's)" — taken to mean the authenticated user's profile time zone.

## Added by ticket 11 (2026-11-12): board configuration — `GET /rest/agile/1.0/board/{boardId}/configuration`

Documented-only, and written from memory of Atlassian's page, which could not be read from this machine: every claim here is `unverified` (`MEM`).

- Not paged. Returns one object: `id`, `name`, `type`, `self`, `filter` (`{ id, self }`), `columnConfig`, `estimation`, `ranking`. `unverified`
- `columnConfig.columns[]`: each has `name` and `statuses[]`, and each status is `{ "id": "<status id as a string>", "self": "<uri>" }`. Columns are in board order, left to right. `columnConfig.constraintType` is also present. `unverified`
- A status mapped to no column is absent. A column may have no statuses. `unverified`
- `estimation.field.fieldId` and `estimation.field.displayName` name the board's story point field (also in section 4 above). `discover` does not read it. `unverified`
- Use in the tool (owner decision D29): `discover` takes the board's status IDs from `columnConfig.columns[].statuses[].id`, in that order, and joins them to `GET /rest/api/3/status` for name and category. Any other shape stops `discover` with "Jira's board configuration answered in a shape this tool does not understand". The first run on the work laptop is the check.

```json
{ "id": 400001, "name": "Fake Board", "type": "scrum",
  "columnConfig": { "columns": [
      { "name": "Fake To Do", "statuses": [ { "id": "600001", "self": "https://example.invalid/rest/api/3/status/600001" } ] },
      { "name": "Fake Done", "statuses": [ { "id": "600008", "self": "https://example.invalid/rest/api/3/status/600008" } ] } ],
    "constraintType": "issueCount" },
  "estimation": { "type": "field", "field": { "fieldId": "customfield_10016", "displayName": "Fake Story point estimate" } },
  "ranking": { "rankCustomFieldId": 10019 } }
```

## Added by ticket 10 (2026-11-12): what the collector now does with pages, rate limits and old history

Nothing here was verified against a real site; every Jira shape below stays `unverified`. This records what the code accepts.

- Search and bulkfetch are read until a page gives no `nextPageToken` (absent or null). The search's `isLast`, when present, must agree with the token; a disagreement, or a token given twice, stops the run. This replaces the finding-24 paragraph above: a further page no longer stops the run.
- The search asks for `maxResults` 100 (the reported cap with named fields), bulkfetch for 10000.
- An issue met on two search pages is kept once, as last seen.
- The sprint list is read with `?startAt=N` and no `maxResults`. A page is the last when `isLast` is true; without `isLast`, when `startAt + values.length >= total`; with neither, at the first empty page. A page whose `startAt` differs from the one asked for, an empty page that says more follow, or a page holding only sprints already given stops the run.
- 429: `Retry-After` as seconds or as an HTTP date is waited out (at least 1 s, at most 5 min; longer ends the run); without a readable header the waits are 5, 10, 20, 40, 80 s. Five retries per request. 503 is not retried. The HTTP-date form is exercised by a test now (note 06 had it `unverified`).
- Changelog items without `fieldId` (`MEM`: old history on real sites) are read by `field`: `status` with `fieldtype` other than `custom` is the status field; `Sprint` is the Sprint field. A site whose Sprint field was renamed loses the Sprint items of such history. Whether bulkfetch's `fieldIds` filter returns items without a field ID at all is unknown; the fake returns them.

Changed by ticket 10's review round (2026-11-12), all still `unverified` against a real site:

- The search JQL ends `ORDER BY updated ASC, id ASC`.
- An empty search or bulkfetch page that still gives a `nextPageToken` stops the run.
- Sprint list: `isLast` and `total` must agree when both are given, and with `total` given the distinct sprints read must equal it; otherwise the run stops.
- Items without `fieldId` are read only as (`field` "status", `fieldtype` "jira") and (`field` "Sprint", `fieldtype` "custom"). A status change's `from` and `to` must be digits or empty.
- `Retry-After` present but unreadable ends the run. Each wait is announced on standard error; one command waits at most 15 minutes in all.
