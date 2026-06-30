\set ON_ERROR_STOP on

SELECT format(
    'CREATE ROLE %I WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L',
    :'readonly_user',
    :'readonly_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'readonly_user')
\gexec

SELECT format(
    'ALTER ROLE %I WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L',
    :'readonly_user',
    :'readonly_password')
\gexec

SELECT format('ALTER ROLE %I SET default_transaction_read_only = on', :'readonly_user')
\gexec

SELECT format('ALTER ROLE %I SET statement_timeout = %L', :'readonly_user', '30s')
\gexec

SELECT format('ALTER ROLE %I SET lock_timeout = %L', :'readonly_user', '3s')
\gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'readonly_user')
\gexec

SELECT format('GRANT USAGE ON SCHEMA public TO %I', :'readonly_user')
\gexec

SELECT format('REVOKE CREATE ON SCHEMA public FROM %I', :'readonly_user')
\gexec

SELECT format('REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM %I', :'readonly_user')
\gexec

SELECT format('REVOKE %I FROM %I', parent_role.rolname, :'readonly_user')
FROM pg_auth_members member
INNER JOIN pg_roles parent_role ON parent_role.oid = member.roleid
INNER JOIN pg_roles child_role ON child_role.oid = member.member
WHERE child_role.rolname = :'readonly_user'
\gexec

SELECT format(
    'GRANT SELECT ON TABLE public.devices, public.mfg_processes, public.device_logs, public.hourly_capacity, public.pass_station_records TO %I',
    :'readonly_user')
\gexec

SELECT 'cloud_readonly_grants_applied' AS status,
       :'readonly_user' AS role_name,
       current_database() AS database_name;
