// Support types for the row classes emitted by flatten-dynamic-content.sql (@Mode = 'poco').
// One copy per project. Parses the JSON columns with ServiceStack.Text, which Sitefinity ships and
// which is the fastest serializer already in the bin folder. The generator emits PascalCase keys
// (Id, Title, UrlName ...) that match these properties by name, so no serializer attributes are
// needed and no global JsConfig setting is touched (never change JsConfig inside Sitefinity - the
// backend shares the instance). Outside a Sitefinity project (a .NET 8 reporting service) 
// the four DeserializeFromString calls with System.Text.Json.JsonSerializer.Deserialize<T>(json).
//
// Column mapping:
//   Dapper   - DefaultTypeMap.MatchNamesWithUnderscores = true;   // once, at startup
//              conn.Query<AnnouncementFlatRow>("EXEC dbo.usp_SitefinityFlattenDynamicType @Module, @Type, 'live'", new { Module = "Announcements", Type = "Announcement" })
//   EF Core  - modelBuilder.Entity<AnnouncementFlatRow>().HasNoKey().ToView("vw_Announcements_Announcement");
//              the [Column] attributes below map the sf_* columns; field columns match by name.
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using ServiceStack.Text;

namespace Sitefinity.Flat
{
    /// <summary>The sf_* lifecycle columns every flattened row starts with. All dates are UTC.</summary>
    public abstract class SitefinityFlatRowBase
    {
        [Column("sf_master_id")]
        public Guid SfMasterId { get; set; }

        [Column("sf_row_id")]
        public Guid SfRowId { get; set; }

        /// <summary>"live" | "master"</summary>
        [Column("sf_lifecycle")]
        public string SfLifecycle { get; set; }

        /// <summary>Published | Scheduled | Expired | Unpublished | Draft | Deleted</summary>
        [Column("sf_publication_state")]
        public string SfPublicationState { get; set; }

        [Column("sf_is_published")]
        public int SfIsPublished { get; set; }

        [Column("sf_scheduled_publish_utc")]
        public DateTime? SfScheduledPublishUtc { get; set; }

        [Column("sf_url_name")]
        public string SfUrlName { get; set; }

        [Column("sf_default_url")]
        public string SfDefaultUrl { get; set; }

        [Column("sf_publication_date_utc")]
        public DateTime? SfPublicationDateUtc { get; set; }

        [Column("sf_expiration_date_utc")]
        public DateTime? SfExpirationDateUtc { get; set; }

        [Column("sf_date_created_utc")]
        public DateTime? SfDateCreatedUtc { get; set; }

        [Column("sf_last_modified_utc")]
        public DateTime? SfLastModifiedUtc { get; set; }

        [Column("sf_owner_id")]
        public Guid? SfOwnerId { get; set; }

        [Column("sf_owner_user_name")]
        public string SfOwnerUserName { get; set; }

        [Column("sf_last_modified_by_id")]
        public Guid? SfLastModifiedById { get; set; }

        [Column("sf_last_modified_by_user_name")]
        public string SfLastModifiedByUserName { get; set; }

        [Column("sf_provider")]
        public string SfProvider { get; set; }

        [Column("sf_version")]
        public int? SfVersion { get; set; }

        [Column("sf_parent_master_id")]
        public Guid? SfParentMasterId { get; set; }

        [Column("sf_parent_type")]
        public string SfParentType { get; set; }

        public bool IsPublished => this.SfIsPublished == 1;
    }

    /// <summary>One element of a taxonomy JSON column.</summary>
    public sealed class TaxonRef
    {
        public Guid Id { get; set; }

        public string Title { get; set; }

        public string UrlName { get; set; }

        /// <summary>sf_taxonomies.nme, e.g. "Categories", "Tags"</summary>
        public string Taxonomy { get; set; }
    }

    /// <summary>One element of a related-data / related-media JSON column. Ids are master ids.</summary>
    public sealed class RelatedRef
    {
        public Guid Id { get; set; }

        /// <summary>CLR type, e.g. Telerik.Sitefinity.Libraries.Model.Document</summary>
        public string Type { get; set; }

        public string Provider { get; set; }

        /// <summary>sort key only; may be negative</summary>
        public decimal Ordinal { get; set; }
    }

    /// <summary>The sf_addresses row behind an Address field.</summary>
    public sealed class AddressRef
    {
        public string Street { get; set; }

        public string City { get; set; }

        public string StateCode { get; set; }

        public string Zip { get; set; }

        public string CountryCode { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public int? MapZoomLevel { get; set; }
    }

    /// <summary>Null-safe parsers for the JSON columns. A NULL column (no values) yields an empty list / null.</summary>
    public static class FlatJson
    {
        private static readonly IReadOnlyList<TaxonRef> NoTaxa = new TaxonRef[0];
        private static readonly IReadOnlyList<RelatedRef> NoRelated = new RelatedRef[0];
        private static readonly IReadOnlyList<string> NoStrings = new string[0];

        public static IReadOnlyList<TaxonRef> Taxa(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? NoTaxa : JsonSerializer.DeserializeFromString<List<TaxonRef>>(json);
        }

        public static IReadOnlyList<RelatedRef> Related(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? NoRelated : JsonSerializer.DeserializeFromString<List<RelatedRef>>(json);
        }

        public static IReadOnlyList<string> Strings(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? NoStrings : JsonSerializer.DeserializeFromString<List<string>>(json);
        }

        public static AddressRef Address(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.DeserializeFromString<AddressRef>(json);
        }
    }
}
