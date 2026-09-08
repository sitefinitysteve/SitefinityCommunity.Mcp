# Sitefinity 15.4 relationship map (column by column)

Verified against the OpenAccess fluent mappings decompiled from the 15.4.8636 assemblies (`Telerik.Sitefinity.Model.dll`: `PagesFluentMapping`, `ObjectDataFluentMapping`, `FormsFluentMapping`, `DynamicModuleFluentMapping`, `DynamicFluentMapping`, `ModuleBuilderFluentMapping`, `MetadataFluentMapping`, `LibrariesFluentMapping`, `CommonFluentMapping`, `ContentBaseFluentMapping`, `PermissionsFluentMapping`, `MultisiteFluentMapping`, `PublishingFluentMapping`; `Telerik.Sitefinity.dll`: `ContentLinksFluentMapping`) and against a production 15.4 database. "FK" = a real constraint exists; "conv" = convention only, enforced by the ORM at runtime.

Newer Sitefinity releases may add columns; they very rarely rename or remove. Re-verify with `verification-queries.sql` before relying on this on a different version.

## Pages

| From | Column | To | Kind | CLR property |
|---|---|---|---|---|
| sf_page_node | content_id | sf_page_data.content_id | conv | `PageNode.Page` / `PageId` - the AUTHORITATIVE current page data |
| sf_page_node | parent_id | sf_page_node.id | conv | `PageNode.Parent` |
| sf_page_node | root_id | sf_page_node.id | conv | `PageNode.RootNode` (a site's page root; `sf_sites.site_map_root_node_id`) |
| sf_page_node | ownr, last_modified_by | sf_users.id | conv | may point at deleted users |
| sf_page_node | linked_node_id | sf_page_node.id | conv | for `node_type` InnerRedirect / Rewriting |
| sf_page_node_sf_permissions | id / id2 | sf_page_node.id / sf_permissions.id | FK | `PageNode.Permissions` join |
| sf_page_node_attrbutes | id | sf_page_node.id | FK | attribute bag |
| sf_page_node_references | page_node_id | sf_page_node.id | conv | `PageNodeReference` |
| sf_page_node_translation_siblings | id / id2 | sf_page_node.id | FK | multilingual sibling nodes |
| sf_page_data | page_node_id | sf_page_node.id | conv | `PageData.NavigationNode` - reverse nav; can hit stale rows |
| sf_page_data | template_id | sf_page_templates.id | conv | `PageData.Template` |
| sf_page_data | ownr, last_modified_by, locked_by | sf_users.id | conv | |
| sf_page_data | personalization_master_id | sf_page_data.content_id | conv | personalized page variants |
| sf_page_data_sf_language_data | content_id / id | sf_page_data.content_id / sf_language_data.id | FK | |
| sf_pg_dt_pblished_translations | content_id | sf_page_data.content_id | FK | `val` = culture code |
| sf_page_data_attrbutes, sf_page_data_sf_commnt | content_id | sf_page_data.content_id | FK | |
| sf_draft_pages (PageDraft) | page_id | sf_page_data.content_id | conv | `PageDraft.ParentPage` / `ParentId` |
| sf_draft_pages (TemplateDraft) | page_id | sf_page_templates.id | conv | `TemplateDraft.ParentTemplate` |
| sf_draft_pages (FormDraft) | content_id | sf_form_description.content_id | conv | `FormDraft.ParentForm` |
| sf_draft_pages | template_id | sf_page_templates.id | conv | the draft's own template choice (`PageDraft.Template`, `TemplateDraft.DraftTemplate`) |
| sf_draft_pages | ownr | sf_users.id | conv | checkout owner |
| sf_drft_pages_sf_language_data | id / id2 | sf_draft_pages.id / sf_language_data.id | FK | |
| sf_page_templates | prent_template_id | sf_page_templates.id | conv | `PageTemplate.ParentTemplate` (inheritance chain) |
| sf_page_templates | category | sf_taxa.id | conv | template category taxon (`PageTemplates` taxonomy) |
| sf_pg_templates_sf_permissions | id / id2 | sf_page_templates.id / sf_permissions.id | FK | |
| sf_pg_tmpltes_sf_language_data, sf_pg_tmplts_pblshd_trnsltions | id | sf_page_templates.id | FK | |

`sf_page_node.node_type` = enum `NodeType`: Standard 0, Group 1, External 2, InnerRedirect 3, OuterRedirect 4, Rewriting 5.
`sf_page_templates.framework` = enum `PageTemplateFramework`: Hybrid 0, Mvc 1, WebForms 2.

## Controls and properties

| From | Column | To | Kind | CLR property |
|---|---|---|---|---|
| sf_object_data (PageControl) | page_id | sf_page_data.content_id | conv | `PageControl.Page` |
| sf_object_data (PageDraftControl) | page_id | sf_draft_pages.id | conv | `PageDraftControl.Page` |
| sf_object_data (TemplateControl) | page_id | sf_page_templates.id | conv | `TemplateControl.Template` (field `template`, column `page_id`) |
| sf_object_data (TemplateDraftControl) | page_id | sf_draft_pages.id | conv | |
| sf_object_data (FormControl) | content_id | sf_form_description.content_id | conv | `FormControl.Form` / `ContainerId` |
| sf_object_data (FormDraftControl) | id3 | sf_draft_pages.id | conv | `FormDraftControl.Form` / `ContainerId` |
| sf_object_data (ObjectData) | parent_prop_id | sf_control_properties.id | conv | `ObjectData.ParentProperty` - nested complex value / list item |
| sf_object_data | parent_id | sf_object_data.id | conv | `ObjectData.ParentId` (nesting between controls) |
| sf_object_data | sibling_id | sf_object_data.id | conv | `ControlData.SiblingId` - the PREVIOUS control in the placeholder; Empty = head |
| sf_object_data | base_control_id | sf_object_data.id | conv | `ControlData.BaseControlId` - template control this page row overrides |
| sf_object_data | original_control_id | sf_object_data.id | conv | `ControlData.OriginalControlId` - draft control -> live control it mirrors |
| sf_object_data | id2 | sf_presentation_data.id | conv | `ControlData.CurrentPresentation` |
| sf_object_data | personalization_master_id | sf_object_data.id | conv | `ControlData.PersonalizedControls` |
| sf_object_data | ownr | sf_users.id | conv | |
| sf_object_data_sf_permissions | id / id2 | sf_object_data.id / sf_permissions.id | FK | `ControlData.Permissions` |
| sf_control_properties | control_id | sf_object_data.id | conv | `ControlProperty.Control` - Level 1 only |
| sf_control_properties | prnt_prop_id | sf_control_properties.id | conv | `ControlProperty.ParentProperty` - Level 2+ |
| sf_presentation_data (ControlPresentation) | item_id | sf_object_data.id | conv | `ControlPresentation.Control` |
| sf_presentation_data (PropertyPresentation) | item_id | sf_control_properties.id | conv | |
| sf_presentation_data (PagePresentation / PageDraftPresentation / TemplateDraftPresentation) | item_id | sf_page_data.content_id / sf_draft_pages.id | conv | |
| sf_presentation_data (TemplatePresentation) | id4 | sf_page_templates.id | conv | |
| sf_presentation_data (DraftPresentation) | id3 | sf_draft_pages.id | conv | |
| sf_presentation_data (FormPresentation) | id2 | sf_draft_pages.id (FormDraft) | conv | |

Columns per family on `sf_draft_pages`: PageDraft uses `last_control_id2`, `include_script_manger`, `master_page`, `theme`; TemplateDraft uses `last_control_id`, `include_script_manger2`, `master_page2`, `theme2`, `ky`; FormDraft uses `last_control_id3`, `nme`, `content_id`, `is_temp_form`, `rules`, `submit_action*`, `redirect_page_url*`. `published` (FormControl) vs `published2` (FormDraftControl) on `sf_object_data`.

## Generic content (news, events, blogs, lists, content items, comments, forms)

| From | Column | To | Kind | Notes |
|---|---|---|---|---|
| sf_<type> (live row) | original_content_id | sf_<type>.content_id (master row) | conv | `Content.OriginalContentId`; Empty on the master |
| sf_<type> | ownr, last_modified_by | sf_users.id | conv | |
| sf_<type> | default_page_id | sf_page_node.id | conv | canonical detail page |
| sf_<type>_category, sf_<type>_tags (+ any custom `{table}_{field}`) | content_id / val | sf_<type>.content_id (FK) / sf_taxa.id (conv) | mixed | multi-value taxonomy fields, `seq` ordered |
| sf_<type>_sf_permissions | content_id / id | sf_<type>.content_id / sf_permissions.id | FK | |
| sf_<type>_sf_language_data | content_id / id | sf_<type>.content_id / sf_language_data.id | FK | |
| sf_<type>_sf_commnt | content_id / content_id2 | sf_<type>.content_id / sf_commnt.content_id | FK | |
| sf_<abbrev>_pblshd_translations | content_id | sf_<type>.content_id | FK | `val` = culture |
| sf_list_items | parent_id | sf_lists.content_id | conv | 100% resolve on the reference DB |
| sf_blog_posts | parent_id | sf_blogs.content_id | conv | |
| sf_blogs | landing_page_id | sf_page_node.id | conv | |
| sf_events | parent_id | sf_calendars.id | conv | |
| sf_commnt | commented_item_i_d + commented_item_type | any content id | conv | |
| sf_content_relation | subject_id / object_id (+ types, relation_type) | ContentItem.content_id / PageData.content_id | conv | `Contains` = shared content block used on page |
| sf_form_description | library_id | sf_libraries.content_id | conv | upload library for file fields |
| sf_form_description | redirect_node_id | sf_page_node.id | conv | |
| sf_form_description_* junctions | content_id | sf_form_description.content_id | FK | |
| sf_form_entry | user_id | sf_users.id | conv | anonymous entries NULL |
| sf_form_entry | source_site_id | sf_sites.id | conv | |
| sf_forms_frm_* / sf_frms_frm_* / sf_fm_* | id | sf_form_entry.id | FK | vertical extension, one column per form field |

## Module Builder (dynamic) content

| From | Column | To | Kind | Notes |
|---|---|---|---|---|
| <module>_<type> | base_id | sf_dynamic_content.base_id | **conv - FK deliberately filtered out by `OpenAccessConnection.UpgradeDatabaseSchema` on MsSql** | `DynamicContent.Id` maps to `base_id` |
| <module>_<type> | parent_id | <parenttype>.base_id | conv | child types in a hierarchy; mirrored by `sf_dynamic_content.system_parent_id` / `system_parent_type` |
| <module>_<type>_<field> (vowel-stripped) | base_id / val | <module>_<type>.base_id (FK) / sf_taxa.id (conv) | mixed | taxonomy and multi-choice fields |
| sf_dynamic_content (live) | original_content_id | sf_dynamic_content.base_id (master) | conv | |
| sf_dynamic_content | system_parent_id | sf_dynamic_content.base_id | conv | `DynamicContent.SystemParentItem` |
| sf_dynamic_content | ownr, last_modified_by | sf_users.id | conv | |
| sf_dynamic_content | id | (legacy, mostly NULL) | - | never join on it |
| sf_dynmc_cntent_sf_permissions | base_id / id | sf_dynamic_content.base_id / sf_permissions.id | FK | |
| sf_dynmc_cntnt_sf_lnguage_data | base_id / id | sf_dynamic_content.base_id / sf_language_data.id | FK | |
| sf_dynmc_cntnt_pblshd_trnsltns | base_id | sf_dynamic_content.base_id | FK | |
| sf_mb_dynamic_module_type | parent_module_id | sf_mb_dynamic_module.id | conv | |
| sf_mb_dynamic_module_type | parentTypeId | sf_mb_dynamic_module_type.id | conv | type hierarchy |
| sf_mb_dynamic_module_type | pageId | sf_page_node.id | conv | backend page for the type |
| sf_mb_dynamic_module_field | parent_type_id / parent_section_id / classification_id | sf_mb_dynamic_module_type.id / sf_mb_fields_backend_section.id / sf_taxonomies.id | conv | |
| sf_meta_fields | type_id | sf_meta_types.id | conv | `MetaField.Parent`; indexed |
| sf_meta_fields | taxonomy_id | sf_taxonomies.id | conv | Empty when not a taxonomy field |
| sf_meta_fields | id2 | sf_meta_index.id | conv | `MetaField.Index` |
| sf_meta_types | parent_type_id | sf_meta_types.id | conv | |
| sf_meta_attribute (MetaTypeAttribute) | id2 | sf_meta_types.id | conv | flat-mapped by `voa_class` |
| sf_meta_attribute (MetaFieldAttribute) | id2 | sf_meta_fields.id | conv | |
| sf_meta_data_mapping | (module_name, type_name, field_name) -> table_name | - | - | THE name registry |
| dynmc_* | id | sf_dynamic_type_base.id | FK | vertical inheritance |
| sf_dynamic_type_base | original_item_id / original_parent_id | the synced/indexed item | conv | pipe-specific |

`sf_meta_types.database_inheritance` = enum `DatabaseInheritanceType`: flat 0, vertical 1, horizontal 2. Module Builder types are vertical (1).

## Related data, URLs, taxonomies

| From | Column | To | Kind | Notes |
|---|---|---|---|---|
| sf_content_link | parent_item_id (+ parent_item_type, parent_item_provider_name, component_property_name) | master id of any item | conv | RelatedData / RelatedMedia owner side; indexed |
| sf_content_link | child_item_id (+ child_item_type, child_item_provider_name) | master id of any item | conv | indexed, also `(child_item_type, child_item_id)` |
| sf_content_link_attrbutes | id | sf_content_link.id | FK | |
| sf_url_data (PageUrlData) | id2 | sf_page_node.id | conv | `PageNode.Urls` |
| sf_url_data (all content url types) | content_id (+ item_type) | master item id | conv | `Urls` collection on each content type |
| sf_taxa | taxonomy_id | sf_taxonomies.id | conv | indexed |
| sf_taxa | parent_id | sf_taxa.id | conv | hierarchical taxa; indexed |
| sf_taxa | fct_txn_fct_tx_id | sf_taxa.id | conv | facets |
| sf_taxonomies | root_id | sf_taxa.id | conv | |
| sf_taxonomies | ownr | sf_users.id | conv | |
| sf_facet_facets, sf_network_subtaxa, sf_network_supertaxa | id / id2 | sf_taxa.id / sf_taxonomies.id | FK | |
| sf_taxa_attrbutes | id | sf_taxa.id | FK | |
| sf_taxonomy_statistic | taxonomy_id / taxon_id | sf_taxonomies.id / sf_taxa.id | conv | per `data_item_type` counts |
| sf_synonyms | taxon_id | sf_taxa.id | conv | |

## Media and libraries

| From | Column | To | Kind | Notes |
|---|---|---|---|---|
| sf_media_content (live) | original_content_id | sf_media_content.content_id | conv | |
| sf_media_content | parent_id | sf_libraries.content_id | conv | 100% resolve; indexed |
| sf_media_content | folder_id | sf_folders.id | conv | NULL at library root; indexed |
| sf_media_content | file_id | file store id | conv | blob/disk file identity |
| sf_media_file_links | content_id | sf_media_content.content_id | conv | one per item + culture |
| sf_media_file_urls | media_file_link_id | sf_media_file_links.id | conv | current + historical file URLs |
| sf_media_thumbnails | content_id | sf_media_content.content_id | conv | |
| sf_media_file_additional_url | item_id | sf_media_content.content_id | conv | |
| sf_libraries | cover_id | sf_media_content.content_id | conv | |
| sf_libraries | running_task | sf_scheduled_tasks.id | conv | |
| sf_folders | parent_id / root_id | sf_folders.id | conv | |
| sf_chunks | file_id | sf_media_content.file_id | conv | only when binaries live in the DB |
| sf_media_content_category(2,3), _tags(2,3), _test etc. | content_id / val | sf_media_content.content_id (FK) / sf_taxa.id (conv) | mixed | numbered per flat-mapped class |
| sf_lbraries_thumbnail_profiles | content_id | sf_libraries.content_id | FK | |

## Security and multisite

| From | Column | To | Kind | Notes |
|---|---|---|---|---|
| sf_user_link | user_id / role_id | sf_users.id / sf_roles.id | conv | user-role assignment |
| sf_user_link | membership_info | sf_manager_info.id | conv | which membership provider |
| sf_users | manager_info | sf_manager_info.id | conv | |
| sf_user_profile | user_id | sf_users.id | conv | |
| sf_sitefinity_profile, custom profile tables | id | sf_user_profile.id | FK | vertical inheritance |
| sf_permissions | object_id | secured object id (page node, control, media, library, dynamic content, template, taxonomy, security root, form, site ...) | conv | discriminated by `set_name` |
| sf_permissions | principal_id | sf_roles.id or sf_users.id | conv | |
| *_sf_permissions join tables | id or content_id / id2 or id | owner / sf_permissions.id | FK | |
| sf_permissions_inheritance_map | object_id / child_object_id | secured object ids | conv | `child_object_type_name` is a type int |
| sf_security_roots | ky | data provider name | - | provider-level permission roots |
| sf_approval_tracking_record | workflow_item_id | master content id, or sf_page_node.id for pages | conv | |
| sf_approval_tracking_record | user_id | sf_users.id | conv | |
| sf_sites | site_map_root_node_id / home_page_id / front_end_login_page_id | sf_page_node.id | conv | |
| sf_sites | default_frontend_template_id | sf_page_templates.id | conv | |
| sf_site_data_src_lnks | site_id / data_source_id | sf_sites.id / sf_site_data_src.id | conv | `Site.SiteDataSourceLinks` / `SiteDataSourceLink.DataSource` - the mapped table |
| sf_site_data_source_links | site_id (+ provider_name, dataSource_name) | sf_sites.id | conv | legacy name-based shape, still populated alongside the mapped table |
| sf_site_item_links | site_id / item_id (+ item_type) | sf_sites.id / item | conv | only ControlPresentation, FormDescription, PublishingPoint, PageTemplate(+presentations) on the reference DB |
| sf_sites_culture_keys, sf_sites_domain_aliases, sf_sites_sf_permissions | id | sf_sites.id | FK | |

## Versioning

| From | Column | To | Kind | Notes |
|---|---|---|---|---|
| sf_version_chnges | item_id | the versioned item id (`sf_page_data.content_id` for pages; master content id for content; `sf_object_data.id`/presentation ids for widget templates) | conv | indexed |
| sf_version_chnges | serial_info_id | sf_version_serial_info.id | conv | formatter used |
| sf_version_chnges | ownr | sf_users.id | conv | |
| sf_vesion_items | id | the versioned item id | conv | `type_name` says which model type |
| sf_vesion_items | trunk_id | sf_version_trunks.id | conv | |
| sf_vrsn_chngs_sf_vrsn_dpndncy | id / id2 | sf_version_chnges.id / sf_version_dependency.id | FK | |

## Foreign keys on the reference database (323) by shape

- 33 `dynmc_*.id -> sf_dynamic_type_base.id`
- 51 form-entry tables `.id -> sf_form_entry.id`
- 2 profile tables `.id -> sf_user_profile.id`
- ~90 taxonomy / multi-value junctions (`{owner}_{field}` -> owner PK)
- ~60 `*_sf_permissions` joins (two FKs each), `*_sf_commnt` joins (two each), `*_sf_language_data` joins (two each)
- ~20 `*_pblshd_translations` / `*_attrbutes` / `*_translation_siblings` / taxa network tables
- the rest: settings sub-tables for publishing, workflow, newsletters, ecommerce, packaging, sites, security roots

Zero FKs on: `sf_page_node`, `sf_page_data`, `sf_draft_pages`, `sf_object_data`, `sf_control_properties`, `sf_page_templates`, `sf_dynamic_content`, `sf_content_link`, `sf_url_data`, `sf_taxa`, `sf_taxonomies`, `sf_permissions`, `sf_users`, `sf_roles`, `sf_user_link`, `sf_meta_types`, `sf_meta_fields`, `sf_media_content`, `sf_libraries`, `sf_version_chnges`, `sf_vesion_items`, `sf_approval_tracking_record`, `sf_sites`, and every Module Builder per-type table (as the referencing side).

## Non-clustered indexes on the core tables (reference database)

| Table | Indexes |
|---|---|
| sf_page_node | content_id; parent_id; root_id; (parent_id, ordinal) |
| sf_page_data | page_node_id; template_id; personalization_master_id |
| sf_draft_pages | page_id; content_id; nme |
| sf_object_data | page_id; parent_prop_id; sibling_id; base_control_id; content_id; id3; personalization_master_id; collection_index |
| sf_control_properties | control_id; prnt_prop_id; nme |
| sf_page_templates | prent_template_id |
| sf_dynamic_content | (application_name, status, visible); original_content_id; url_name_; (application_name, system_parent_id); system_src_key |
| sf_content_link | parent_item_id; child_item_id; (child_item_type, child_item_id) |
| sf_url_data | url; content_id; id2; (item_type, url) |
| sf_taxa | taxonomy_id; parent_id; status; fct_txn_fct_tx_id |
| sf_permissions | (object_id, principal_id, app_name, set_name); set_name |
| sf_meta_fields | type_id; (app_name, field_name) |
| sf_meta_types | (app_name, class_name, name_space) |
| sf_media_content | parent_id; folder_id; original_content_id; (app_name, status, visible) |
| sf_news_items | app_name; original_content_id |
| sf_users | (user_name, app_name); email; (ext_provider_name, ext_id) |
| sf_user_link | (app_name, user_id, role_id); role_id |
| sf_version_chnges | item_id; (is_published_version, vrsion, last_modified, item_id, culture) |
| sf_site_item_links | (item_id, item_type) |
| sf_form_entry | app_name |
| Module Builder per-type tables | PK only |
