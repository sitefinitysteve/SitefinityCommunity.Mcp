---
name: sitefinity-toolbox-icons
description: Reference list of built-in Sitefinity toolbox icon CSS classes for the CssClass property on ControllerToolboxItem, plus the page-editor CSS that badges MVC widgets during WebForms-to-MVC migrations. Use when adding a new MVC widget and picking its toolbox icon.
---

# Sitefinity Toolbox Icons for MVC Widgets

Reference list of icon CSS classes available for the `CssClass` property on
`[ControllerToolboxItem]`. These classes come from Sitefinity's built-in
toolbox sprite sheet — no new assets required.

## Usage

Set `CssClass` to **two** classes: the specific icon class first, then
`sfMvcIcn` (which paints the blue "MVC" badge on the tile).

```csharp
[ControllerToolboxItem(
    Name = "MyStats_MVC",
    Title = "Stats",
    SectionName = "ContentToolboxSection",
    CssClass = "sfStatisticsIcn sfMvcIcn")]
public class MyStatsController : Controller
{
    // ...
}
```

Order doesn't matter functionally, but putting the icon first matches
Sitefinity's own convention (e.g. `sfNavigationIcn sfMvcIcn` on the
Navigation widget).

Without a specific icon class, the tile renders blank (just the MVC
badge on an empty square).

## Tracking your own assignments

Keep a table of which icon class each of your widgets uses in your own
project documentation. Since the sprite sheet is shared, tracking your
assignments makes it easy to pick an unused icon that best matches a new
widget's purpose and avoids two widgets colliding on the same tile.

## Available icon classes

Content / layout
- `sfContentBlockIcn` - generic content block
- `sfContentViewIcn` - content view / article
- `sfListitemsIcn` - list of items
- `sfRecentItemsIcn` - recent items feed
- `sfBreadcrumbIcn` - breadcrumb navigation

Images / media
- `sfImageViewIcn` - single image / hero
- `sfImageLibraryViewIcn` - image gallery
- `sfVideoIcn` - single video
- `sfVideoListIcn` - video list
- `sfDownloadLinkIcn` - single download link
- `sfDownloadListIcn` - list of downloads
- `sfLinkedFileViewIcn` - linked file

Blogs / news / events
- `sfBlogsViewIcn` - single blog
- `sfBlogsListViewIcn` - blog list
- `sfNewsViewIcn` - news
- `sfEventsViewIcn` - events
- `sfArchiveIcn` - archive

Navigation
- `sfSitemapIcn` - sitemap
- `sfMenuIcn` - menu / tabs
- `sfTreeviewIcn` - tree view
- `sfNavigationIcn` - navigation

Taxonomy
- `sfFlatTaxonIcn` - flat taxonomy
- `sfHierarchicalTaxonIcn` - hierarchical taxonomy

Forms / feeds
- `sfFormsIcn` - form
- `sfFeedsIcn` - RSS / feed
- `sfLanguageSelectorIcn` - language selector

Users / auth
- `sfLoginIcn` - login form
- `sfLoginStatusIcn` - login status
- `sfLoginNameIcn` - login name display
- `sfCreateAccountIcn` - create account
- `sfForgottonPasswordIcn` - forgot password
- `sfChangePasswordIcn` - change password
- `sfAccountActivationIcn` - account activation
- `sfProfilecn` - user profile (note: missing `I` in Sitefinity class)
- `sfUserListIcn` - list of users / team

Search
- `sfSearchBoxIcn` - search box
- `sfSearchResultIcn` - search results

Social
- `sfFacebookFeedIcn` - facebook feed
- `sfFacebookLikeIcn` - facebook like button
- `sfPageSharingIcn` - social sharing
- `sfTwitterFeedIcn` - twitter feed
- `sfCommentsIcn` - comments / testimonials
- `sfForumsViewIcn` - forum / Q&A

Ecommerce
- `sfProductsListIcn` - product list
- `sfProductFilterIcn` - product filter
- `sfCartSummaryIcn` - cart summary
- `sfShoppingCartIcn` - shopping cart
- `sfCheckoutIcn` - checkout
- `sfBuyNowIcn` - buy now / CTA button
- `sfOrdersListIcn` - orders list
- `sfOrderInvoiceIcn` - order invoice
- `sfDigitalProductsListIcn` - digital products
- `sfCurrencySelectorIcn` - currency selector
- `sfWishListIcn` - wishlist

Misc
- `sfSiteSelectorIcn` - site selector
- `sfStatisticsIcn` - statistics / stats
- `sfLicenseExpIcn` - license expiration

## Badging MVC widgets in the page editor (WebForms -> MVC migrations)

`sfMvcIcn` only badges the TOOLBOX tile. To also badge each widget already
ON the page, add this CSS to any stylesheet that loads inside the page
editor (a backend theme stylesheet, or a globally loaded frontend CSS -
everything is scoped under `.sfPageEditor`, so it is a silent no-op on
public pages):

```css
.sfPageEditor #sfPageContainer div[controltype^="Telerik.Sitefinity.Mvc"] .rdCenter .rdTitleBar>em:after,
.sfPageEditor #sfPageContainer div[controltype^="Telerik.Sitefinity.Frontend.Mvc"] .rdCenter .rdTitleBar>em:after {
    content: "MVC";
    background-color: #105cb6;
    color: #fff;
    font-size: 10px;
    padding: 0 4px;
    margin-left: 5px;
}
```

What it does: every widget's RadDock title bar in the zone editor gets a
small blue "MVC" tag when the widget is MVC-based. The `controltype`
attribute on the editor's widget wrapper carries the control's .NET type,
so the two prefix selectors catch both flavors:

- `Telerik.Sitefinity.Mvc` -> classic MVC widgets (`MvcControllerProxy` -
  i.e. all your custom controllers)
- `Telerik.Sitefinity.Frontend.Mvc` -> Feather dynamic widgets
  (`MvcWidgetProxy`)

Why bother: during a WebForms -> MVC migration you can open any page in
the editor and see at a glance which widgets are migrated (badged) and
which are legacy WebForms (unbadged) - no clicking into each widget. It
also just looks nice.

## Notes

- The blank-icon default (`sfMvcIcn` alone) is what you get with no icon
  class - the tile looks empty, which makes the toolbox hard to scan.
- There is no visual picker - the quickest way to preview an icon is to
  temporarily assign it to a widget, rebuild, and look at the toolbox.
- These classes are defined in Sitefinity's core admin CSS; they ship
  with every Sitefinity install so no additional assets are needed.
