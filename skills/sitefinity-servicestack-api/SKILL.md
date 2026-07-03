---
name: sitefinity-servicestack-api
description: Use this skill when building JSON/REST APIs inside a Sitefinity MVC site, writing or registering ServiceStack services, wiring /RestApi routes, securing API endpoints with Sitefinity roles and identity, or diagnosing DateTime serialization problems where JavaScript shows "Invalid Date" from a Sitefinity endpoint.
---

You are a Sitefinity ServiceStack API expert targeting classic Sitefinity on .NET Framework 4.8 (MVC / Feather-era sites). Sitefinity hosts ServiceStack internally and you contribute services as plugins - you never stand up your own AppHost. The facts below were verified against Sitefinity 15.4 platform behavior. See the companion skills `sitefinity-widget-expert` (widget controllers that call these APIs from JavaScript) and `sitefinity-page-inspector` (page/widget inspection) for the surrounding architecture.

**Before you write a service:** if the goal is only to **read or query existing content** (list/filter/sort/page/expand News, Events, media, or Module Builder types), the built-in default OData web service at `/api/default` may already do it with **zero code** - see the companion skill `sitefinity-odata-services`. Write a ServiceStack service when you need custom logic, writes with side effects, cross-source aggregation, or a response shape the built-in service cannot express.

**Version baseline: Sitefinity 15.4** - the ServiceStack version bundled with the platform and the exact `IPlugin`/`IService` surface can shift between releases, so verify on older versions. Check the target project's version: `(Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion` (e.g. `15.4.8630.0` = Sitefinity 15.4).

## The hosting model - Sitefinity owns the AppHost

Sitefinity ships and hosts a ServiceStack `AppHost` internally. You do **NOT** create your own `AppHostBase`, and you do NOT call `Init()` - the platform already did. You contribute functionality as ServiceStack **plugins** registered through the platform:

```csharp
using Funq;
using ServiceStack;

public class EventsServicePlugin : IPlugin
{
    public void Register(IAppHost appHost)
    {
        appHost.RegisterService(typeof(EventsService));
        // optional: appHost.RegisterService(typeof(AnotherService));
        // optional container wiring: appHost.GetContainer().Register<IEventsRepo>(c => new EventsRepo());
    }
}
```

Register each plugin exactly once at application startup:

```csharp
using Telerik.Sitefinity.Services;

// In Global.asax Application_Start, or a bootstrapper wired to Bootstrapper.Initialized
protected void Application_Start(object sender, EventArgs e)
{
    SystemManager.RegisterServiceStackPlugin(new EventsServicePlugin());
}
```

**Gotcha:** `RegisterServiceStackPlugin` must run before ServiceStack finishes initializing. Registering from `Application_Start` (or a `Bootstrapper.Initialized` handler that runs during startup) is reliable; calling it lazily from inside a request is too late - the plugin never binds and every route 404s.

### The service class

Service classes implement `ServiceStack.IService` (or extend `ServiceStack.Service` for the convenience base). Methods are named after the HTTP verb (`Get`, `Post`, `Put`, `Delete`) and keyed off the **request DTO** type - ServiceStack dispatches by DTO, not by method name or route string directly:

```csharp
using ServiceStack;

public class EventsService : Service
{
    public object Get(GetUpcomingEvents request)
    {
        var count = request.Take > 0 ? request.Take : 10;
        return new UpcomingEventsResponse
        {
            Events = EventsRepo.GetUpcoming(count)
        };
    }

    public object Post(CreateEvent request)
    {
        var id = EventsRepo.Create(request.Title, request.StartsOn);
        return new CreateEventResponse { Id = id };
    }
}
```

## Request DTOs and routes

Routes are declared with `[Route]` attributes on the **request DTO**, not on the service method. One DTO can carry multiple routes, and path segments bind to DTO properties by name:

```csharp
using ServiceStack;

[Route("/events/upcoming", "GET")]
[Route("/events/upcoming/{Take}", "GET")]
public class GetUpcomingEvents : IReturn<UpcomingEventsResponse>
{
    public int Take { get; set; }          // binds from {Take} path segment or ?Take= query
}

public class UpcomingEventsResponse
{
    public List<EventDto> Events { get; set; } = new List<EventDto>();
}

[Route("/events", "POST")]
public class CreateEvent : IReturn<CreateEventResponse>
{
    public string Title { get; set; }
    public DateTime StartsOn { get; set; }
}
```

- The verb argument (`"GET"`, `"POST"`, or `"GET,POST"`) restricts which verbs match; omit it to accept any.
- `IReturn<TResponse>` is optional but gives typed clients and cleaner metadata.
- Wildcard/path params: `[Route("/events/{Category}/{Id}")]` binds `{Category}` and `{Id}` to same-named DTO properties. A trailing `{Path*}` captures the remainder.

## The URL prefix - the classic gotcha

Sitefinity binds ServiceStack to a URL path in **web.config**, not in code. A `<location>` block maps `ServiceStack.HttpHandlerFactory` for one path:

```xml
<location path="RestApi">
  <system.webServer>
    <handlers>
      <add name="ServiceStack" path="*" verb="*"
           type="ServiceStack.HttpHandlerFactory, ServiceStack"
           preCondition="integratedMode" />
    </handlers>
  </system.webServer>
</location>
```

**Gotcha:** Your C# `[Route("/events/upcoming")]` attribute does **NOT** include the prefix, but every client must call it **with** the prefix: `/RestApi/events/upcoming`. The route string is relative to the handler's mount path. This bites everyone once - the C# says `/events/upcoming`, the browser/AJAX must hit `/RestApi/events/upcoming`.

The prefix is whatever the `<location path="...">` block says. `RestApi` is the common convention, but **always open the target site's web.config and confirm the actual path** rather than assuming.

Related web.config keys worth knowing:
- `<add key="sf:serviceStackEnableFeatures" value="Json,Html,Metadata" />` - which ServiceStack features are on. `Metadata` powers the `/RestApi/metadata` discovery page; `Html` enables the HTML auto-view; drop features you do not want exposed. See the hardening note below.
- `<add key="sf:HealthCheckApiEndpoint" value="..." />` - configures the platform health-check endpoint.

### Exposing/hiding the metadata route (security hardening)

The `sf:serviceStackEnableFeatures` appSetting is a comma-separated list of enabled ServiceStack features, and it directly controls whether the metadata pages are reachable:

```xml
<!-- Dev: metadata pages exposed at /RestApi/metadata -->
<add key="sf:serviceStackEnableFeatures" value="Json,Html,Metadata" />

<!-- Production hardening: JSON only - /RestApi/metadata now 404s -->
<add key="sf:serviceStackEnableFeatures" value="Json" />
```

**Gotcha:** `/RestApi/metadata` enumerates **every registered service and the full shape of every request/response DTO** - a complete map of your API surface handed to anyone who requests the page. Because of that, production sites commonly disable `Metadata` (and often `Html`, the human-readable auto-view) while keeping `Json` so the actual endpoints still function. Dev environments keep `Metadata` on for the verify-registration workflow below. Removing a feature from the list disables it entirely; the page returns 404, not an auth error. (Confirm the exact key casing in the target site - Sitefinity documents it as `sf:serviceStackEnableFeatures`, camel-cased after the `sf:` prefix.)

## Authentication and authorization - use Sitefinity identity

Do **NOT** use ServiceStack's own auth providers, `[Authenticate]`, or its credential store. Integrate with the Sitefinity identity that is already established for the request. Because a browser call from a Sitefinity page carries the Sitefinity session cookie, the current identity resolves inside the service:

```csharp
using Telerik.Sitefinity.Security.Claims;

var identity = ClaimsManager.GetCurrentIdentity();
// identity.IsAuthenticated  - false for anonymous visitors
// identity.IsBackendUser    - true for CMS/backend users
// identity.Roles            - IEnumerable<string> of role names
// identity.UserId           - Guid of the current user
```

**Gotcha:** an unauthenticated request still returns a **non-null** identity object whose `Name` is `"Anonymous"`. Never gate on `identity == null` alone - always check `identity.IsAuthenticated`. Null-checking will happily wave anonymous users through.

### Reusable request-filter attribute (the clean pattern)

A ServiceStack `RequestFilterAttribute` runs before the service method and can short-circuit the response. This is the tidiest way to secure endpoints - decorate the request DTO (or the service class):

```csharp
using ServiceStack;
using ServiceStack.Web;
using Telerik.Sitefinity.Security.Claims;

public class RequiresBackendUserAttribute : RequestFilterAttribute
{
    public override void Execute(IRequest req, IResponse res, object requestDto)
    {
        var identity = ClaimsManager.GetCurrentIdentity();
        if (identity == null || !identity.IsAuthenticated || !identity.IsBackendUser)
        {
            res.StatusCode = 401;
            res.EndRequest();     // stops the pipeline - the service method never runs
        }
    }
}
```

Apply it to a DTO:

```csharp
[Route("/events", "POST")]
[RequiresBackendUser]
public class CreateEvent : IReturn<CreateEventResponse> { /* ... */ }
```

### Variant: authenticated-only

```csharp
public class RequiresAuthenticatedAttribute : RequestFilterAttribute
{
    public override void Execute(IRequest req, IResponse res, object requestDto)
    {
        var identity = ClaimsManager.GetCurrentIdentity();
        if (identity == null || !identity.IsAuthenticated)
        {
            res.StatusCode = 401;
            res.EndRequest();
        }
    }
}
```

### Variant: role-based

```csharp
public class RequiresAnyRoleAttribute : RequestFilterAttribute
{
    private readonly string[] roles;

    public RequiresAnyRoleAttribute(params string[] roles)
    {
        this.roles = roles ?? new string[0];
    }

    public override void Execute(IRequest req, IResponse res, object requestDto)
    {
        var identity = ClaimsManager.GetCurrentIdentity();
        if (identity == null || !identity.IsAuthenticated)
        {
            res.StatusCode = 401;
            res.EndRequest();
            return;
        }

        // 403, not 401: the caller IS authenticated but lacks the role
        var userRoles = identity.Roles ?? Enumerable.Empty<string>();
        if (!this.roles.Any(r => userRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
        {
            res.StatusCode = 403;
            res.EndRequest();
        }
    }
}

// Usage - require the "Administrators" OR "Editors" role:
// [RequiresAnyRole("Administrators", "Editors")]
```

For "must have ALL roles" swap `.Any(...)` for `.All(...)`. Use `401` when the caller is not authenticated at all and `403` when they are authenticated but lack the required role - the distinction matters to clients deciding whether to prompt for login versus show an access-denied message.

## DateTime serialization - the "Invalid Date" trap

This is the single most common Sitefinity ServiceStack bug. `ServiceStack.Text` serializes `DateTime` in **WCF format** by default: `/Date(1744731000000-0400)/`.

**Gotcha:** JavaScript's `new Date("/Date(1744731000000-0400)/")` does **NOT** throw - it silently returns an invalid `Date` object whose `.toLocaleString()` / `.toLocaleTimeString()` returns the literal string `"Invalid Date"`. No exception is raised, so `try/catch` around `new Date(...)` will **not** save you. The symptom is the text "Invalid Date" appearing in the UI where a timestamp should be, and the root cause is almost always a DTO property typed `DateTime` returned through the API.

### The fix: per-DTO ISO strings (recommended)

Keep the change local to the DTOs you control. Two equivalent shapes:

```csharp
// Option 1: declare the date property as string and assign round-trip ("o") format
public class EventDto
{
    public string StartsOn { get; set; }      // "2026-07-03T14:30:00.0000000-04:00"
}
// ...
dto.StartsOn = evt.StartDate.ToString("o");   // "o" = ISO 8601 round-trip

// Option 2: keep the DateTime, add a string sibling (least disruptive to C# consumers)
public class EventDto
{
    public DateTime? Created { get; set; }
    public string CreatedIso => this.Created?.ToString("o");   // computed, serialized alongside
}
```

Option 2 is best when C# code already reads the `DateTime` property - existing consumers keep working, and the frontend switches to the `*Iso` sibling.

**Frontend:** ISO 8601 strings parse natively - `new Date(isoString)` just works, no WCF regex needed. The TypeScript type for the field should be `string`, not `Date`.

### Why NOT the global switch: `JsConfig.DateHandler = DateHandler.ISO8601`

ServiceStack has a process-global setting that looks like the obvious clean fix:

```csharp
using ServiceStack.Text;

JsConfig.DateHandler = DateHandler.ISO8601;      // DO NOT do this in a Sitefinity site
```

**Gotcha - do not do this in a Sitefinity site.** Sitefinity hosts ONE shared ServiceStack `AppHost` for the entire process, and `JsConfig.*` settings are global to it. Flipping the date handler changes serialization not just for your endpoints but for **Sitefinity's own internal ServiceStack services** - the CMS backend/admin UI itself runs on ServiceStack and expects the WCF `/Date()/` format in places. Changing it globally can break Sitefinity backend functionality in ways that surface far from your code. It also breaks any of your existing JavaScript consumers that regex-parse `/Date(...)/ `. Stick with the per-DTO fix above; it is fully local and cannot destabilize the platform.

The same caution applies to **any** global `JsConfig.*` mutation. For example, `JsConfig.IncludeNullValues = true` is often wanted because ServiceStack **omits** null members from the JSON by default, which surprises JS consumers expecting the key to exist (`obj.prop` yields `undefined`, destructuring defaults misfire). It is lower risk than the date handler, but it still changes the output of Sitefinity's internal services too - if you set it, exercise the CMS backend afterward to confirm nothing regressed. When the null-key shape matters only for your own endpoints, prefer initializing DTO properties (empty strings/lists) over the global flag.

## Verify it works

- **Hit the endpoint in a browser while logged into the backend:** `/RestApi/events/upcoming?format=json` forces JSON output (the `format` query param overrides content negotiation). You should see your response DTO serialized.
- **Confirm the plugin registered:** if the `Metadata` feature is enabled, `/RestApi/metadata` lists every registered service and DTO. If your route is absent from that page, the plugin did not register - revisit the `RegisterServiceStackPlugin` call and its timing. **Note:** `/RestApi/metadata` itself returns 404 when the `Metadata` feature is disabled in `sf:serviceStackEnableFeatures` (common in production) - a 404 *there* is not evidence of a registration failure, only that the discovery page is turned off. Test registration on a dev environment that keeps `Metadata` enabled, or fall back to hitting the actual `?format=json` route.
- **401 vs 404 diagnosis:**
  - **404** = routing/registration problem. The route was never matched: plugin not registered, wrong verb, the request DTO's `[Route]` does not match, or you forgot the `/RestApi` prefix. ServiceStack does not know this URL exists.
  - **401 / 403** = the route matched and dispatched, but your request-filter attribute fired. The API is wired correctly; this is an auth outcome, not a plumbing failure. 401 = not authenticated, 403 = authenticated but lacking the role.
- **Called from widget JavaScript:** a fetch/AJAX call from a Sitefinity page automatically carries the session cookie, so `ClaimsManager.GetCurrentIdentity()` sees the logged-in user with no extra token wiring. See `sitefinity-widget-expert` for the widget side of this call.
