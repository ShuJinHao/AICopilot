using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerCloudSimulationSeeder
{
    private const string CloudSimConnectionName = "cloud-device-semantic-sim";
    private const string SemanticDatabaseName = "DeviceSemanticReadonly";
    private const string SemanticDatabaseDescription = "本机开发环境 Cloud 只读模拟数据源，包含设备状态、设备日志、产能和生产记录视图。";

    public static async Task EnsureSourceAsync(
        IConfiguration configuration,
        string environmentName,
        DataAnalysisDbContext dataAnalysisDbContext,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(CloudSimConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (!IsSimulationSeedAllowed(configuration, environmentName))
        {
            return;
        }

        await EnsureBusinessDatabaseAsync(dataAnalysisDbContext, connectionString, cancellationToken);
        await EnsureSimulationSchemaAsync(connectionString, cancellationToken);
    }

    private static bool IsSimulationSeedAllowed(IConfiguration configuration, string environmentName)
    {
        var mode = configuration["CloudReadonly:Mode"];
        if (!string.Equals(mode, "Simulation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CloudReadonly:Mode=Simulation is only allowed in Development. Production deployments must use Real Cloud AiRead and must not seed simulation data sources.");
        }

        var enabled = configuration["CloudReadonly:Simulation:Enabled"];
        return bool.TryParse(enabled, out var parsed) && parsed;
    }

    private static async Task EnsureBusinessDatabaseAsync(
        DataAnalysisDbContext dataAnalysisDbContext,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var database = await dataAnalysisDbContext.BusinessDatabases
            .SingleOrDefaultAsync(item => item.Name == SemanticDatabaseName, cancellationToken);

        if (database is null)
        {
            dataAnalysisDbContext.BusinessDatabases.Add(new BusinessDatabase(
                SemanticDatabaseName,
                SemanticDatabaseDescription,
                connectionString,
                DbProviderType.PostgreSql,
                isReadOnly: true,
                externalSystemType: BusinessDataExternalSystemType.SimulationBusiness,
                readOnlyCredentialVerified: true,
                isEnabled: true,
                category: "CloudReadonly",
                tags: ["simulation", "cloud-readonly", "semantic"],
                ownerDepartment: "AICopilot",
                businessDomain: "Manufacturing",
                sensitivityLevel: "Internal",
                defaultQueryLimit: 200,
                maxQueryLimit: 1000,
                isSelectableInChat: true,
                isSelectableInAgent: true));
        }
        else
        {
            database.UpdateInfo(SemanticDatabaseName, SemanticDatabaseDescription);
            database.UpdateConnection(connectionString, DbProviderType.PostgreSql);
            database.UpdateSettings(
                isEnabled: true,
                isReadOnly: true,
                externalSystemType: BusinessDataExternalSystemType.SimulationBusiness,
                readOnlyCredentialVerified: true);
            database.UpdateGovernance(
                category: "CloudReadonly",
                tags: ["simulation", "cloud-readonly", "semantic"],
                ownerDepartment: "AICopilot",
                businessDomain: "Manufacturing",
                sensitivityLevel: "Internal",
                defaultQueryLimit: 200,
                maxQueryLimit: 1000,
                isSelectableInChat: true,
                isSelectableInAgent: true);
        }

        await dataAnalysisDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureSimulationSchemaAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS devices (
                id text PRIMARY KEY,
                client_code text NOT NULL UNIQUE,
                device_name text NOT NULL,
                process_code text NULL
            );

            CREATE TABLE IF NOT EXISTS device_status_snapshots (
                device_id text PRIMARY KEY REFERENCES devices(id) ON DELETE CASCADE,
                status text NOT NULL,
                line_name text NOT NULL,
                updated_at timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS device_logs (
                id bigint PRIMARY KEY,
                device_id text NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
                level text NOT NULL,
                message text NOT NULL,
                source text NOT NULL,
                log_time timestamptz NOT NULL,
                received_at timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS recipes (
                id text PRIMARY KEY,
                recipe_name text NOT NULL,
                device_id text NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
                process_name text NOT NULL,
                version text NOT NULL,
                is_active boolean NOT NULL,
                updated_at timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS capacity_records (
                id text PRIMARY KEY,
                device_id text NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
                process_name text NOT NULL,
                shift_date date NOT NULL,
                output_qty integer NOT NULL,
                qualified_qty integer NOT NULL,
                occurred_at timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS production_records (
                id text PRIMARY KEY,
                device_id text NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
                process_name text NOT NULL,
                barcode text NOT NULL,
                station_name text NOT NULL,
                result text NOT NULL,
                occurred_at timestamptz NOT NULL
            );

            INSERT INTO devices (id, client_code, device_name, process_code) VALUES
                ('DEV-ID-001', 'DEV-001', 'Cutter A', 'PROC-CUT'),
                ('DEV-ID-002', 'DEV-002', 'Welder B', 'PROC-WELD'),
                ('DEV-ID-003', 'DEV-003', 'Assembler C', 'PROC-ASM'),
                ('DEV-ID-004', 'DEV-004', 'Painter D', 'PROC-PAINT'),
                ('DEV-ID-005', 'DEV-005', 'Inspector E', 'PROC-QC')
            ON CONFLICT (id) DO UPDATE
            SET client_code = EXCLUDED.client_code,
                device_name = EXCLUDED.device_name,
                process_code = EXCLUDED.process_code;

            INSERT INTO device_status_snapshots (device_id, status, line_name, updated_at) VALUES
                ('DEV-ID-001', 'Running', 'LINE-A', '2026-04-21T08:30:00Z'),
                ('DEV-ID-002', 'Stopped', 'LINE-B', '2026-04-21T07:00:00Z'),
                ('DEV-ID-003', 'Idle', 'LINE-A', '2026-04-21T06:45:00Z'),
                ('DEV-ID-004', 'Running', 'LINE-C', '2026-04-21T08:10:00Z'),
                ('DEV-ID-005', 'Maintenance', 'LINE-B', '2026-04-21T05:55:00Z')
            ON CONFLICT (device_id) DO UPDATE
            SET status = EXCLUDED.status,
                line_name = EXCLUDED.line_name,
                updated_at = EXCLUDED.updated_at;

            INSERT INTO device_logs (id, device_id, level, message, source, log_time, received_at) VALUES
                (1, 'DEV-ID-001', 'Info', 'Start completed', 'Cloud', '2026-04-20T08:00:00Z', '2026-04-20T08:00:05Z'),
                (2, 'DEV-ID-001', 'Error', 'Motor overload', 'Cloud', '2026-04-20T10:00:00Z', '2026-04-20T10:00:03Z'),
                (3, 'DEV-ID-001', 'Warn', 'Temperature high', 'Cloud', '2026-04-21T09:15:00Z', '2026-04-21T09:15:02Z'),
                (4, 'DEV-ID-002', 'Error', 'Emergency stop', 'Edge', '2026-04-20T11:00:00Z', '2026-04-20T11:00:04Z'),
                (5, 'DEV-ID-003', 'Info', 'Idle heartbeat', 'Cloud', '2026-04-21T06:50:00Z', '2026-04-21T06:50:01Z'),
                (6, 'DEV-ID-002', 'Warn', 'Shield gas pressure low', 'Cloud', '2026-04-21T08:20:00Z', '2026-04-21T08:20:02Z'),
                (7, 'DEV-ID-004', 'Error', 'Spray nozzle blocked', 'Cloud', '2026-04-21T11:10:00Z', '2026-04-21T11:10:03Z'),
                (8, 'DEV-ID-005', 'Warn', 'Sensor calibration due', 'Cloud', '2026-04-21T12:00:00Z', '2026-04-21T12:00:05Z'),
                (9, 'DEV-ID-004', 'Info', 'Paint circulation stable', 'Edge', '2026-04-21T07:15:00Z', '2026-04-21T07:15:03Z')
            ON CONFLICT (id) DO UPDATE
            SET device_id = EXCLUDED.device_id,
                level = EXCLUDED.level,
                message = EXCLUDED.message,
                source = EXCLUDED.source,
                log_time = EXCLUDED.log_time,
                received_at = EXCLUDED.received_at;

            INSERT INTO recipes (id, recipe_name, device_id, process_name, version, is_active, updated_at) VALUES
                ('RCP-001-V1', 'Recipe-Cut-01', 'DEV-ID-001', 'Cutting', 'V1.0', false, '2026-04-18T08:00:00Z'),
                ('RCP-001-V2', 'Recipe-Cut-01', 'DEV-ID-001', 'Cutting', 'V2.0', true, '2026-04-21T08:00:00Z'),
                ('RCP-002-V1', 'Recipe-Weld-01', 'DEV-ID-002', 'Welding', 'V1.0', false, '2026-04-20T12:00:00Z'),
                ('RCP-002-V2', 'Recipe-Weld-01', 'DEV-ID-002', 'Welding', 'V2.0', true, '2026-04-21T10:00:00Z'),
                ('RCP-003-V1', 'Recipe-Assemble-01', 'DEV-ID-003', 'Assembly', 'V1.0', true, '2026-04-20T14:00:00Z'),
                ('RCP-004-V1', 'Recipe-Paint-01', 'DEV-ID-004', 'Painting', 'V1.0', true, '2026-04-21T09:30:00Z')
            ON CONFLICT (id) DO UPDATE
            SET recipe_name = EXCLUDED.recipe_name,
                device_id = EXCLUDED.device_id,
                process_name = EXCLUDED.process_name,
                version = EXCLUDED.version,
                is_active = EXCLUDED.is_active,
                updated_at = EXCLUDED.updated_at;

            INSERT INTO capacity_records (id, device_id, process_name, shift_date, output_qty, qualified_qty, occurred_at) VALUES
                ('CAP-001', 'DEV-ID-001', 'Cutting', '2026-04-20', 118, 116, '2026-04-20T20:00:00Z'),
                ('CAP-002', 'DEV-ID-001', 'Cutting', '2026-04-21', 126, 123, '2026-04-21T20:00:00Z'),
                ('CAP-003', 'DEV-ID-002', 'Welding', '2026-04-21', 84, 80, '2026-04-21T20:30:00Z'),
                ('CAP-004', 'DEV-ID-003', 'Assembly', '2026-04-21', 92, 91, '2026-04-21T21:00:00Z'),
                ('CAP-005', 'DEV-ID-002', 'Welding', '2026-04-20', 78, 75, '2026-04-20T20:30:00Z'),
                ('CAP-006', 'DEV-ID-004', 'Painting', '2026-04-20', 73, 72, '2026-04-20T19:30:00Z'),
                ('CAP-007', 'DEV-ID-004', 'Painting', '2026-04-21', 79, 77, '2026-04-21T19:30:00Z'),
                ('CAP-008', 'DEV-ID-005', 'QualityCheck', '2026-04-21', 121, 118, '2026-04-21T22:00:00Z')
            ON CONFLICT (id) DO UPDATE
            SET device_id = EXCLUDED.device_id,
                process_name = EXCLUDED.process_name,
                shift_date = EXCLUDED.shift_date,
                output_qty = EXCLUDED.output_qty,
                qualified_qty = EXCLUDED.qualified_qty,
                occurred_at = EXCLUDED.occurred_at;

            INSERT INTO production_records (id, device_id, process_name, barcode, station_name, result, occurred_at) VALUES
                ('PD-001', 'DEV-ID-001', 'Cutting', 'CELL-0001', 'Station-A', 'Pass', '2026-04-21T08:30:00Z'),
                ('PD-002', 'DEV-ID-001', 'Cutting', 'CELL-0002', 'Station-A', 'Fail', '2026-04-21T08:45:00Z'),
                ('PD-003', 'DEV-ID-002', 'Welding', 'CELL-0003', 'Station-B', 'Pass', '2026-04-21T09:10:00Z'),
                ('PD-004', 'DEV-ID-003', 'Assembly', 'CELL-0004', 'Station-C', 'Pass', '2026-04-21T09:30:00Z'),
                ('PD-005', 'DEV-ID-002', 'Welding', 'CELL-0005', 'Station-B', 'Fail', '2026-04-21T09:22:00Z'),
                ('PD-006', 'DEV-ID-004', 'Painting', 'CELL-0006', 'Station-D', 'Pass', '2026-04-21T10:05:00Z'),
                ('PD-007', 'DEV-ID-005', 'QualityCheck', 'CELL-0007', 'Station-Q', 'Fail', '2026-04-21T10:30:00Z'),
                ('PD-008', 'DEV-ID-005', 'QualityCheck', 'CELL-0008', 'Station-Q', 'Pass', '2026-04-21T10:42:00Z')
            ON CONFLICT (id) DO UPDATE
            SET device_id = EXCLUDED.device_id,
                process_name = EXCLUDED.process_name,
                barcode = EXCLUDED.barcode,
                station_name = EXCLUDED.station_name,
                result = EXCLUDED.result,
                occurred_at = EXCLUDED.occurred_at;

            CREATE OR REPLACE VIEW device_master_cloud_sim_view AS
            SELECT
                d.id AS device_id,
                d.client_code AS device_code,
                d.device_name,
                s.status,
                s.line_name,
                s.updated_at
            FROM devices d
            INNER JOIN device_status_snapshots s ON s.device_id = d.id;

            CREATE OR REPLACE VIEW device_log_cloud_sim_view AS
            SELECT
                l.id AS log_id,
                d.id AS device_id,
                d.client_code AS device_code,
                l.level AS log_level,
                l.message AS log_message,
                l.source AS log_source,
                l.log_time AS occurred_at
            FROM device_logs l
            INNER JOIN devices d ON d.id = l.device_id;

            CREATE OR REPLACE VIEW recipe_cloud_sim_view AS
            SELECT
                r.id AS recipe_id,
                r.recipe_name,
                d.id AS device_id,
                d.client_code AS device_code,
                r.process_name,
                r.version,
                r.is_active,
                r.updated_at
            FROM recipes r
            INNER JOIN devices d ON d.id = r.device_id;

            CREATE OR REPLACE VIEW capacity_cloud_sim_view AS
            SELECT
                c.id AS record_id,
                d.id AS device_id,
                d.client_code AS device_code,
                c.process_name,
                c.shift_date,
                c.output_qty,
                c.qualified_qty,
                c.occurred_at
            FROM capacity_records c
            INNER JOIN devices d ON d.id = c.device_id;

            CREATE OR REPLACE VIEW production_data_cloud_sim_view AS
            SELECT
                p.id AS record_id,
                d.id AS device_id,
                d.client_code AS device_code,
                p.process_name,
                p.barcode,
                p.station_name,
                p.result,
                p.occurred_at
            FROM production_records p
            INNER JOIN devices d ON d.id = p.device_id;
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
