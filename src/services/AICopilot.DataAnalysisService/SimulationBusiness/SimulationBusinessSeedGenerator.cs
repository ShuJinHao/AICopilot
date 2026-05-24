using System.Text;

namespace AICopilot.DataAnalysisService.SimulationBusiness;

public enum SimulationBusinessProfile
{
    Small = 1,
    Medium = 2,
    Large = 3
}

public sealed record SimulationBusinessTableCount(
    string TableName,
    int RowCount,
    string BusinessDomain);

public sealed record SimulationBusinessSeedPlan(
    string DatabaseName,
    SimulationBusinessProfile Profile,
    int Seed,
    IReadOnlyList<SimulationBusinessTableCount> TableCounts,
    string SqlScript);

public interface ISimulationBusinessSeedGenerator
{
    SimulationBusinessSeedPlan CreatePlan(
        SimulationBusinessProfile profile = SimulationBusinessProfile.Medium,
        int seed = 20260519);
}

public sealed class SimulationBusinessSeedGenerator : ISimulationBusinessSeedGenerator
{
    public const string DatabaseName = "aicopilot_sim_business";

    public SimulationBusinessSeedPlan CreatePlan(
        SimulationBusinessProfile profile = SimulationBusinessProfile.Medium,
        int seed = 20260519)
    {
        var counts = CreateCounts(profile);
        return new SimulationBusinessSeedPlan(
            DatabaseName,
            profile,
            seed,
            counts,
            BuildPostgreSqlSeedScript(profile, seed, counts));
    }

    private static IReadOnlyList<SimulationBusinessTableCount> CreateCounts(SimulationBusinessProfile profile)
    {
        var factor = profile switch
        {
            SimulationBusinessProfile.Small => 0.1,
            SimulationBusinessProfile.Medium => 1.0,
            SimulationBusinessProfile.Large => 5.0,
            _ => 1.0
        };

        return
        [
            Count("employees", 300, "员工与组织", factor),
            Count("attendance", 18000, "员工与组织", factor),
            Count("production_devices", 80, "生产设备", factor),
            Count("production_records", 30000, "生产设备", factor),
            Count("device_events", 50000, "生产设备", factor),
            Count("quality_inspections", 20000, "质量检验", factor),
            Count("inventory_movements", 30000, "库存采购", factor),
            Count("purchase_orders", 4000, "库存采购", factor),
            Count("sales_orders", 3000, "销售交付", factor),
            Count("delivery_records", 5000, "销售交付", factor),
            Count("customer_complaints", 600, "质量检验", factor)
        ];
    }

    private static SimulationBusinessTableCount Count(
        string tableName,
        int mediumRows,
        string businessDomain,
        double factor)
    {
        return new SimulationBusinessTableCount(
            tableName,
            Math.Max(1, (int)Math.Round(mediumRows * factor)),
            businessDomain);
    }

    private static string BuildPostgreSqlSeedScript(
        SimulationBusinessProfile profile,
        int seed,
        IReadOnlyList<SimulationBusinessTableCount> counts)
    {
        var rowCount = counts.ToDictionary(item => item.TableName, item => item.RowCount, StringComparer.Ordinal);
        var sql = new StringBuilder();
        sql.AppendLine("-- AICopilot SimulationBusiness seed");
        sql.AppendLine($"-- database: {DatabaseName}; profile: {profile}; seed: {seed}");
        sql.AppendLine("SET search_path TO public;");
        sql.AppendLine();
        sql.AppendLine("CREATE TABLE IF NOT EXISTS employees (employee_id integer PRIMARY KEY, employee_no text NOT NULL, employee_name text NOT NULL, department text NOT NULL, position_name text NOT NULL, hire_date date NOT NULL, employment_status text NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS attendance (attendance_id bigint PRIMARY KEY, employee_id integer NOT NULL, attendance_date date NOT NULL, shift_code text NOT NULL, work_hours numeric(6,2) NOT NULL, overtime_hours numeric(6,2) NOT NULL, leave_type text);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS production_devices (device_id integer PRIMARY KEY, device_code text NOT NULL, device_name text NOT NULL, workshop text NOT NULL, line_code text NOT NULL, device_status text NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS production_records (record_id bigint PRIMARY KEY, device_id integer NOT NULL, production_date date NOT NULL, product_code text NOT NULL, planned_qty integer NOT NULL, actual_qty integer NOT NULL, scrap_qty integer NOT NULL, shift_code text NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS device_events (event_id bigint PRIMARY KEY, device_id integer NOT NULL, event_time timestamp NOT NULL, event_type text NOT NULL, severity text NOT NULL, duration_minutes integer NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS quality_inspections (inspection_id bigint PRIMARY KEY, production_record_id bigint NOT NULL, inspection_time timestamp NOT NULL, product_code text NOT NULL, sample_qty integer NOT NULL, defect_qty integer NOT NULL, defect_type text NOT NULL, result text NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS inventory_movements (movement_id bigint PRIMARY KEY, material_code text NOT NULL, warehouse_code text NOT NULL, movement_time timestamp NOT NULL, movement_type text NOT NULL, quantity numeric(12,2) NOT NULL, unit text NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS purchase_orders (purchase_order_id bigint PRIMARY KEY, supplier_code text NOT NULL, material_code text NOT NULL, order_date date NOT NULL, promised_date date NOT NULL, quantity numeric(12,2) NOT NULL, received_quantity numeric(12,2) NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS sales_orders (sales_order_id bigint PRIMARY KEY, customer_code text NOT NULL, order_date date NOT NULL, promised_date date NOT NULL, product_code text NOT NULL, order_qty integer NOT NULL, delivered_qty integer NOT NULL, order_status text NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS delivery_records (delivery_id bigint PRIMARY KEY, sales_order_id bigint NOT NULL, delivery_date date NOT NULL, delivered_qty integer NOT NULL, carrier_code text NOT NULL, delivery_status text NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS customer_complaints (complaint_id bigint PRIMARY KEY, sales_order_id bigint NOT NULL, complaint_date date NOT NULL, complaint_type text NOT NULL, severity text NOT NULL, resolution_status text NOT NULL);");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO employees SELECT i, 'E' || lpad(i::text, 4, '0'), '员工' || i, CASE i % 5 WHEN 0 THEN '生产部' WHEN 1 THEN '质量部' WHEN 2 THEN '设备部' WHEN 3 THEN '仓储部' ELSE '销售部' END, CASE i % 4 WHEN 0 THEN '班组长' WHEN 1 THEN '操作员' WHEN 2 THEN '工程师' ELSE '专员' END, date '2020-01-01' + (i % 1800), CASE WHEN i % 29 = 0 THEN '离职' ELSE '在职' END FROM generate_series(1, " + rowCount["employees"] + ") AS s(i) ON CONFLICT (employee_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO attendance SELECT i, ((i - 1) % " + rowCount["employees"] + ") + 1, date '2026-01-01' + (i % 180), CASE i % 3 WHEN 0 THEN 'A' WHEN 1 THEN 'B' ELSE 'C' END, 8 + (i % 3) * 0.5, i % 4, CASE WHEN i % 97 = 0 THEN '婚假' WHEN i % 41 = 0 THEN '病假' ELSE NULL END FROM generate_series(1, " + rowCount["attendance"] + ") AS s(i) ON CONFLICT (attendance_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO production_devices SELECT i, 'DEV-' || lpad(i::text, 3, '0'), '生产设备' || i, '车间' || ((i - 1) % 4 + 1), 'LINE-' || ((i - 1) % 12 + 1), CASE WHEN i % 17 = 0 THEN '维护' WHEN i % 23 = 0 THEN '停机' ELSE '运行' END FROM generate_series(1, " + rowCount["production_devices"] + ") AS s(i) ON CONFLICT (device_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO production_records SELECT i, ((i - 1) % " + rowCount["production_devices"] + ") + 1, date '2026-01-01' + (i % 180), 'P' || lpad(((i - 1) % 120 + 1)::text, 4, '0'), 900 + (i % 300), 860 + (i % 320), i % 18, CASE i % 3 WHEN 0 THEN 'A' WHEN 1 THEN 'B' ELSE 'C' END FROM generate_series(1, " + rowCount["production_records"] + ") AS s(i) ON CONFLICT (record_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO device_events SELECT i, ((i - 1) % " + rowCount["production_devices"] + ") + 1, timestamp '2026-01-01 00:00:00' + (i || ' minutes')::interval, CASE i % 5 WHEN 0 THEN '停机' WHEN 1 THEN '报警' WHEN 2 THEN '保养' WHEN 3 THEN '换型' ELSE '恢复' END, CASE i % 4 WHEN 0 THEN 'High' WHEN 1 THEN 'Medium' ELSE 'Low' END, (i % 120) + 1 FROM generate_series(1, " + rowCount["device_events"] + ") AS s(i) ON CONFLICT (event_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO quality_inspections SELECT i, ((i - 1) % " + rowCount["production_records"] + ") + 1, timestamp '2026-01-01 00:00:00' + (i || ' minutes')::interval, 'P' || lpad(((i - 1) % 120 + 1)::text, 4, '0'), 50 + (i % 30), i % 7, CASE i % 5 WHEN 0 THEN '尺寸' WHEN 1 THEN '外观' WHEN 2 THEN '性能' WHEN 3 THEN '包装' ELSE '无' END, CASE WHEN i % 11 = 0 THEN 'Fail' ELSE 'Pass' END FROM generate_series(1, " + rowCount["quality_inspections"] + ") AS s(i) ON CONFLICT (inspection_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO inventory_movements SELECT i, 'M' || lpad(((i - 1) % 300 + 1)::text, 4, '0'), 'WH-' || ((i - 1) % 8 + 1), timestamp '2026-01-01 00:00:00' + (i || ' minutes')::interval, CASE i % 4 WHEN 0 THEN '入库' WHEN 1 THEN '出库' WHEN 2 THEN '调拨' ELSE '盘点' END, 10 + (i % 500), 'pcs' FROM generate_series(1, " + rowCount["inventory_movements"] + ") AS s(i) ON CONFLICT (movement_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO purchase_orders SELECT i, 'SUP-' || ((i - 1) % 80 + 1), 'M' || lpad(((i - 1) % 300 + 1)::text, 4, '0'), date '2026-01-01' + (i % 180), date '2026-01-01' + (i % 180) + 14, 200 + (i % 1000), 180 + (i % 900) FROM generate_series(1, " + rowCount["purchase_orders"] + ") AS s(i) ON CONFLICT (purchase_order_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO sales_orders SELECT i, 'CUS-' || ((i - 1) % 220 + 1), date '2026-01-01' + (i % 180), date '2026-01-01' + (i % 180) + 21, 'P' || lpad(((i - 1) % 120 + 1)::text, 4, '0'), 100 + (i % 700), 80 + (i % 690), CASE WHEN i % 13 = 0 THEN '延期' WHEN i % 7 = 0 THEN '部分交付' ELSE '正常' END FROM generate_series(1, " + rowCount["sales_orders"] + ") AS s(i) ON CONFLICT (sales_order_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO delivery_records SELECT i, ((i - 1) % " + rowCount["sales_orders"] + ") + 1, date '2026-01-01' + (i % 200), 20 + (i % 200), 'CAR-' || ((i - 1) % 20 + 1), CASE WHEN i % 19 = 0 THEN '异常' ELSE '完成' END FROM generate_series(1, " + rowCount["delivery_records"] + ") AS s(i) ON CONFLICT (delivery_id) DO NOTHING;");
        sql.AppendLine("INSERT INTO customer_complaints SELECT i, ((i - 1) % " + rowCount["sales_orders"] + ") + 1, date '2026-01-01' + (i % 200), CASE i % 4 WHEN 0 THEN '质量' WHEN 1 THEN '交付' WHEN 2 THEN '包装' ELSE '服务' END, CASE i % 3 WHEN 0 THEN 'High' WHEN 1 THEN 'Medium' ELSE 'Low' END, CASE WHEN i % 5 = 0 THEN '处理中' ELSE '已关闭' END FROM generate_series(1, " + rowCount["customer_complaints"] + ") AS s(i) ON CONFLICT (complaint_id) DO NOTHING;");
        return sql.ToString();
    }
}
