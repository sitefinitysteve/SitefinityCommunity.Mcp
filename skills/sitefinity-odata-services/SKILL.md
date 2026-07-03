---
name: sitefinity-odata-services
description: Use this skill when querying Sitefinity content over REST/OData, hitting the /api/default endpoints, reading the sfhelp service document, exposing content types or Module Builder dynamic module data through web services, answering $filter/$expand/$select/paging questions, or deciding between the built-in default OData web service and writing a custom API.
---

You are a Sitefinity built-in OData web services expert, targeting the frontend content API that Sitefinity exposes out of the box at `/api/default` on classic Sitefinity (.NET Framework 4.8, MVC / Feather-era sites). This is the zero-code path to read and query existing CMS content - native modules (news, events, media) and Module Builder dynamic types - over HTTP as OData v4. The facts below were verified against the Progress documentation; where a claim could not be confirmed in the docs it is flagged inline.

**Version baseline: Sitefinity 15.4** - the default service, route names, and query surface are stable across the recent 13.x-15.x line, but the set of auto-exposed types and per-type options shift between releases, so verify on older versions. Check the target project's version: `(Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion` (e.g. `15.4.8630.0` = Sitefinity 15.4).

## Three API surfaces - pick the right one first

Sitefinity exposes three distinct HTTP surfaces. Confusing them is the most common early mistake:

1. **Backend OData** at `{domain}/sf/system/...` - runs as the **authenticated backend user**, used by the admin UI and AdminApp extensions (auth injected via `HTTP_PREFIX`). Covered by `sitefinity-adminapp-extensions`. Not for anonymous frontend callers.
2. **Custom ServiceStack services** at `{domain}/RestApi/...` - hand-written C# services for custom logic, writes, aggregation, or shapes the built-in service cannot express. Covered by `sitefinity-servicestack-api`.
3. **Frontend default OData service** at `{domain}/api/default/...` - **this skill**. The built-in, configuration-driven content API. Query existing content with **zero code**.

**Decision rule:** need to **READ or query existing content** (list, filter, sort, page, expand related items) -> use the built-in OData service, write no C#. Need **custom logic, writes with side effects, cross-source aggregation, or a bespoke response shape** -> write a ServiceStack service (`sitefinity-servicestack-api`). Reaching for a custom controller just to return a filtered list of news items is wasted effort - `/api/default/newsitems?$filter=...` already does it.

## Where the default service comes from

Sitefinity ships a web service named **`default`** out of the box. Its URL is composed of two configurable segments plus the type:

```
{domain}/{RouteUrlName}/{ServiceUrlName}/{TypeUrlName}
         └── "api" ─────┘└── "default" ──┘└─ e.g. "newsitems"
```

- The **route** (`RouteUrlName`) defaults to `api`, configured under **Administration > Settings > Advanced > WebServices > Routes > Frontend**. Its `UrlName` must be a single unique segment with no special characters.
- The **service** (`ServiceUrlName`) defaults to `default`, managed under **Administration > Web services**.
- Together they yield the base URL **`/api/default`**.

**Gotcha:** the out-of-the-box `default` service is active and populated, but a service you **create manually** (Administration > Web services > *Create a web service*) is **deactivated by default** - you must tick *This service is active* before any of its routes respond.

## `/api/default/sfhelp` - the self-describing catalog

The service publishes human-readable API documentation at:

```http
GET /api/default/sfhelp
```

This is the **discovery entry point** for everything the service exposes. It enumerates **every entity set on the service - native modules AND Module Builder dynamic types** - because the documentation is generated from the types actually configured on the service (see "Configuring exposed types"). That makes it the ideal first stop for an agent or LLM tool: one fetch tells you the complete list of what is queryable before you write a single query.

Each listed type links to its **own per-type help page**. **Gotcha:** every per-type page is the **same templated boilerplate** - identical operations, identical example layout (list / get-by-id / filter / CRUD). The **only** thing that differs page to page is **that type's field list and each field's data type**. So:

1. **Fetch `sfhelp` once** to get the catalog of exposed entity sets (native + dynamic).
2. **Fetch a specific type's help page only to read that type's field schema** (field names + types) - which is what you need to write a correct `$select` / `$filter` / `$orderby`.
3. **Do NOT crawl every type's page.** The surrounding content is identical; crawling them all just re-reads the same boilerplate. For agent/LLM tooling: one `sfhelp` catalog fetch, then targeted per-type fetches for field schemas as needed.

The raw OData **service document** (the service root) and **`/api/default/$metadata`** (the EDM/CSDL) are the machine-readable equivalents - the service root lists entity sets as JSON, `$metadata` describes every entity type and property as XML. `sfhelp` is the readable layer on top.

## URL shape for entity sets

Type URL names are the **pluralized, lower-cased** type names:

| Content | Entity set URL |
|---|---|
| News | `/api/default/newsitems` |
| Media (images) | `/api/default/images` |
| A Module Builder dynamic type "Offices" | `/api/default/offices` |

**Gotcha:** the dynamic-module URL segment is the type's own pluralized name (e.g. `offices`), **not** prefixed by the module name. Confirm the exact segment from `sfhelp` rather than guessing pluralization - `sfhelp` is authoritative for the site's actual naming.

Single item by id (GUID, unquoted, in parentheses):

```http
GET /api/default/newsitems(3b177186-8b09-497c-8def-58613183d670)
```

## Query options (OData v4)

Sitefinity documents support for `$select`, `$filter`, `$orderby`, `$top`, `$skip`, `$count`, and `$expand` - the subset covered below. Other OData v4 system query options (`$search`, `$apply`, `$compute`, ...) are NOT documented for this service; test on the target site before relying on one. Worked examples:

**List with `$select` (project only the fields you need):**
```http
GET /api/default/newsitems?$select=Title,Summary,PublicationDate
```

**`$filter` - comparison operators** `eq ne gt ge lt le`, logical `and` / `or`, string functions `contains` / `startswith` / `endswith`, and `not`:
```http
GET /api/default/newsitems?$filter=contains(Title,'news')
GET /api/default/newsitems?$filter=not contains(Title,'news')
GET /api/default/newsitems?$filter=NumberField eq 5
GET /api/default/newsitems?$filter=NumberField ne 5
```

**`$filter` - date/datetime comparisons** (ISO 8601, `Z`-terminated; use a range with `and`):
```http
GET /api/default/newsitems?$filter=PublicationDate gt 2021-04-13T08:21:21Z and PublicationDate lt 2021-04-14T08:21:21Z
GET /api/default/newsitems?$filter=contains(Title,'news') and LastModified gt 2021-04-08T08:21:21Z
```

**`$orderby`** (default `asc`; multiple keys comma-separated):
```http
GET /api/default/newsitems?$orderby=Title asc
GET /api/default/newsitems?$orderby=Title asc, PublicationDate desc
```

**Paging with `$skip` / `$top`, and `$count`:**
```http
GET /api/default/newsitems?$orderby=Title asc&$skip=0&$top=10
GET /api/default/newsitems?$count=true          # inline total alongside the page
GET /api/default/newsitems/$count               # bare integer count, no items
```

**`$expand` of related items** (related content, related media):
```http
GET /api/default/newsitems(3b177186-8b09-497c-8def-58613183d670)?$expand=RelatedEvents
```
For related media whose field uses the default-site source in a multisite setup, add `sf_site`:
```http
GET /api/default/newsitems(3b177186-8b09-497c-8def-58613183d670)?sf_site={siteId}&$expand=RelatedImages
```
**Gotcha (not doc-verified here):** nested `$expand` depth and `$select` inside an `$expand` clause were not confirmed in the docs consulted - treat multi-level expansion as version-dependent and test it against the target site rather than assuming arbitrary depth.

**Querying a Module Builder dynamic type** - identical grammar, just the dynamic entity set:
```http
GET /api/default/offices?$select=Name,City&$filter=contains(City,'port')&$orderby=Name asc&$top=20
```

**Response format:** JSON. Collections are wrapped in a `value` array and carry an OData context header:
```json
{
  "@odata.context": "http://localhost:4242/api/default/$metadata#newsitems",
  "value": [ { "Id": "3b177186-...", "Title": "...", "Summary": "..." } ]
}
```
**Gotcha (not doc-verified here):** OData `odata.metadata` levels (`none` / `minimal` / `full`) via the `Accept` header follow the OData v4 spec, but exact support and defaults were not confirmed in the docs consulted - do not hard-code an assumption; inspect the actual `@odata.context` the site returns. Likewise a hard **maximum page size** was not documented - always drive paging explicitly with `$top`/`$skip` rather than relying on an implicit cap.

## Configuring exposed types (why a query 404s or 401s)

A type is queryable **only if it is added to the service**. Under **Administration > Web services > (the service) > This service provides access to...**, click *Change* to pick content types. Notes:

- **AutoGeneration:** leaving all types selected auto-populates the service from Sitefinity's recommended out-of-the-box types and their properties; disable via *Automatically select newly created content types*.
- **Field-level control:** in a type's *Advanced* section you choose which fields are exposed and whether each allows *filtering*, *sorting*, or both. A field not enabled for filtering cannot appear in `$filter`.
- **A type cannot be added twice** to the same service.

**Gotcha:** a type that is **not added to the service does not exist** as far as the API is concerned - the entity set URL simply is not routed. Combined with access levels below, an anonymous caller sees either a 404 (type not on the service) or a 401 (type on the service but not anonymously readable). Check `sfhelp` first: if the type is not in the catalog, it is not exposed.

## Authentication and access levels

Access is governed per service (and can be overridden per type) via the **Access** property, set under the service's *Define the access permissions*:

| Access level | Anonymous caller | Authenticated caller |
|---|---|---|
| **Anonymous** | can **read** content | can read; can modify per their Sitefinity roles |
| **Authenticated** (default) | **401 Unauthorized** | can read/modify per roles |
| **Admin** | **401 Unauthorized** | non-admin -> **403 Forbidden**; admin -> allowed |

- **Per-type override:** the *ACCESS* dropdown in a type's *Advanced* section lets one type differ from the service-wide default (e.g. news anonymous-readable while everything else stays Authenticated).
- **CORS:** the `AccessControlAllowOrigin` property restricts calling domains - list trusted origins, `*` for all, or leave empty to fall back to `SecurityConfig.config`.
- **The `X-SF-Service-Request: true` header:** include it on requests to protected services to avoid a spurious 401 - Sitefinity uses it to distinguish an intended service call from a normal browser navigation.
- **Authenticated calls from a Sitefinity page** carry the session automatically, so a logged-in user's identity resolves without extra token wiring (same mechanism the ServiceStack skill describes).

- **Access keys for headless/server-to-server callers:** Sitefinity supports per-user access keys - create a service user with the permissions you want the caller to have, then **Administration => Users => Actions => Generate access key**, and pass it on requests via the **`X-SF-Access-Key`** header. The request then executes as that user against the service's Access levels/roles. Prefer a dedicated least-privilege service user over reusing a real admin's key.

## Verify it works

1. **Catalog:** open `{domain}/api/default/sfhelp` in a browser - you should see every exposed type listed (news, media, your dynamic modules). If a type is missing, it is not configured on the service.
2. **An entity set:** `curl "{domain}/api/default/newsitems?$top=1"` (or paste in a browser). A `200` with a `value` array = wired correctly.
3. **Diagnose the status code:**
   - **404** = the entity set is not routed - the type is not added to the service, or the route/service URL segment is wrong. Re-check `sfhelp` and the *This service provides access to...* list.
   - **401** = the type is exposed but not anonymously readable (Access = Authenticated/Admin) - authenticate, or set the type/service to Anonymous, and add `X-SF-Service-Request: true`.
   - **403** = authenticated but lacking the required role (Access = Admin).
4. **A single item / field schema:** `{domain}/api/default/newsitems({id})?$select=Title` confirms both id addressing and that the field is exposed. Read the type's `sfhelp` page for the exact field names/types before building larger `$select`/`$filter` clauses.

When the docs and the running site disagree (an option that "should" work returns 400, a field that "should" filter is rejected), trust the running site - the exposed field set and per-type access are configuration, and configuration on the target site wins over any generic documentation.
