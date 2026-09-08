-- Read-only verification queries for the sitefinity-database-structure skill.
-- Run against a COPY of a Sitefinity database (verified on 15.4.8636). Every query is a SELECT.
-- Each block is independent; GO separates them so one failure does not abort the rest.
SET NOCOUNT ON;

PRINT '== Sitefinity schema build (8636 = 15.4)';
SELECT module_name, version_number, previous_version_number FROM sf_schema_vrsns WHERE module_name = 'Sitefinity';
GO
PRINT '== FK inventory: every constrained relationship (anything not listed is convention only)';
SELECT OBJECT_NAME(fk.parent_object_id) AS child_table, c.name AS child_col,
       OBJECT_NAME(fk.referenced_object_id) AS parent_table, rc.name AS parent_col, fk.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns c  ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY child_table, child_col;
GO
PRINT '== Core tables carry NO foreign keys (expect all zeros)';
SELECT t.name, (SELECT COUNT(*) FROM sys.foreign_keys fk WHERE fk.parent_object_id = t.object_id) AS fks_out
FROM sys.tables t
WHERE t.name IN ('sf_page_node','sf_page_data','sf_draft_pages','sf_object_data','sf_control_properties','sf_page_templates',
                 'sf_dynamic_content','sf_content_link','sf_url_data','sf_taxa','sf_permissions','sf_meta_fields','sf_version_chnges')
ORDER BY t.name;
GO
PRINT '== Table-name registry (Module Builder types + generated form tables)';
SELECT module_name, type_name, field_name, table_name FROM sf_meta_data_mapping ORDER BY module_name, type_name;
GO
PRINT '== dynmc_ tables are publishing-point types, not Module Builder';
SELECT class_name, base_class_name, module_name FROM sf_meta_types WHERE class_name LIKE 'Dynamic[_]%';
GO
PRINT '== sf_draft_pages families (PageDraft: page_id; TemplateDraft: page_id; FormDraft: content_id + nme)';
SELECT voa_class, is_temp_draft, COUNT(*) AS rows_,
       SUM(CASE WHEN page_id IS NOT NULL THEN 1 ELSE 0 END) AS via_page_id,
       SUM(CASE WHEN content_id IS NOT NULL THEN 1 ELSE 0 END) AS via_content_id,
       SUM(CASE WHEN EXISTS (SELECT 1 FROM sf_page_data pd WHERE pd.content_id = d.page_id) THEN 1 ELSE 0 END) AS page_drafts,
       SUM(CASE WHEN EXISTS (SELECT 1 FROM sf_page_templates pt WHERE pt.id = d.page_id) THEN 1 ELSE 0 END) AS template_drafts,
       MIN(nme) AS sample_form_name
FROM sf_draft_pages d GROUP BY voa_class, is_temp_draft ORDER BY rows_ DESC;
GO
PRINT '== sf_object_data families and their owner columns';
SELECT voa_class, COUNT(*) AS rows_,
       SUM(CASE WHEN page_id IS NOT NULL THEN 1 ELSE 0 END) AS via_page_id,
       SUM(CASE WHEN content_id IS NOT NULL THEN 1 ELSE 0 END) AS via_content_id,
       SUM(CASE WHEN id3 IS NOT NULL THEN 1 ELSE 0 END) AS via_id3,
       SUM(CASE WHEN parent_prop_id IS NOT NULL THEN 1 ELSE 0 END) AS via_parent_prop,
       COUNT(DISTINCT object_type) AS distinct_types, MIN(object_type) AS sample_type
FROM sf_object_data GROUP BY voa_class ORDER BY rows_ DESC;
GO
PRINT '== Owner classification of sf_object_data.page_id (live page / draft / template / dangling)';
SELECT k, COUNT(*) AS rows_ FROM (
  SELECT CASE WHEN pd.content_id IS NOT NULL THEN 'live page'
              WHEN dp.id IS NOT NULL AND dp.page_id IS NOT NULL THEN 'page or template draft'
              WHEN dp.id IS NOT NULL THEN 'form draft'
              WHEN pt.id IS NOT NULL THEN 'template'
              WHEN od.page_id IS NULL THEN 'page_id NULL (form control / nested / orphan)'
              ELSE 'dangling page_id' END AS k
  FROM sf_object_data od
  LEFT JOIN sf_page_data pd ON pd.content_id = od.page_id
  LEFT JOIN sf_draft_pages dp ON dp.id = od.page_id
  LEFT JOIN sf_page_templates pt ON pt.id = od.page_id) x
GROUP BY k ORDER BY rows_ DESC;
GO
PRINT '== Control property levels (L1 has control_id, L2+ has prnt_prop_id; never both)';
SELECT CASE WHEN control_id IS NOT NULL AND prnt_prop_id IS NULL THEN 'L1'
            WHEN control_id IS NULL AND prnt_prop_id IS NOT NULL THEN 'L2+'
            WHEN control_id IS NOT NULL AND prnt_prop_id IS NOT NULL THEN 'both (unexpected)'
            ELSE 'neither (orphan)' END AS lvl, COUNT(*) AS rows_
FROM sf_control_properties GROUP BY CASE WHEN control_id IS NOT NULL AND prnt_prop_id IS NULL THEN 'L1'
            WHEN control_id IS NULL AND prnt_prop_id IS NOT NULL THEN 'L2+'
            WHEN control_id IS NOT NULL AND prnt_prop_id IS NOT NULL THEN 'both (unexpected)'
            ELSE 'neither (orphan)' END;
GO
PRINT '== Page node -> page data pointer integrity (node_dangling, data_orphan, nodes_with_multiple_live_rows)';
SELECT (SELECT COUNT(*) FROM sf_page_node pn WHERE pn.content_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sf_page_data pd WHERE pd.content_id = pn.content_id)) AS node_dangling,
       (SELECT COUNT(*) FROM sf_page_data pd WHERE NOT EXISTS (SELECT 1 FROM sf_page_node pn WHERE pn.id = pd.page_node_id)) AS data_orphan,
       (SELECT COUNT(*) FROM (SELECT page_node_id FROM sf_page_data WHERE status = 2 GROUP BY page_node_id HAVING COUNT(*) > 1) x) AS multi_live;
GO
PRINT '== Page data culture / status / visibility mix';
SELECT culture, status, visible, COUNT(*) AS rows_ FROM sf_page_data GROUP BY culture, status, visible ORDER BY rows_ DESC;
GO
PRINT '== Page node types (0 Standard, 1 Group, 2 External, 3 InnerRedirect, 4 OuterRedirect, 5 Rewriting)';
SELECT node_type, COUNT(*) AS rows_, SUM(CAST(is_deleted AS int)) AS deleted FROM sf_page_node GROUP BY node_type;
GO
PRINT '== Dynamic content: states are separate rows; live.original_content_id -> master.base_id (expect via_base = live, via_id = 0)';
SELECT (SELECT COUNT(*) FROM sf_dynamic_content l WHERE l.status = 2 AND EXISTS (SELECT 1 FROM sf_dynamic_content m WHERE m.base_id = l.original_content_id)) AS via_base,
       (SELECT COUNT(*) FROM sf_dynamic_content l WHERE l.status = 2 AND EXISTS (SELECT 1 FROM sf_dynamic_content m WHERE m.id = l.original_content_id)) AS via_id,
       (SELECT COUNT(*) FROM sf_dynamic_content WHERE status = 2) AS live_rows;
GO
PRINT '== Dynamic content per type and state (voa_class = type; status 0 master / 1 temp / 2 live / 4 deleted)';
SELECT voa_class, status, COUNT(*) AS rows_ FROM sf_dynamic_content GROUP BY voa_class, status ORDER BY voa_class, status;
GO
PRINT '== Everything points at the MASTER id: url_data, content_link, permissions, version items (expect *_id columns = 0)';
SELECT (SELECT COUNT(*) FROM sf_url_data u WHERE u.app_name = '/DynamicModule' AND EXISTS (SELECT 1 FROM sf_dynamic_content d WHERE d.base_id = u.content_id)) AS url_via_base,
       (SELECT COUNT(*) FROM sf_url_data u WHERE u.app_name = '/DynamicModule' AND EXISTS (SELECT 1 FROM sf_dynamic_content d WHERE d.id = u.content_id)) AS url_via_id,
       (SELECT COUNT(*) FROM sf_content_link l WHERE l.parent_item_type LIKE 'Telerik.Sitefinity.DynamicTypes%' AND EXISTS (SELECT 1 FROM sf_dynamic_content d WHERE d.base_id = l.parent_item_id)) AS link_parent_via_base,
       (SELECT COUNT(*) FROM sf_content_link l WHERE l.child_item_type LIKE 'Telerik.Sitefinity.DynamicTypes%' AND EXISTS (SELECT 1 FROM sf_dynamic_content d WHERE d.base_id = l.child_item_id)) AS link_child_via_base,
       (SELECT COUNT(*) FROM sf_vesion_items v WHERE v.type_name LIKE 'Telerik.Sitefinity.DynamicTypes%' AND EXISTS (SELECT 1 FROM sf_dynamic_content d WHERE d.base_id = v.id)) AS version_via_base,
       (SELECT COUNT(*) FROM sf_vesion_items v WHERE v.type_name LIKE 'Telerik.Sitefinity.DynamicTypes%' AND EXISTS (SELECT 1 FROM sf_dynamic_content d WHERE d.id = v.id)) AS version_via_id;
GO
PRINT '== Media: live -> master, parent -> library, file links (expect full resolution)';
SELECT (SELECT COUNT(*) FROM sf_media_content l JOIN sf_media_content m ON m.content_id = l.original_content_id WHERE l.status = 2) AS live_to_master,
       (SELECT COUNT(*) FROM sf_media_content WHERE status = 2) AS live_rows,
       (SELECT COUNT(*) FROM sf_media_content m JOIN sf_libraries l ON l.content_id = m.parent_id) AS parent_resolves,
       (SELECT COUNT(*) FROM sf_media_content) AS media_rows,
       (SELECT COUNT(*) FROM sf_media_content m JOIN sf_media_file_links fl ON fl.content_id = m.content_id) AS file_links;
GO
PRINT '== Media flat-mapped classes (discover Image / Video / Document by voa_class)';
SELECT voa_class, COUNT(*) AS rows_, MIN(mime_type) AS a_mime, MAX(mime_type) AS z_mime,
       SUM(CASE WHEN width IS NOT NULL THEN 1 ELSE 0 END) AS has_width, SUM(CASE WHEN width2 IS NOT NULL THEN 1 ELSE 0 END) AS has_width2
FROM sf_media_content GROUP BY voa_class;
GO
PRINT '== URL rows by owner (pages: Sitefinity/ with id2 = page node)';
SELECT app_name, voa_class, item_type, COUNT(*) AS rows_,
       SUM(CASE WHEN content_id IS NOT NULL THEN 1 ELSE 0 END) AS has_content_id,
       SUM(CASE WHEN id2 IS NOT NULL THEN 1 ELSE 0 END) AS has_id2,
       SUM(CAST(is_default AS int)) AS defaults, SUM(CAST(redirect AS int)) AS redirects
FROM sf_url_data GROUP BY app_name, voa_class, item_type ORDER BY rows_ DESC;
GO
PRINT '== Page URLs: id2 resolves to sf_page_node, never sf_page_data';
SELECT (SELECT COUNT(*) FROM sf_url_data u JOIN sf_page_node n ON n.id = u.id2 WHERE u.app_name = 'Sitefinity/') AS node_hits,
       (SELECT COUNT(*) FROM sf_url_data u JOIN sf_page_data d ON d.content_id = u.id2 WHERE u.app_name = 'Sitefinity/') AS data_hits,
       (SELECT COUNT(*) FROM sf_url_data WHERE app_name = 'Sitefinity/' AND id2 IS NULL) AS null_id2,
       (SELECT COUNT(*) FROM sf_url_data WHERE app_name = 'Sitefinity/' AND id2 IS NULL AND redirect = 1) AS null_id2_redirects;
GO
PRINT '== Content links by relation (with lifecycle availability flags)';
SELECT TOP 40 parent_item_type, component_property_name, child_item_type, COUNT(*) AS rows_,
       SUM(CAST(available_for_live AS int)) AS live_, SUM(CAST(available_for_master AS int)) AS master_, SUM(CAST(available_for_temp AS int)) AS temp_
FROM sf_content_link GROUP BY parent_item_type, component_property_name, child_item_type ORDER BY rows_ DESC;
GO
PRINT '== Permissions: sets, principals, and which tables object_id lands in';
SELECT set_name, COUNT(*) AS rows_ FROM sf_permissions GROUP BY set_name ORDER BY rows_ DESC;
GO
SELECT (SELECT COUNT(*) FROM sf_permissions p JOIN sf_roles r ON r.id = p.principal_id) AS principal_role,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_users u ON u.id = p.principal_id) AS principal_user,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_page_node x ON x.id = p.object_id) AS on_page_node,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_object_data x ON x.id = p.object_id) AS on_control,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_media_content x ON x.content_id = p.object_id) AS on_media,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_libraries x ON x.content_id = p.object_id) AS on_library,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_dynamic_content x ON x.base_id = p.object_id) AS on_dynamic,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_page_templates x ON x.id = p.object_id) AS on_template,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_taxonomies x ON x.id = p.object_id) AS on_taxonomy,
       (SELECT COUNT(*) FROM sf_permissions p JOIN sf_security_roots x ON x.id = p.object_id) AS on_security_root,
       (SELECT COUNT(*) FROM sf_permissions) AS total_;
GO
PRINT '== Approval tracking: workflow_item_id is the page NODE for pages, the master id for content';
SELECT (SELECT COUNT(*) FROM sf_approval_tracking_record a JOIN sf_page_node d ON d.id = a.workflow_item_id) AS page_node,
       (SELECT COUNT(*) FROM sf_approval_tracking_record a JOIN sf_page_data d ON d.content_id = a.workflow_item_id) AS page_data,
       (SELECT COUNT(*) FROM sf_approval_tracking_record a JOIN sf_dynamic_content d ON d.base_id = a.workflow_item_id) AS dynamic_master,
       (SELECT COUNT(*) FROM sf_approval_tracking_record a JOIN sf_media_content d ON d.content_id = a.workflow_item_id) AS media,
       (SELECT COUNT(*) FROM sf_approval_tracking_record) AS total_;
GO
PRINT '== Versioning: item_id is the page DATA id for pages; type names per versioned item';
SELECT (SELECT COUNT(*) FROM sf_version_chnges a JOIN sf_page_data d ON d.content_id = a.item_id) AS page_data,
       (SELECT COUNT(*) FROM sf_version_chnges a JOIN sf_page_node d ON d.id = a.item_id) AS page_node,
       (SELECT COUNT(*) FROM sf_version_chnges a JOIN sf_dynamic_content d ON d.base_id = a.item_id) AS dynamic_master,
       (SELECT COUNT(*) FROM sf_version_chnges a JOIN sf_media_content d ON d.content_id = a.item_id) AS media,
       (SELECT COUNT(*) FROM sf_version_chnges) AS total_;
GO
SELECT type_name, COUNT(*) AS rows_ FROM sf_vesion_items GROUP BY type_name ORDER BY rows_ DESC;
GO
SELECT change_type, is_published_version, COUNT(*) AS rows_ FROM sf_version_chnges GROUP BY change_type, is_published_version;
GO
PRINT '== Forms: descriptions, drafts (content_id), controls (content_id) vs draft controls (id3), orphan draft controls';
SELECT (SELECT COUNT(*) FROM sf_form_description) AS forms,
       (SELECT COUNT(*) FROM sf_draft_pages WHERE content_id IS NOT NULL) AS form_drafts,
       (SELECT COUNT(*) FROM sf_object_data o JOIN sf_form_description f ON f.content_id = o.content_id) AS form_controls,
       (SELECT COUNT(*) FROM sf_object_data o JOIN sf_draft_pages d ON d.id = o.id3) AS form_draft_controls,
       (SELECT COUNT(*) FROM sf_object_data o WHERE o.id3 IS NULL AND o.content_id IS NULL AND o.page_id IS NULL AND o.parent_prop_id IS NULL AND o.published2 IS NOT NULL) AS orphan_form_draft_controls;
GO
PRINT '== Form entries: user link, per-form vertical table join';
SELECT (SELECT COUNT(*) FROM sf_form_entry) AS entries,
       (SELECT COUNT(*) FROM sf_form_entry e JOIN sf_users u ON u.id = e.user_id) AS entries_with_known_user,
       (SELECT COUNT(DISTINCT voa_class) FROM sf_form_entry) AS distinct_forms;
GO
PRINT '== Taxonomy junctions resolve to sf_taxa (pick one stock and one Module Builder junction on your site)';
SELECT (SELECT COUNT(*) FROM sf_media_content_tags j) AS media_tag_rows,
       (SELECT COUNT(*) FROM sf_media_content_tags j JOIN sf_taxa t ON t.id = j.val) AS media_tag_hits;
GO
PRINT '== Multisite';
SELECT id, nme, is_default, site_map_root_node_id, home_page_id, default_frontend_template_id FROM sf_sites;
GO
SELECT item_type, COUNT(*) AS rows_ FROM sf_site_item_links GROUP BY item_type ORDER BY rows_ DESC;
GO
PRINT '== Owners pointing at deleted users (page nodes)';
SELECT (SELECT COUNT(*) FROM sf_page_node WHERE ownr IS NOT NULL) AS with_owner,
       (SELECT COUNT(*) FROM sf_page_node n JOIN sf_users u ON u.id = n.ownr) AS owner_exists;
GO
PRINT '== Stored procedures (stock Sitefinity ships none - anything here is site-specific)';
SELECT name FROM sys.procedures ORDER BY name;
GO
PRINT '== Largest tables';
SELECT TOP 40 t.name, p.rows FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) ORDER BY p.rows DESC;
GO
