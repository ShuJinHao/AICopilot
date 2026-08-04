using System.Data;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace AICopilot.EntityFrameworkCore;

public enum AiGatewayProductionUpgradeState
{
    Fresh,
    ProductionBaseline,
    Current
}

public sealed record AiGatewayProductionUpgradeInspection(
    AiGatewayProductionUpgradeState State,
    string HistorySha256,
    int HistoryLineCount,
    string SchemaSha256,
    int SchemaLineCount);

public static class AiGatewayProductionUpgradePreflight
{
    private const string HistoryTable = "__EFMigrationsHistory_AiGateway";

    public static async Task<AiGatewayProductionUpgradeInspection> InspectAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await InspectAsync(connection, cancellationToken);
    }

    public static async Task<AiGatewayProductionUpgradeInspection> InspectAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "AiGateway production migration preflight requires an open PostgreSQL connection.");
        }

        var historyExists = await ReadBooleanAsync(
            connection,
            "SELECT to_regclass('aigateway.\"__EFMigrationsHistory_AiGateway\"') IS NOT NULL;",
            cancellationToken);
        var schemaObjectCount = await ReadInt32Async(
            connection,
            """
            SELECT (
                (SELECT count(*)
                 FROM pg_class AS object_class
                 JOIN pg_namespace AS object_namespace
                   ON object_namespace.oid = object_class.relnamespace
                 WHERE object_namespace.nspname = 'aigateway') +
                (SELECT count(*)
                 FROM pg_proc AS object_function
                 JOIN pg_namespace AS object_namespace
                   ON object_namespace.oid = object_function.pronamespace
                 WHERE object_namespace.nspname = 'aigateway') +
                (SELECT count(*)
                 FROM pg_type AS object_type
                 JOIN pg_namespace AS object_namespace
                   ON object_namespace.oid = object_type.typnamespace
                 WHERE object_namespace.nspname = 'aigateway')
            )::integer;
            """,
            cancellationToken);
        var relevantDefaultAclCount = await ReadInt32Async(
            connection,
            """
            SELECT count(*)::integer
            FROM pg_default_acl AS default_acl
            LEFT JOIN pg_namespace AS default_namespace
              ON default_namespace.oid = default_acl.defaclnamespace
            WHERE default_acl.defaclrole = (
                      SELECT role_state.oid
                      FROM pg_roles AS role_state
                      WHERE role_state.rolname = current_user)
              AND (default_acl.defaclnamespace = 0 OR
                   default_namespace.nspname = 'aigateway');
            """,
            cancellationToken);
        var freshSchemaSecurityIsExpected = await ReadBooleanAsync(
            connection,
            """
            SELECT NOT EXISTS (
                       SELECT 1
                       FROM pg_namespace AS schema_state
                       WHERE schema_state.nspname = 'aigateway')
                   OR EXISTS (
                       SELECT 1
                       FROM pg_namespace AS schema_state
                       WHERE schema_state.nspname = 'aigateway'
                         AND pg_get_userbyid(schema_state.nspowner) = current_user
                         AND (
                             SELECT count(*)
                             FROM aclexplode(coalesce(
                                 schema_state.nspacl,
                                 acldefault('n'::"char", schema_state.nspowner))) AS acl_state
                         ) = 2
                         AND NOT EXISTS (
                             SELECT 1
                             FROM aclexplode(coalesce(
                                 schema_state.nspacl,
                                 acldefault('n'::"char", schema_state.nspowner))) AS acl_state
                             WHERE acl_state.grantor <> schema_state.nspowner
                                OR acl_state.grantee <> schema_state.nspowner
                                OR acl_state.privilege_type NOT IN ('CREATE', 'USAGE')
                                OR acl_state.is_grantable));
            """,
            cancellationToken);

        if (!historyExists &&
            schemaObjectCount == 0 &&
            relevantDefaultAclCount == 0 &&
            freshSchemaSecurityIsExpected)
        {
            return new AiGatewayProductionUpgradeInspection(
                AiGatewayProductionUpgradeState.Fresh,
                Sha256([]),
                0,
                Sha256([]),
                0);
        }

        var historyLines = historyExists
            ? await ReadLinesAsync(
                connection,
                """
                SELECT "MigrationId" || '|' || "ProductVersion"
                FROM aigateway."__EFMigrationsHistory_AiGateway"
                ORDER BY "MigrationId";
                """,
                cancellationToken)
            : [];
        var schemaLines = await ReadLinesAsync(
            connection,
            """
            SELECT 'schema-security|' || schema_state.nspname || '|' ||
                   pg_get_userbyid(schema_state.nspowner)
            FROM pg_namespace AS schema_state
            WHERE schema_state.nspname = 'aigateway';

            SELECT 'schema-acl|' || schema_state.nspname || '|' ||
                   pg_get_userbyid(acl_state.grantor) || '|' ||
                   CASE WHEN acl_state.grantee = 0
                        THEN 'PUBLIC'
                        ELSE pg_get_userbyid(acl_state.grantee)
                   END || '|' ||
                   acl_state.privilege_type || '|' || acl_state.is_grantable::text
            FROM pg_namespace AS schema_state
            CROSS JOIN LATERAL aclexplode(coalesce(
                schema_state.nspacl,
                acldefault('n'::"char", schema_state.nspowner))) AS acl_state
            WHERE schema_state.nspname = 'aigateway'
            ORDER BY pg_get_userbyid(acl_state.grantor),
                     CASE WHEN acl_state.grantee = 0
                          THEN 'PUBLIC'
                          ELSE pg_get_userbyid(acl_state.grantee)
                     END,
                     acl_state.privilege_type,
                     acl_state.is_grantable;

            SELECT 'default-acl|' || pg_get_userbyid(default_acl.defaclrole) || '|' ||
                   CASE WHEN default_acl.defaclnamespace = 0
                        THEN '*'
                        ELSE default_namespace.nspname
                   END || '|' ||
                   default_acl.defaclobjtype::text || '|' ||
                   pg_get_userbyid(acl_state.grantor) || '|' ||
                   CASE WHEN acl_state.grantee = 0
                        THEN 'PUBLIC'
                        ELSE pg_get_userbyid(acl_state.grantee)
                   END || '|' ||
                   acl_state.privilege_type || '|' || acl_state.is_grantable::text
            FROM pg_default_acl AS default_acl
            LEFT JOIN pg_namespace AS default_namespace
              ON default_namespace.oid = default_acl.defaclnamespace
            CROSS JOIN LATERAL aclexplode(default_acl.defaclacl) AS acl_state
            WHERE default_acl.defaclrole = (
                      SELECT role_state.oid
                      FROM pg_roles AS role_state
                      WHERE role_state.rolname = current_user)
              AND (default_acl.defaclnamespace = 0 OR
                   default_namespace.nspname = 'aigateway')
            ORDER BY CASE WHEN default_acl.defaclnamespace = 0
                          THEN '*'
                          ELSE default_namespace.nspname
                     END,
                     default_acl.defaclobjtype,
                     pg_get_userbyid(acl_state.grantor),
                     CASE WHEN acl_state.grantee = 0
                          THEN 'PUBLIC'
                          ELSE pg_get_userbyid(acl_state.grantee)
                     END,
                     acl_state.privilege_type,
                     acl_state.is_grantable;

            SELECT 'table|' || table_name
            FROM information_schema.tables
            WHERE table_schema = 'aigateway'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;

            SELECT 'column|' || table_name || '|' || lpad(ordinal_position::text, 4, '0') || '|' ||
                   column_name || '|' || data_type || '|' || udt_name || '|' || is_nullable || '|' ||
                   coalesce(character_maximum_length::text, '') || '|' ||
                   coalesce(numeric_precision::text, '') || '|' || coalesce(numeric_scale::text, '') || '|' ||
                   coalesce(column_default, '') || '|' || coalesce(collation_name, '') || '|' ||
                   is_identity || '|' || coalesce(identity_generation, '') || '|' ||
                   is_generated || '|' || coalesce(generation_expression, '')
            FROM information_schema.columns
            WHERE table_schema = 'aigateway'
            ORDER BY table_name, ordinal_position;

            SELECT 'column-acl|' || relation_state.relname || '|' ||
                   lpad(column_state.attnum::text, 4, '0') || '|' ||
                   column_state.attname || '|' ||
                   pg_get_userbyid(acl_state.grantor) || '|' ||
                   CASE WHEN acl_state.grantee = 0
                        THEN 'PUBLIC'
                        ELSE pg_get_userbyid(acl_state.grantee)
                   END || '|' ||
                   acl_state.privilege_type || '|' || acl_state.is_grantable::text
            FROM pg_attribute AS column_state
            JOIN pg_class AS relation_state
              ON relation_state.oid = column_state.attrelid
            JOIN pg_namespace AS relation_namespace
              ON relation_namespace.oid = relation_state.relnamespace
            CROSS JOIN LATERAL aclexplode(column_state.attacl) AS acl_state
            WHERE relation_namespace.nspname = 'aigateway'
              AND relation_state.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND column_state.attnum > 0
              AND NOT column_state.attisdropped
            ORDER BY relation_state.relname,
                     column_state.attnum,
                     pg_get_userbyid(acl_state.grantor),
                     CASE WHEN acl_state.grantee = 0
                          THEN 'PUBLIC'
                          ELSE pg_get_userbyid(acl_state.grantee)
                     END,
                     acl_state.privilege_type,
                     acl_state.is_grantable;

            SELECT 'index|' || table_class.relname || '|' || index_class.relname || '|' ||
                   index_state.indisunique::text || '|' || index_state.indisprimary::text || '|' ||
                   index_state.indisexclusion::text || '|' || index_state.indimmediate::text || '|' ||
                   index_state.indisclustered::text || '|' || index_state.indisvalid::text || '|' ||
                   index_state.indisready::text || '|' || index_state.indislive::text || '|' ||
                   index_state.indisreplident::text || '|' || pg_get_indexdef(index_class.oid)
            FROM pg_index AS index_state
            JOIN pg_class AS table_class ON table_class.oid = index_state.indrelid
            JOIN pg_class AS index_class ON index_class.oid = index_state.indexrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
            ORDER BY table_class.relname, index_class.relname;

            SELECT 'constraint|' || table_class.relname || '|' || constraint_state.conname || '|' ||
                   constraint_state.contype::text || '|' || constraint_state.condeferrable::text || '|' ||
                   constraint_state.condeferred::text || '|' || constraint_state.convalidated::text || '|' ||
                   pg_get_constraintdef(constraint_state.oid, true)
            FROM pg_constraint AS constraint_state
            JOIN pg_class AS table_class ON table_class.oid = constraint_state.conrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
            ORDER BY table_class.relname, constraint_state.conname;

            SELECT 'table-security|' || table_class.relname || '|' ||
                   table_class.relrowsecurity::text || '|' ||
                   table_class.relforcerowsecurity::text || '|' ||
                   table_class.relreplident::text
            FROM pg_class AS table_class
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
              AND table_class.relkind IN ('r', 'p')
            ORDER BY table_class.relname;

            SELECT 'relation|' || relation_state.relname || '|' ||
                   relation_state.relkind::text || '|' ||
                   relation_state.relpersistence::text || '|' ||
                   relation_state.relispartition::text || '|' ||
                   pg_get_userbyid(relation_state.relowner) || '|' ||
                   coalesce((
                       SELECT string_agg(relation_option.option, ',' ORDER BY relation_option.option)
                       FROM unnest(relation_state.reloptions) AS relation_option(option)), '')
            FROM pg_class AS relation_state
            JOIN pg_namespace AS relation_namespace
              ON relation_namespace.oid = relation_state.relnamespace
            WHERE relation_namespace.nspname = 'aigateway'
            ORDER BY relation_state.relname, relation_state.relkind;

            SELECT 'relation-acl|' || relation_state.relname || '|' ||
                   relation_state.relkind::text || '|' ||
                   pg_get_userbyid(acl_state.grantor) || '|' ||
                   CASE WHEN acl_state.grantee = 0
                        THEN 'PUBLIC'
                        ELSE pg_get_userbyid(acl_state.grantee)
                   END || '|' ||
                   acl_state.privilege_type || '|' || acl_state.is_grantable::text
            FROM pg_class AS relation_state
            JOIN pg_namespace AS relation_namespace
              ON relation_namespace.oid = relation_state.relnamespace
            CROSS JOIN LATERAL aclexplode(coalesce(
                relation_state.relacl,
                acldefault(
                    CASE WHEN relation_state.relkind = 'S'
                         THEN 'S'::"char"
                         ELSE 'r'::"char"
                    END,
                    relation_state.relowner))) AS acl_state
            WHERE relation_namespace.nspname = 'aigateway'
              AND relation_state.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
            ORDER BY relation_state.relname,
                     relation_state.relkind,
                     pg_get_userbyid(acl_state.grantor),
                     CASE WHEN acl_state.grantee = 0
                          THEN 'PUBLIC'
                          ELSE pg_get_userbyid(acl_state.grantee)
                     END,
                     acl_state.privilege_type,
                     acl_state.is_grantable;

            SELECT 'view|' || view_state.relname || '|' ||
                   view_state.relkind::text || '|' ||
                   pg_get_viewdef(view_state.oid, true)
            FROM pg_class AS view_state
            JOIN pg_namespace AS view_namespace
              ON view_namespace.oid = view_state.relnamespace
            WHERE view_namespace.nspname = 'aigateway'
              AND view_state.relkind IN ('v', 'm')
            ORDER BY view_state.relname, view_state.relkind;

            SELECT 'trigger|' || table_class.relname || '|' || trigger_state.tgname || '|' ||
                   trigger_state.tgenabled::text || '|' || pg_get_triggerdef(trigger_state.oid, true)
            FROM pg_trigger AS trigger_state
            JOIN pg_class AS table_class ON table_class.oid = trigger_state.tgrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
              AND NOT trigger_state.tgisinternal
            ORDER BY table_class.relname, trigger_state.tgname;

            SELECT 'function|' || function_namespace.nspname || '|' ||
                   function_state.proname || '|' ||
                   pg_get_function_identity_arguments(function_state.oid) || '|' ||
                   pg_get_userbyid(function_state.proowner) || '|' ||
                   function_state.prokind::text || '|' ||
                   function_state.provolatile::text || '|' ||
                   function_state.proisstrict::text || '|' ||
                   function_state.prosecdef::text || '|' ||
                   function_state.proleakproof::text || '|' ||
                   function_state.proparallel::text || '|' ||
                   coalesce((
                       SELECT string_agg(function_option.option, ',' ORDER BY function_option.option)
                       FROM unnest(function_state.proconfig) AS function_option(option)), '') || '|' ||
                   pg_get_functiondef(function_state.oid)
            FROM pg_proc AS function_state
            JOIN pg_namespace AS function_namespace ON function_namespace.oid = function_state.pronamespace
            WHERE function_namespace.nspname = 'aigateway'
            ORDER BY function_namespace.nspname, function_state.proname,
                     pg_get_function_identity_arguments(function_state.oid);

            SELECT 'function-acl|' || function_state.proname || '|' ||
                   pg_get_function_identity_arguments(function_state.oid) || '|' ||
                   pg_get_userbyid(acl_state.grantor) || '|' ||
                   CASE WHEN acl_state.grantee = 0
                        THEN 'PUBLIC'
                        ELSE pg_get_userbyid(acl_state.grantee)
                   END || '|' ||
                   acl_state.privilege_type || '|' || acl_state.is_grantable::text
            FROM pg_proc AS function_state
            JOIN pg_namespace AS function_namespace
              ON function_namespace.oid = function_state.pronamespace
            CROSS JOIN LATERAL aclexplode(coalesce(
                function_state.proacl,
                acldefault('f'::"char", function_state.proowner))) AS acl_state
            WHERE function_namespace.nspname = 'aigateway'
            ORDER BY function_state.proname,
                     pg_get_function_identity_arguments(function_state.oid),
                     pg_get_userbyid(acl_state.grantor),
                     CASE WHEN acl_state.grantee = 0
                          THEN 'PUBLIC'
                          ELSE pg_get_userbyid(acl_state.grantee)
                     END,
                     acl_state.privilege_type,
                     acl_state.is_grantable;

            SELECT 'type|' || type_state.typname || '|' ||
                   type_state.typtype::text || '|' ||
                   pg_get_userbyid(type_state.typowner) || '|' ||
                   type_state.typcategory::text || '|' ||
                   type_state.typispreferred::text || '|' ||
                   type_state.typnotnull::text || '|' ||
                   coalesce(format_type(nullif(type_state.typbasetype, 0), type_state.typtypmod), '') || '|' ||
                   type_state.typndims::text || '|' ||
                   coalesce(nullif(type_state.typcollation, 0)::regcollation::text, '') || '|' ||
                   coalesce(type_state.typdefault, '') || '|' ||
                   coalesce((
                       SELECT string_agg(
                           enum_state.enumsortorder::text || ':' || enum_state.enumlabel,
                           ',' ORDER BY enum_state.enumsortorder)
                       FROM pg_enum AS enum_state
                       WHERE enum_state.enumtypid = type_state.oid), '') || '|' ||
                   coalesce((
                       SELECT string_agg(
                           domain_constraint.conname || ':' ||
                           pg_get_constraintdef(domain_constraint.oid, true),
                           ',' ORDER BY domain_constraint.conname)
                       FROM pg_constraint AS domain_constraint
                       WHERE domain_constraint.contypid = type_state.oid), '') || '|' ||
                   coalesce((
                       SELECT range_state.rngsubtype::regtype::text || ':' ||
                              coalesce(nullif(range_state.rngcollation, 0)::regcollation::text, '') || ':' ||
                              coalesce(nullif(range_state.rngcanonical, 0)::regproc::text, '') || ':' ||
                              coalesce(nullif(range_state.rngsubdiff, 0)::regproc::text, '') || ':' ||
                              coalesce(nullif(range_state.rngmultitypid, 0)::regtype::text, '')
                       FROM pg_range AS range_state
                       WHERE range_state.rngtypid = type_state.oid), '')
            FROM pg_type AS type_state
            JOIN pg_namespace AS type_namespace
              ON type_namespace.oid = type_state.typnamespace
            WHERE type_namespace.nspname = 'aigateway'
              AND (
                      type_state.typtype IN ('e', 'd', 'r', 'm') OR
                  (type_state.typtype = 'c' AND EXISTS (
                      SELECT 1
                      FROM pg_class AS composite_relation
                      WHERE composite_relation.oid = type_state.typrelid
                        AND composite_relation.relkind = 'c')))
            ORDER BY type_state.typname;

            SELECT 'type-acl|' || type_state.typname || '|' ||
                   type_state.typtype::text || '|' ||
                   pg_get_userbyid(acl_state.grantor) || '|' ||
                   CASE WHEN acl_state.grantee = 0
                        THEN 'PUBLIC'
                        ELSE pg_get_userbyid(acl_state.grantee)
                   END || '|' ||
                   acl_state.privilege_type || '|' || acl_state.is_grantable::text
            FROM pg_type AS type_state
            JOIN pg_namespace AS type_namespace
              ON type_namespace.oid = type_state.typnamespace
            CROSS JOIN LATERAL aclexplode(coalesce(
                type_state.typacl,
                acldefault('T'::"char", type_state.typowner))) AS acl_state
            WHERE type_namespace.nspname = 'aigateway'
              AND (
                  type_state.typtype IN ('e', 'd', 'r', 'm') OR
                  (type_state.typtype = 'c' AND EXISTS (
                      SELECT 1
                      FROM pg_class AS composite_relation
                      WHERE composite_relation.oid = type_state.typrelid
                        AND composite_relation.relkind = 'c')))
            ORDER BY type_state.typname,
                     pg_get_userbyid(acl_state.grantor),
                     CASE WHEN acl_state.grantee = 0
                          THEN 'PUBLIC'
                          ELSE pg_get_userbyid(acl_state.grantee)
                     END,
                     acl_state.privilege_type,
                     acl_state.is_grantable;

            SELECT 'composite-attribute|' || type_state.typname || '|' ||
                   lpad(attribute_state.attnum::text, 4, '0') || '|' ||
                   attribute_state.attname || '|' ||
                   format_type(attribute_state.atttypid, attribute_state.atttypmod) || '|' ||
                   attribute_state.attnotnull::text || '|' ||
                   attribute_state.attidentity::text || '|' ||
                   attribute_state.attgenerated::text
            FROM pg_type AS type_state
            JOIN pg_namespace AS type_namespace
              ON type_namespace.oid = type_state.typnamespace
            JOIN pg_class AS composite_relation
              ON composite_relation.oid = type_state.typrelid
             AND composite_relation.relkind = 'c'
            JOIN pg_attribute AS attribute_state
              ON attribute_state.attrelid = composite_relation.oid
             AND attribute_state.attnum > 0
             AND NOT attribute_state.attisdropped
            WHERE type_namespace.nspname = 'aigateway'
            ORDER BY type_state.typname, attribute_state.attnum;

            SELECT 'policy|' || table_class.relname || '|' || policy_state.polname || '|' ||
                   policy_state.polpermissive::text || '|' || policy_state.polcmd::text || '|' ||
                   coalesce((
                       SELECT string_agg(policy_role.role_name, ',' ORDER BY policy_role.role_name)
                       FROM (
                           SELECT CASE WHEN role_oid = 0
                                       THEN 'public'
                                       ELSE pg_get_userbyid(role_oid)
                                  END AS role_name
                           FROM unnest(policy_state.polroles) AS expanded_role(role_oid)
                       ) AS policy_role), '') || '|' ||
                   coalesce(pg_get_expr(policy_state.polqual, policy_state.polrelid, true), '') || '|' ||
                   coalesce(pg_get_expr(policy_state.polwithcheck, policy_state.polrelid, true), '')
            FROM pg_policy AS policy_state
            JOIN pg_class AS table_class ON table_class.oid = policy_state.polrelid
            JOIN pg_namespace AS table_namespace ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'aigateway'
            ORDER BY table_class.relname, policy_state.polname;

            SELECT 'sequence|' || sequence_name || '|' || data_type || '|' ||
                   start_value || '|' || minimum_value || '|' || maximum_value || '|' ||
                   increment || '|' || cycle_option
            FROM information_schema.sequences
            WHERE sequence_schema = 'aigateway'
            ORDER BY sequence_name;
            """,
            cancellationToken);

        var historySha = Sha256(historyLines);
        var schemaSha = Sha256(schemaLines);
        var productionHistory = ExpectedHistoryLines(includeCurrent: false);
        var currentHistory = ExpectedHistoryLines(includeCurrent: true);

        if (historyLines.SequenceEqual(productionHistory, StringComparer.Ordinal) &&
            historySha == AiGatewayProductionUpgradeContract.ExpectedProductionHistorySha256 &&
            schemaSha == AiGatewayProductionUpgradeContract.ExpectedProductionSchemaSha256)
        {
            return new AiGatewayProductionUpgradeInspection(
                AiGatewayProductionUpgradeState.ProductionBaseline,
                historySha,
                historyLines.Count,
                schemaSha,
                schemaLines.Count);
        }

        if (historyLines.SequenceEqual(currentHistory, StringComparer.Ordinal) &&
            schemaSha == AiGatewayProductionUpgradeContract.ExpectedCurrentSchemaSha256)
        {
            return new AiGatewayProductionUpgradeInspection(
                AiGatewayProductionUpgradeState.Current,
                historySha,
                historyLines.Count,
                schemaSha,
                schemaLines.Count);
        }

        throw new InvalidOperationException(
            "AiGateway production migration preflight rejected an unknown schema/history state. " +
            $"historySha256={historySha} historyLines={historyLines.Count} " +
            $"schemaSha256={schemaSha} schemaLines={schemaLines.Count}. " +
            "Do not infer or insert migration history.");
    }

    private static string[] ExpectedHistoryLines(bool includeCurrent)
    {
        var ids = includeCurrent
            ? AiGatewayProductionUpgradeContract.ProductionMigrationIds
                .Append(AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId)
            : AiGatewayProductionUpgradeContract.ProductionMigrationIds;
        return ids
            .Select(id => $"{id}|{AiGatewayProductionUpgradeContract.EfProductVersion}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<List<string>> ReadLinesAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        do
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(reader.GetString(0));
            }
        }
        while (await reader.NextResultAsync(cancellationToken));
        return lines;
    }

    private static async Task<bool> ReadBooleanAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("AiGateway preflight returned no boolean result."));
    }

    private static async Task<int> ReadInt32Async(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("AiGateway preflight returned no count."));
    }

    private static string Sha256(IReadOnlyCollection<string> lines)
    {
        var canonical = lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
