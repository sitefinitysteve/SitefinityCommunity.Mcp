-- ============================================================================
-- flatten-dynamic-content.sql  (skill: sitefinity-dynamic-content-sql)
--
-- HOW TO RUN
--   A) Ad hoc, no DDL rights needed (this is how it was verified): set the
--      parameters in the DECLARE block below and execute the whole file.
--   B) As a stored procedure: delete the DECLARE block, and wrap the rest in
--        CREATE PROCEDURE dbo.usp_SitefinityFlattenDynamicType
--            @Module nvarchar(255), @Type nvarchar(255),
--            @Lifecycle varchar(6) = 'live',  -- 'live' | 'master'
--            @Mode varchar(6) = 'select',     -- 'select' | 'sql' | 'view' | 'poco'
--            @ViewName sysname = NULL,        -- required for 'view'
--            @IncludeDeleted bit = 0,         -- master mode: include status = 4 rows
--            @Top int = NULL,                 -- select mode only
--            @Namespace nvarchar(255) = NULL  -- poco mode: C# namespace (default Sitefinity.Flat)
--        AS BEGIN ... END
--   C) One view per type for BI: @Mode = 'view', @ViewName = 'dbo.vw_<Module>_<Type>'.
--
-- Read-only against Sitefinity tables in every mode except 'view' (which
-- creates/alters only the named view). Requires SQL Server 2017+.
-- ============================================================================
DECLARE @Module nvarchar(255) = N'Announcements', @Type nvarchar(255) = N'Announcement';
DECLARE @Lifecycle varchar(6) = 'live', @Mode varchar(6) = 'select', @ViewName sysname = NULL, @IncludeDeleted bit = 0, @Top int = 25, @Namespace nvarchar(255) = NULL;

SET NOCOUNT ON;

DECLARE @nl nchar(1) = CHAR(10);
DECLARE @sql nvarchar(max), @cols nvarchar(max) = N'', @joins nvarchar(max) = N'', @from nvarchar(max), @where nvarchar(max);
DECLARE @emptyGuid uniqueidentifier = '00000000-0000-0000-0000-000000000000';
DECLARE @nsPrefix varchar(50) = 'Telerik.Sitefinity.DynamicTypes.Model.';

IF @Lifecycle NOT IN ('live', 'master') BEGIN RAISERROR('@Lifecycle must be ''live'' or ''master''.', 16, 1); RETURN; END
IF @Mode NOT IN ('select', 'sql', 'view', 'poco') BEGIN RAISERROR('@Mode must be ''select'', ''sql'', ''view'' or ''poco''.', 16, 1); RETURN; END
IF @Mode = 'view' AND @ViewName IS NULL BEGIN RAISERROR('@ViewName is required when @Mode = ''view''.', 16, 1); RETURN; END
IF @Mode = 'view' AND (PARSENAME(@ViewName, 3) IS NOT NULL OR PARSENAME(@ViewName, 1) IS NULL) BEGIN RAISERROR('@ViewName must be "name" or "schema.name".', 16, 1); RETURN; END
IF @Mode = 'poco' AND @Namespace IS NOT NULL AND (@Namespace LIKE '%[^A-Za-z0-9_.]%' OR @Namespace LIKE '.%' OR @Namespace LIKE '%.' OR @Namespace LIKE '%..%') BEGIN RAISERROR('@Namespace must be a dotted C# identifier (letters, digits, underscore).', 16, 1); RETURN; END

-- ---------------------------------------------------------------------------
-- 1. Resolve the type from the Module Builder registry (accepts the module's
--    display name "Case Library", its namespace segment "CaseLibrary", or the
--    full CLR namespace).
-- ---------------------------------------------------------------------------
DECLARE @ns varchar(255), @typeName varchar(255), @mainField varchar(255), @parentTypeId uniqueidentifier, @metaTypeId uniqueidentifier;

SELECT TOP 1 @ns = t.type_namespace, @typeName = t.type_name, @mainField = t.main_short_text_field_name, @parentTypeId = NULLIF(t.parentTypeId, @emptyGuid)
FROM sf_mb_dynamic_module_type t
JOIN sf_mb_dynamic_module md ON md.id = t.parent_module_id
WHERE t.type_name = @Type
  AND (md.nme = @Module OR REPLACE(md.nme, ' ', '') = REPLACE(@Module, ' ', '') OR t.type_namespace = @Module OR t.type_namespace = @nsPrefix + REPLACE(@Module, ' ', ''));

IF @ns IS NULL BEGIN RAISERROR('Type "%s" in module "%s" was not found in sf_mb_dynamic_module_type.', 16, 1, @Type, @Module); RETURN; END

SELECT @metaTypeId = id FROM sf_meta_types WHERE name_space = @ns AND class_name = @typeName AND is_deleted = 0;
IF @metaTypeId IS NULL BEGIN RAISERROR('No live sf_meta_types row for %s.%s.', 16, 1, @ns, @typeName); RETURN; END

DECLARE @nsLast varchar(255) = CASE WHEN @ns LIKE @nsPrefix + '%' THEN SUBSTRING(@ns, LEN(@nsPrefix) + 1, 255) ELSE @ns END;
DECLARE @clrType varchar(500) = @ns + '.' + @typeName;

-- ---------------------------------------------------------------------------
-- 2. Field inventory from the ORM meta model (NOT sf_mb_dynamic_module_field:
--    its classification_id carries stale values on non-taxonomy fields).
--    Storage kind is decided by clr_type + taxonomy_id.
-- ---------------------------------------------------------------------------
DECLARE @fields TABLE (field_name sysname, column_name sysname NULL, clr_type varchar(500) NULL, taxonomy_id uniqueidentifier NULL, kind varchar(20), devowel varchar(255));

INSERT @fields (field_name, column_name, clr_type, taxonomy_id, kind, devowel)
SELECT mf.field_name, mf.column_name, mf.clr_type, mf.taxonomy_id,
       CASE WHEN mf.taxonomy_id IS NOT NULL AND mf.taxonomy_id <> @emptyGuid THEN 'taxonomy'
            WHEN mf.clr_type LIKE 'Telerik.Sitefinity.RelatedData.RelatedItems%' THEN 'related'
            WHEN mf.clr_type LIKE '%ChoiceOption[[]]%' THEN 'multichoice'
            WHEN mf.clr_type LIKE 'Telerik.Sitefinity.GeoLocations.Model.Address%' THEN 'address'
            ELSE 'column' END,
       REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(mf.field_name), 'a', ''), 'e', ''), 'i', ''), 'o', ''), 'u', '')
FROM sf_meta_fields mf
WHERE mf.type_id = @metaTypeId AND mf.is_deleted = 0;

-- ---------------------------------------------------------------------------
-- 3. Resolve the per-type table: mapping row -> naming convention -> column
--    signature (types whose table was named by hand, e.g. "resrce", "msg").
-- ---------------------------------------------------------------------------
DECLARE @table sysname, @how varchar(40);

SELECT @table = table_name, @how = 'sf_meta_data_mapping' FROM sf_meta_data_mapping WHERE field_name = '#NA' AND module_name = @nsLast AND type_name = @typeName;

IF @table IS NULL AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = LOWER(@nsLast + '_' + @typeName))
    SELECT @table = LOWER(@nsLast + '_' + @typeName), @how = 'naming convention';

IF @table IS NULL
    SELECT TOP 1 @table = x.TABLE_NAME, @how = 'column signature (' + CAST(x.score AS varchar(5)) + ' of ' + CAST(x.expected AS varchar(5)) + ' columns)'
    FROM (
        SELECT t.TABLE_NAME,
               (SELECT COUNT(*) FROM @fields f JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_NAME = t.TABLE_NAME AND c.COLUMN_NAME = f.column_name WHERE f.kind IN ('column', 'address')) AS score,
               (SELECT COUNT(*) FROM @fields f WHERE f.kind IN ('column', 'address')) AS expected
        FROM INFORMATION_SCHEMA.TABLES t
        WHERE t.TABLE_SCHEMA = 'dbo' AND t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_NAME NOT LIKE 'sf[_]%'
          AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME AND c.COLUMN_NAME = 'base_id')
          AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME AND c.COLUMN_NAME = 'seq')
          AND NOT EXISTS (SELECT 1 FROM sf_meta_data_mapping m WHERE m.table_name = t.TABLE_NAME)
    ) x
    WHERE x.score > 0 AND x.score = x.expected
    ORDER BY x.score DESC;

IF @table IS NULL BEGIN RAISERROR('Could not resolve the physical table for %s (no mapping row, no conventional name, no column-signature match).', 16, 1, @clrType); RETURN; END

-- Parent type (hierarchical modules): parent_id on the type table -> parent MASTER base_id
DECLARE @parentTable sysname, @parentMainField sysname, @parentTypeName varchar(255), @parentNs varchar(255);
IF @parentTypeId IS NOT NULL
BEGIN
    SELECT @parentTypeName = pt.type_name, @parentNs = pt.type_namespace, @parentMainField = pt.main_short_text_field_name FROM sf_mb_dynamic_module_type pt WHERE pt.id = @parentTypeId;
    DECLARE @pNsLast varchar(255) = CASE WHEN @parentNs LIKE @nsPrefix + '%' THEN SUBSTRING(@parentNs, LEN(@nsPrefix) + 1, 255) ELSE @parentNs END;
    SELECT @parentTable = table_name FROM sf_meta_data_mapping WHERE field_name = '#NA' AND module_name = @pNsLast AND type_name = @parentTypeName;
    IF @parentTable IS NULL AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = LOWER(@pNsLast + '_' + @parentTypeName)) SET @parentTable = LOWER(@pNsLast + '_' + @parentTypeName);
END

-- ---------------------------------------------------------------------------
-- 4. Junction tables: discovered through their FK to the type table (the ORM
--    always emits that one), never by name - long names are vowel-stripped
--    and truncated to 30 chars ("annncmnts_nnouncement_category").
--    val uniqueidentifier = taxonomy field (-> sf_taxa); val varchar = multi-
--    select Choices field (the value strings). Junction -> field is matched by
--    comparing the de-voweled tail of the table name with the de-voweled field
--    name (the mangler strips vowels/truncates, it never reorders).
-- ---------------------------------------------------------------------------
DECLARE @junctions TABLE (table_name sysname, val_type varchar(30), devowel varchar(255), field_name sysname NULL, score int NULL);

INSERT @junctions (table_name, val_type, devowel)
SELECT OBJECT_NAME(fk.parent_object_id), c.DATA_TYPE,
       REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(OBJECT_NAME(fk.parent_object_id)), '_', ''), 'a', ''), 'e', ''), 'i', ''), 'o', ''), 'u', '')
FROM sys.foreign_keys fk
JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_NAME = OBJECT_NAME(fk.parent_object_id) AND c.COLUMN_NAME = 'val'
WHERE fk.referenced_object_id = OBJECT_ID('dbo.' + QUOTENAME(@table))
  AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS s WHERE s.TABLE_NAME = OBJECT_NAME(fk.parent_object_id) AND s.COLUMN_NAME = 'seq');

;WITH n AS (SELECT TOP 60 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects),
cand AS (
    SELECT j.table_name, f.field_name, n.n AS score,
           ROW_NUMBER() OVER (PARTITION BY j.table_name ORDER BY n.n DESC, f.field_name) AS rn
    FROM @junctions j
    JOIN @fields f ON (j.val_type = 'uniqueidentifier' AND f.kind = 'taxonomy') OR (j.val_type IN ('varchar', 'nvarchar') AND f.kind = 'multichoice')
    JOIN n ON n.n BETWEEN 3 AND LEN(f.devowel)
    WHERE RIGHT(j.devowel, n.n) = LEFT(f.devowel, n.n)
)
UPDATE j SET field_name = c.field_name, score = c.score
FROM @junctions j JOIN cand c ON c.table_name = j.table_name AND c.rn = 1;

-- ---------------------------------------------------------------------------
-- 5. Build the SELECT. Aliases: m = master row, l = live row (LEFT JOIN),
--    r = the row whose values are read (m or l), t = per-type row of r.
--    Lifecycle columns are prefixed sf_ so they never collide with a field.
-- ---------------------------------------------------------------------------
DECLARE @r nchar(1) = CASE @Lifecycle WHEN 'live' THEN 'l' ELSE 'm' END;
DECLARE @availFlag sysname = CASE @Lifecycle WHEN 'live' THEN 'available_for_live' ELSE 'available_for_master' END;

SET @cols = N'    m.base_id AS sf_master_id,' + @nl
  + N'    ' + @r + N'.base_id AS sf_row_id,' + @nl
  + N'    ''' + @Lifecycle + N''' AS sf_lifecycle,' + @nl
  + N'    CASE WHEN m.status = 4 THEN ''Deleted''' + @nl
  + N'         WHEN l.base_id IS NOT NULL AND l.visible = 1 THEN ''Published''' + @nl
  + N'         WHEN sch.execute_time IS NOT NULL THEN ''Scheduled''' + @nl
  + N'         WHEN l.base_id IS NOT NULL AND l.expiration_date IS NOT NULL AND l.expiration_date < GETUTCDATE() THEN ''Expired''' + @nl
  + N'         WHEN l.base_id IS NOT NULL THEN ''Unpublished''' + @nl
  + N'         ELSE ''Draft'' END AS sf_publication_state,' + @nl
  + N'    CASE WHEN l.base_id IS NOT NULL AND l.visible = 1 THEN 1 ELSE 0 END AS sf_is_published,' + @nl
  + N'    sch.execute_time AS sf_scheduled_publish_utc,' + @nl
  + N'    m.url_name_ AS sf_url_name,' + @nl
  + N'    u.url AS sf_default_url,' + @nl
  + N'    ' + @r + N'.publication_date AS sf_publication_date_utc,' + @nl
  + N'    ' + @r + N'.expiration_date AS sf_expiration_date_utc,' + @nl
  + N'    ' + @r + N'.date_created AS sf_date_created_utc,' + @nl
  + N'    ' + @r + N'.last_modified AS sf_last_modified_utc,' + @nl
  + N'    ' + @r + N'.ownr AS sf_owner_id,' + @nl
  + N'    ou.user_name AS sf_owner_user_name,' + @nl
  + N'    ' + @r + N'.last_modified_by AS sf_last_modified_by_id,' + @nl
  + N'    mu.user_name AS sf_last_modified_by_user_name,' + @nl
  + N'    m.application_name AS sf_provider,' + @nl
  + N'    ' + @r + N'.vrsion AS sf_version,' + @nl
  + N'    m.system_parent_id AS sf_parent_master_id,' + @nl
  + N'    m.system_parent_type AS sf_parent_type';

-- parent title (hierarchical types)
IF @parentTable IS NOT NULL AND @parentMainField IS NOT NULL AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @parentTable AND COLUMN_NAME = @parentMainField)
BEGIN
    SET @cols = @cols + N',' + @nl + N'    pt.' + QUOTENAME(@parentMainField) + N' AS sf_parent_' + LOWER(@parentMainField);
    SET @joins = @joins + N'LEFT JOIN ' + QUOTENAME(@parentTable) + N' pt ON pt.base_id = m.system_parent_id' + @nl;
END

-- plain columns, in physical order, skipping storage internals
SELECT @cols = @cols + N',' + @nl + N'    t.' + QUOTENAME(c.COLUMN_NAME)
    + CASE WHEN f.kind = 'address' THEN N' AS ' + QUOTENAME(c.COLUMN_NAME + '_address_id') + N',' + @nl
        + N'    (SELECT a.street AS Street, a.city AS City, a.state_code AS StateCode, a.zip AS Zip, a.country_code AS CountryCode, CAST(a.latitude AS decimal(12, 8)) AS Latitude, CAST(a.longitude AS decimal(12, 8)) AS Longitude, a.map_zoom_level AS MapZoomLevel FROM sf_addresses a WHERE a.id = t.' + QUOTENAME(c.COLUMN_NAME) + N' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS ' + QUOTENAME(c.COLUMN_NAME)
      ELSE N'' END
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN @fields f ON f.column_name = c.COLUMN_NAME
WHERE c.TABLE_NAME = @table AND c.COLUMN_NAME NOT IN ('base_id', 'parent_id', 'voa_version', 'voa_class')
ORDER BY c.ORDINAL_POSITION;

-- taxonomy + multi-choice junctions as JSON
SELECT @cols = @cols + N',' + @nl
    + CASE WHEN j.val_type = 'uniqueidentifier'
        THEN N'    (SELECT ta.id AS Id, ta.title_ AS Title, ta.url_name_ AS UrlName, tx.nme AS Taxonomy FROM ' + QUOTENAME(j.table_name) + N' j JOIN sf_taxa ta ON ta.id = j.val JOIN sf_taxonomies tx ON tx.id = ta.taxonomy_id WHERE j.base_id = ' + @r + N'.base_id ORDER BY j.seq FOR JSON PATH)'
        ELSE N'    (SELECT ''['' + STRING_AGG(''"'' + STRING_ESCAPE(CAST(j.val AS nvarchar(max)), ''json'') + ''"'', '','') WITHIN GROUP (ORDER BY j.seq) + '']'' FROM ' + QUOTENAME(j.table_name) + N' j WHERE j.base_id = ' + @r + N'.base_id)'
      END
    + N' AS ' + QUOTENAME(ISNULL(j.field_name, 'junction:' + j.table_name))
FROM @junctions j
ORDER BY j.field_name, j.table_name;

-- related data / related media as JSON (sf_content_link, keyed on the MASTER id in both lifecycles)
SELECT @cols = @cols + N',' + @nl
    + N'    (SELECT cl.child_item_id AS Id, cl.child_item_type AS Type, cl.child_item_provider_name AS Provider, CAST(cl.ordinal AS decimal(18, 4)) AS Ordinal FROM sf_content_link cl WHERE cl.parent_item_id = m.base_id AND cl.parent_item_type = ''' + @clrType + N''' AND cl.component_property_name = ''' + f.field_name + N''' AND cl.' + @availFlag + N' = 1 AND cl.is_child_deleted = 0 ORDER BY cl.ordinal FOR JSON PATH) AS ' + QUOTENAME(f.field_name)
FROM @fields f
WHERE f.kind = 'related'
ORDER BY f.field_name;

-- row sources
IF @Lifecycle = 'live'
    SET @from = N'FROM sf_dynamic_content l' + @nl
        + N'JOIN sf_dynamic_content m ON m.base_id = l.original_content_id AND m.status IN (0' + CASE WHEN @IncludeDeleted = 1 THEN N', 4' ELSE N'' END + N')' + @nl
        + N'JOIN ' + QUOTENAME(@table) + N' t ON t.base_id = l.base_id' + @nl;
ELSE
    SET @from = N'FROM sf_dynamic_content m' + @nl
        + N'LEFT JOIN sf_dynamic_content l ON l.original_content_id = m.base_id AND l.status = 2' + @nl
        + N'JOIN ' + QUOTENAME(@table) + N' t ON t.base_id = m.base_id' + @nl;

SET @from = @from
    + N'LEFT JOIN sf_users ou ON ou.id = ' + @r + N'.ownr' + @nl
    + N'LEFT JOIN sf_users mu ON mu.id = ' + @r + N'.last_modified_by' + @nl
    + @joins
    + N'OUTER APPLY (SELECT TOP 1 ud.url FROM sf_url_data ud WHERE ud.content_id = m.base_id AND ud.app_name = ''/DynamicModule'' AND ud.is_default = 1 AND ud.redirect = 0 ORDER BY ud.last_modified DESC) u' + @nl
    + N'OUTER APPLY (SELECT MIN(s.execute_time) AS execute_time FROM sf_scheduled_tasks s WHERE s.task_name = ''WorkflowCallTask'' AND s.enabled = 1 AND s.execute_time > GETUTCDATE() AND s.task_data LIKE ''%OperationName="Publish"%'' AND s.task_data LIKE ''%ContentItemMasterId="'' + LOWER(CAST(m.base_id AS varchar(36))) + ''"%'') sch' + @nl;

SET @where = CASE WHEN @Lifecycle = 'live' THEN N'WHERE l.status = 2' ELSE N'WHERE m.status IN (0' + CASE WHEN @IncludeDeleted = 1 THEN N', 4' ELSE N'' END + N')' END + @nl;

SET @sql = N'SELECT' + CASE WHEN @Mode = 'select' AND @Top IS NOT NULL THEN N' TOP (' + CAST(@Top AS nvarchar(10)) + N')' ELSE N'' END + @nl + @cols + @nl + @from + @where;

-- ---------------------------------------------------------------------------
-- 6. Emit / run
-- ---------------------------------------------------------------------------
DECLARE @juncCount int, @juncList nvarchar(max), @relCount int;
SELECT @juncCount = COUNT(*), @juncList = STRING_AGG(j.table_name + '->' + ISNULL(j.field_name, '?'), ', ') FROM @junctions j;
SELECT @relCount = COUNT(*) FROM @fields WHERE kind = 'related';
PRINT '-- ' + @clrType + ' -> ' + QUOTENAME(@table) + ' (resolved via ' + @how + '), lifecycle=' + @Lifecycle
    + ', junctions=' + CAST(@juncCount AS varchar(5)) + ' (' + ISNULL(@juncList, 'none') + ')'
    + ', related=' + CAST(@relCount AS varchar(5));

IF @Mode = 'sql'
BEGIN
    SELECT @sql AS generated_sql;   -- PRINT truncates at 4000 chars; SELECT returns the whole text
    RETURN;
END

IF @Mode = 'poco'
BEGIN
    -- C# row class for the SELECT above. Base class + JSON helper types live in
    -- FlatRowSupport.cs (shipped with the skill). Property names = column names,
    -- so Dapper maps them as-is; EF Core maps them as a keyless entity.
    DECLARE @cs nvarchar(max), @className sysname = @typeName + N'FlatRow', @nsCs nvarchar(255) = ISNULL(@Namespace, N'Sitefinity.Flat');
    DECLARE @csProps nvarchar(max) = N'';

    SELECT @csProps = @csProps
        + CASE WHEN f.kind = 'address' THEN
              N'        /// <summary>Raw sf_addresses id.</summary>' + @nl
            + N'        public Guid? ' + c.COLUMN_NAME + N'_address_id { get; set; }' + @nl + @nl
            + N'        /// <summary>JSON object from sf_addresses; see Address' + c.COLUMN_NAME + N'.</summary>' + @nl
            + N'        public string ' + c.COLUMN_NAME + N' { get; set; }' + @nl
            + N'        public AddressRef Address' + c.COLUMN_NAME + N' => FlatJson.Address(this.' + c.COLUMN_NAME + N');' + @nl + @nl
          ELSE
              N'        public ' + CASE c.DATA_TYPE
                    WHEN 'uniqueidentifier' THEN N'Guid?'
                    WHEN 'bit' THEN N'bool?'
                    WHEN 'int' THEN N'int?'
                    WHEN 'bigint' THEN N'long?'
                    WHEN 'smallint' THEN N'short?'
                    WHEN 'tinyint' THEN N'byte?'
                    WHEN 'decimal' THEN N'decimal?'
                    WHEN 'numeric' THEN N'decimal?'
                    WHEN 'money' THEN N'decimal?'
                    WHEN 'float' THEN N'double?'
                    WHEN 'real' THEN N'float?'
                    WHEN 'datetime' THEN N'DateTime?'
                    WHEN 'datetime2' THEN N'DateTime?'
                    WHEN 'date' THEN N'DateTime?'
                    WHEN 'varbinary' THEN N'byte[]'
                    ELSE N'string' END
            + N' ' + c.COLUMN_NAME + N' { get; set; }'
            + CASE WHEN c.DATA_TYPE IN ('datetime', 'datetime2') THEN N'   // UTC' ELSE N'' END
            + CASE WHEN f.clr_type LIKE '%.Lstring' AND c.DATA_TYPE LIKE '%char' THEN N'   // localizable (default culture)' ELSE N'' END
            + CASE WHEN f.clr_type LIKE '%ChoiceOption' THEN N'   // single-select choice value' ELSE N'' END
            + @nl
          END
    FROM INFORMATION_SCHEMA.COLUMNS c
    LEFT JOIN @fields f ON f.column_name = c.COLUMN_NAME
    WHERE c.TABLE_NAME = @table AND c.COLUMN_NAME NOT IN ('base_id', 'parent_id', 'voa_version', 'voa_class')
    ORDER BY c.ORDINAL_POSITION;

    SELECT @csProps = @csProps + @nl
        + CASE WHEN j.val_type = 'uniqueidentifier'
            THEN N'        /// <summary>JSON array of taxa (Id, Title, UrlName, Taxonomy); see ' + ISNULL(j.field_name, 'Junction_' + j.table_name) + N'Items.</summary>' + @nl
               + N'        public string ' + ISNULL(j.field_name, 'junction_' + j.table_name) + N' { get; set; }' + @nl
               + N'        public IReadOnlyList<TaxonRef> ' + ISNULL(j.field_name, 'Junction_' + j.table_name) + N'Items => FlatJson.Taxa(this.' + ISNULL(j.field_name, 'junction_' + j.table_name) + N');' + @nl
            ELSE N'        /// <summary>JSON array of selected choice values; see ' + ISNULL(j.field_name, 'Junction_' + j.table_name) + N'Values.</summary>' + @nl
               + N'        public string ' + ISNULL(j.field_name, 'junction_' + j.table_name) + N' { get; set; }' + @nl
               + N'        public IReadOnlyList<string> ' + ISNULL(j.field_name, 'Junction_' + j.table_name) + N'Values => FlatJson.Strings(this.' + ISNULL(j.field_name, 'junction_' + j.table_name) + N');' + @nl
          END
    FROM @junctions j ORDER BY j.field_name, j.table_name;

    SELECT @csProps = @csProps + @nl
        + N'        /// <summary>JSON array of related items (Id, Type, Provider, Ordinal); see ' + f.field_name + N'Items.</summary>' + @nl
        + N'        public string ' + f.field_name + N' { get; set; }' + @nl
        + N'        public IReadOnlyList<RelatedRef> ' + f.field_name + N'Items => FlatJson.Related(this.' + f.field_name + N');' + @nl
    FROM @fields f WHERE f.kind = 'related' ORDER BY f.field_name;

    SET @cs = N'// Generated by flatten-dynamic-content.sql (@Mode = poco) for ' + @clrType + N' -> ' + @table + @nl
        + N'// Pair with FlatRowSupport.cs (ServiceStack.Text). Dapper: set DefaultTypeMap.MatchNamesWithUnderscores = true (base-class columns are sf_*).' + @nl
        + N'// EF Core: modelBuilder.Entity<' + @className + N'>().HasNoKey().ToView("<view name>") or FromSqlRaw(...).' + @nl
        + N'using System;' + @nl + N'using System.Collections.Generic;' + @nl + N'using System.ComponentModel.DataAnnotations.Schema;' + @nl + @nl
        + N'namespace ' + @nsCs + @nl + N'{' + @nl
        + N'    public sealed class ' + @className + N' : SitefinityFlatRowBase' + @nl
        + N'    {' + @nl
        + CASE WHEN @parentTable IS NOT NULL AND @parentMainField IS NOT NULL AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @parentTable AND COLUMN_NAME = @parentMainField)
               THEN N'        [Column("sf_parent_' + LOWER(@parentMainField) + N'")]' + @nl + N'        public string SfParent' + @parentMainField + N' { get; set; }' + @nl + @nl ELSE N'' END
        + @csProps
        + N'    }' + @nl + N'}' + @nl;

    SELECT @cs AS generated_csharp;
    RETURN;
END

IF @Mode = 'view'
BEGIN
    DECLARE @viewQuoted nvarchar(600) = QUOTENAME(ISNULL(PARSENAME(@ViewName, 2), N'dbo')) + N'.' + QUOTENAME(PARSENAME(@ViewName, 1));
    SET @sql = N'CREATE OR ALTER VIEW ' + @viewQuoted + N' AS' + @nl + @sql;
    EXEC sp_executesql @sql;
    PRINT '-- created view ' + @viewQuoted;
    RETURN;
END

SET @sql = @sql + N'ORDER BY ' + @r + N'.last_modified DESC;';
EXEC sp_executesql @sql;
