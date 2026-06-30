\set ON_ERROR_STOP on

SELECT
  'readonly_privileges' AS probe,
  current_setting('default_transaction_read_only') = 'on' AS transaction_readonly,
  has_table_privilege(current_user, 'public.devices', 'SELECT') AS devices_select,
  has_table_privilege(current_user, 'public.mfg_processes', 'SELECT') AS mfg_processes_select,
  has_table_privilege(current_user, 'public.device_logs', 'SELECT') AS device_logs_select,
  has_table_privilege(current_user, 'public.hourly_capacity', 'SELECT') AS hourly_capacity_select,
  has_table_privilege(current_user, 'public.pass_station_records', 'SELECT') AS pass_station_records_select,
  NOT has_table_privilege(current_user, 'public.devices', 'INSERT') AS devices_no_insert,
  NOT has_table_privilege(current_user, 'public.devices', 'UPDATE') AS devices_no_update,
  NOT has_table_privilege(current_user, 'public.devices', 'DELETE') AS devices_no_delete,
  NOT has_table_privilege(current_user, 'public.mfg_processes', 'INSERT') AS mfg_processes_no_insert,
  NOT has_table_privilege(current_user, 'public.mfg_processes', 'UPDATE') AS mfg_processes_no_update,
  NOT has_table_privilege(current_user, 'public.mfg_processes', 'DELETE') AS mfg_processes_no_delete,
  NOT has_table_privilege(current_user, 'public.device_logs', 'INSERT') AS device_logs_no_insert,
  NOT has_table_privilege(current_user, 'public.device_logs', 'UPDATE') AS device_logs_no_update,
  NOT has_table_privilege(current_user, 'public.device_logs', 'DELETE') AS device_logs_no_delete,
  NOT has_table_privilege(current_user, 'public.hourly_capacity', 'INSERT') AS hourly_capacity_no_insert,
  NOT has_table_privilege(current_user, 'public.hourly_capacity', 'UPDATE') AS hourly_capacity_no_update,
  NOT has_table_privilege(current_user, 'public.hourly_capacity', 'DELETE') AS hourly_capacity_no_delete,
  NOT has_table_privilege(current_user, 'public.pass_station_records', 'INSERT') AS pass_station_records_no_insert,
  NOT has_table_privilege(current_user, 'public.pass_station_records', 'UPDATE') AS pass_station_records_no_update,
  NOT has_table_privilege(current_user, 'public.pass_station_records', 'DELETE') AS pass_station_records_no_delete,
  NOT has_schema_privilege(current_user, 'public', 'CREATE') AS public_schema_no_create;

SELECT 'devices_rows' AS probe, count(*)::bigint AS row_count FROM public.devices;
SELECT 'mfg_processes_rows' AS probe, count(*)::bigint AS row_count FROM public.mfg_processes;
SELECT 'device_logs_rows' AS probe, count(*)::bigint AS row_count FROM public.device_logs;
SELECT 'hourly_capacity_rows' AS probe, count(*)::bigint AS row_count FROM public.hourly_capacity;
SELECT 'pass_station_records_rows' AS probe, count(*)::bigint AS row_count FROM public.pass_station_records;
