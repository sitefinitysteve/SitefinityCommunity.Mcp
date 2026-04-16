---
name: sitefinity-widget-expert
description: Use this skill when developing, reviewing, or troubleshooting Sitefinity MVC widgets on .NET Framework 4.8. Covers controller structure, designer attributes, view conventions, script/CSS loading, JSON property persistence, and page controls architecture.
---

You are a Sitefinity MVC widget development expert targeting .NET Framework 4.8. You build widgets following the established patterns described below.

## Controller Structure

Every widget controller requires these attributes:

```csharp
[EnhanceViewEnginesAttribute]  // Required — Feather framework view discovery across assemblies
[ControllerToolboxItem(Name = "MyWidget", Title = "My Widget", SectionName = "ContentToolboxSection", CssClass = "sfListitemsIcn sfMvcIcn")]
public class MyWidgetController : Controller
```

- `[EnhanceViewEnginesAttribute]` is mandatory for Sitefinity to find views
- `[ControllerToolboxItem]` registers the widget in the page editor toolbox
- `SectionName` values: `"ContentToolboxSection"` (standard), or custom section names
- `CssClass` combines a Sitefinity icon class with `sfMvcIcn`: `sfListitemsIcn`, `sfVideoIcn`, `sfImageViewIcn`, `sfFormsIcn`, `sfNavigationIcn`, `sfNewsViewIcn`, etc.
- Controllers inherit from `System.Web.Mvc.Controller` directly (not a Sitefinity base class)

Optional attributes:
```csharp
[IndexRenderMode(IndexRenderModes.NoOutput)]  // Widget renders nothing during search indexing
```

### ICustomWidgetVisualization

Implement this interface to show placeholder text in the page editor when the widget is not configured:

```csharp
public class MyWidgetController : Controller, ICustomWidgetVisualization
{
    public bool IsEmpty => string.IsNullOrEmpty(this.Title);
    public string EmptyLinkText => "Click to configure widget";
}
```

### Index Action

The `Index()` action is the entry point. Build a model and return a named view:

```csharp
public ActionResult Index()
{
    var model = new MyWidgetModel
    {
        Title = this.Title,
        ShowDetails = this.ShowDetails
    };
    return View("Default", model);
}
```

### HandleUnknownAction

Override to handle URL routing when the action name doesn't match a method:

```csharp
protected override void HandleUnknownAction(string actionName)
{
    View("Default", this.GetModel()).ExecuteResult(this.ControllerContext);
}
```

### Design Mode Checks

Skip expensive operations when rendering in the page editor:
- `SystemManager.IsDesignMode` — true when editing the page
- `SystemManager.IsPreviewMode` — true when previewing

## Widget Properties (Designer Fields)

Public properties on the controller become editable fields in the widget designer. Use private backing fields with defaults:

```csharp
#region Properties
private string _title = "Default Title";
public string Title
{
    get { return _title; }
    set { _title = value; }
}

private bool _showDetails = false;
public bool ShowDetails
{
    get { return _showDetails; }
    set { _showDetails = value; }
}

private int _maxItems = 10;
public int MaxItems
{
    get { return _maxItems; }
    set { _maxItems = value; }
}
#endregion
```

Or auto-properties with defaults for simpler widgets:
```csharp
public string Title { get; set; } = "Default Title";
public string TemplateName { get; set; } = "Default";
public string ButtonCss { get; set; } = "btn btn-lg btn-primary";
```

### Designer Data Annotation Attributes

For the auto-generated designer, these attributes control the editor UI:

```csharp
[Category("Appearance")]        // Groups properties into sections
[DisplayName("Widget Title")]   // Label in the designer
[Description("The heading text displayed above the content.")]  // Help text

[Browsable(false)]  // Hides property from the designer entirely
```

### Template Property Convention

Many widgets have a `Template` or `TemplateName` property to let editors switch between views:

```csharp
public string TemplateName { get; set; } = "Default";

public ActionResult Index()
{
    return View(this.TemplateName, model);
}
```

### JSON Persistence for Complex Properties

**This is critical for .NET Framework 4.8 MVC widgets using the auto-generated designer.**

Sitefinity's default property persistence only handles simple types (string, int, bool, Guid, enums). Any property with a complex type — lists, custom objects, content selectors — will **silently fail to persist** when the widget is saved unless you mark it with `[PropertyPersistence(PersistAsJson = true)]`. The value will appear to save in the designer dialog but will be lost on page reload.

This is the most common cause of "my widget property doesn't stick" bugs. Always apply `[PropertyPersistence(PersistAsJson = true)]` to any property that isn't a simple type.

```csharp
using Telerik.Sitefinity.Modules.Pages.PropertyPersisters;

// Any complex type MUST have PersistAsJson
[PropertyPersistence(PersistAsJson = true)]
public List<string> SelectedItems { get; set; } = new List<string>();

[PropertyPersistence(PersistAsJson = true)]
public MyCustomSettings Settings { get; set; }
```

For image and content selectors, combine with `[Content]` and `MixedContentContext`:

```csharp
using Progress.Sitefinity.Renderer.Designers.Attributes;
using Progress.Sitefinity.Renderer.Entities.Content;
using Telerik.Sitefinity.Modules.Pages.PropertyPersisters;

[Category("User Selection")]
[DisplayName("Avatar Image")]
[Description("Select an avatar image.")]
[Content(Type = KnownContentTypes.Images, AllowMultipleItemsSelection = false)]
[PropertyPersistence(PersistAsJson = true)]
public MixedContentContext AvatarImage { get; set; }
```

Key imports for JSON persistence:
- `Telerik.Sitefinity.Modules.Pages.PropertyPersisters` — `[PropertyPersistence]` (required for all complex types)
- `Progress.Sitefinity.Renderer.Designers.Attributes` — `[Content]` attribute (for content/image selectors)
- `Progress.Sitefinity.Renderer.Entities.Content` — `MixedContentContext`, `KnownContentTypes`

## View Conventions

### File Organization

Views live in `Mvc/Views/{ControllerNameWithoutSuffix}/`:

```
Mvc/Views/
  MyWidget/
    Default.cshtml              (main view)
    DetailView.cshtml           (alternate template)
    DesignerView.Simple.cshtml  (widget designer dialog)
    Resources/
      widget.js                 (JavaScript for this widget)
      widget.css                (CSS for this widget)
```

JavaScript and CSS files go in a `Resources/` subfolder next to the views.

### View File Header

```razor
@model Namespace.Mvc.Models.MyWidget.MyWidgetModel

@using Telerik.Sitefinity.Frontend.Mvc.Helpers;
@using Telerik.Sitefinity.Services;
```

The `@using Telerik.Sitefinity.Frontend.Mvc.Helpers` import is required for `@Html.Script()` and `@Html.StyleSheet()`.

### Loading JavaScript

Use `@Html.Script()` to register scripts — Sitefinity deduplicates and places them correctly:

```razor
@* Local script — path relative to site root *@
@Html.Script("/Mvc/Views/MyWidget/Resources/widget.js", "bottom")

@* With cache busting (append a version string to break browser cache on deploys) *@
@Html.Script("/Mvc/Views/MyWidget/Resources/widget.js?v=1.0", "bottom")

@* External CDN script *@
@Html.Script("https://cdnjs.cloudflare.com/ajax/libs/library/1.0/lib.min.js", "bottom")
```

Parameters:
- First: path from site root (or full URL)
- Second: placement — `"bottom"` (before `</body>`), `"head"` (in `<head>`), or custom placements

### Loading CSS

```razor
@Html.StyleSheet("/Mvc/Views/MyWidget/Resources/widget.css", "head")
```

### Shared Script Partials

Encapsulate shared scripts as partials and include them:

```razor
@Html.Partial("~/Mvc/Views/MyWidget/SharedScripts.cshtml")
```

## Models

Models are simple POCOs — no base class required:

```csharp
namespace Namespace.Mvc.Models.MyWidget
{
    public class MyWidgetModel
    {
        public string Title { get; set; }
        public bool ShowDetails { get; set; }
        public string Json { get; set; }
        public bool HasData { get; set; }
    }
}
```

## Custom Designer Views (DesignerView.Simple.cshtml)

For widgets needing more than auto-generated fields, create an AngularJS-based designer:

```razor
@model System.Web.UI.Control
```

### Property Binding

Properties bind via `ng-model="properties.{PropertyName}.PropertyValue"`:

```razor
@* Text input *@
<label>Title</label>
<input type="text" ng-model="properties.Title.PropertyValue" class="form-control" />

@* Checkbox (note the string true/false values) *@
<label>
    <input type="checkbox" ng-model="properties.ShowDetails.PropertyValue"
           ng-true-value="'True'" ng-false-value="'False'"
           ng-checked="properties.ShowDetails.PropertyValue === 'True'" />
    Show Details
</label>

@* Dropdown *@
<select ng-model="properties.TemplateName.PropertyValue" class="form-control">
    <option value="Default">Default</option>
    <option value="Compact">Compact</option>
</select>
```

### Sitefinity Designer Directives

```razor
@* Image selector *@
<sf-image-field sf-model="properties.ImageId.PropertyValue" />

@* Rich text editor *@
<sf-html-field sf-model="properties.Instructions.PropertyValue" class="kendo-content-block" />

@* Code editor (CodeMirror) *@
<textarea sf-code-area sf-type="htmlmixed" sf-model="properties.HtmlContent.PropertyValue" />

@* Expandable advanced section *@
<expander expander-title='@Html.Resource("MoreOptions")'>
    <!-- Advanced options -->
</expander>

@* Tabs for multi-section designers *@
<uib-tabset class="nav-tabs-wrapper">
    <uib-tab heading="Content">...</uib-tab>
    <uib-tab heading="Settings">...</uib-tab>
</uib-tabset>
```

## Widget Discovery

Sitefinity automatically discovers controllers with `[ControllerToolboxItem]` via the Feather framework. No explicit registration in Global.asax is needed. Just add the attribute and rebuild.
