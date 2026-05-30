#!/usr/bin/env python3
"""Copy AdventureWorks tables from PostgreSQL (pgloader) into Oracle (PascalCase schemas)."""

from __future__ import annotations

import argparse
import csv
import datetime
import decimal
import sys
from dataclasses import dataclass, field
from pathlib import Path

import oracledb
import psycopg

SCHEMAS = ("humanresources", "person", "production", "purchasing", "sales")

SCHEMA_MAP = {
    "humanresources": "HumanResources",
    "person": "Person",
    "production": "Production",
    "purchasing": "Purchasing",
    "sales": "Sales",
}


@dataclass
class SqlServerIdentifierMaps:
    tables: dict[tuple[str, str], str] = field(default_factory=dict)
    columns: dict[tuple[str, str, str], str] = field(default_factory=dict)

    def resolve_table(self, pg_schema: str, pg_table: str) -> str:
        key = (pg_schema.lower(), pg_table.lower())
        return self.tables.get(key, pascal_name(pg_table))

    def resolve_column(self, pg_schema: str, pg_table: str, pg_column: str) -> str:
        key = (pg_schema.lower(), pg_table.lower(), pg_column.lower())
        resolved = self.columns.get(key, pascal_name(pg_column))
        return normalize_ef_oracle_column(resolved)


def pascal_name(identifier: str) -> str:
    parts = identifier.split("_")
    return "".join(p[:1].upper() + p[1:] for p in parts if p)


def normalize_ef_oracle_column(column_name: str) -> str:
    """Align SQL Server identifiers with AdventureWorks EF property/column names."""
    if column_name.endswith("ID") and len(column_name) > 2:
        column_name = column_name[:-2] + "Id"

    if column_name.isascii() and column_name.islower() and column_name:
        return column_name[0].upper() + column_name[1:]

    return column_name


def load_sqlserver_identifier_maps(path: Path) -> SqlServerIdentifierMaps:
    maps = SqlServerIdentifierMaps()

    with path.open(newline="", encoding="utf-8") as handle:
        reader = csv.reader(handle, delimiter="|")
        for row in reader:
            if len(row) < 3:
                continue

            schema, table, column = (value.strip() for value in row[:3])
            if not schema or schema.startswith("-") or schema.lower() == "table_schema":
                continue

            schema_key = schema.lower()
            table_key = table.lower()
            column_key = column.lower()

            maps.tables[(schema_key, table_key)] = table
            maps.columns[(schema_key, table_key, column_key)] = column

    return maps


def oracle_type(pg_type: str, char_len: int | None, num_prec: int | None, num_scale: int | None) -> str:
    pg_type = pg_type.lower()
    if pg_type in ("integer", "int4"):
        return "NUMBER(10)"
    if pg_type in ("bigint", "int8"):
        return "NUMBER(19)"
    if pg_type in ("smallint", "int2"):
        return "NUMBER(5)"
    if pg_type in ("boolean", "bool"):
        return "NUMBER(1)"
    if pg_type in ("uuid",):
        return "RAW(16)"
    if pg_type in ("bytea",):
        return "BLOB"
    if pg_type in ("text",):
        return "CLOB"
    if pg_type in ("character varying", "varchar"):
        size = char_len or 4000
        return f"VARCHAR2({min(size, 4000)})"
    if pg_type in ("character", "char"):
        size = char_len or 1
        return f"CHAR({min(size, 2000)})"
    if pg_type in ("numeric", "decimal"):
        p = num_prec or 18
        s = num_scale or 0
        return f"NUMBER({p},{s})"
    if pg_type in ("double precision", "float8"):
        return "BINARY_DOUBLE"
    if pg_type in ("real", "float4"):
        return "BINARY_FLOAT"
    if pg_type in ("timestamp with time zone", "timestamptz"):
        return "TIMESTAMP WITH TIME ZONE"
    if pg_type in ("timestamp without time zone", "timestamp"):
        return "TIMESTAMP"
    if pg_type in ("date",):
        return "DATE"
    if pg_type in ("money",):
        return "NUMBER(19,4)"
    return "CLOB"


def ensure_schema_user(cur: oracledb.Cursor, schema: str, password: str) -> None:
    try:
        cur.execute(f'CREATE USER "{schema}" IDENTIFIED BY "{password}" QUOTA UNLIMITED ON USERS')
    except oracledb.DatabaseError as ex:
        if "ORA-01920" not in str(ex):  # user already exists
            raise
    cur.execute(f'GRANT CONNECT, RESOURCE TO "{schema}"')


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pg-host", default="localhost")
    parser.add_argument("--pg-port", default="5432")
    parser.add_argument("--pg-db", default="adventureworks")
    parser.add_argument("--pg-user", default="postgres")
    parser.add_argument("--pg-password", required=True)
    parser.add_argument("--oracle-dsn", default="localhost:1521/FREEPDB1")
    parser.add_argument("--oracle-admin-user", default="SYSTEM")
    parser.add_argument("--oracle-admin-password", required=True)
    parser.add_argument("--oracle-app-user", default="AdvWorks")
    parser.add_argument("--schema-password", default="Oracle1")
    parser.add_argument("--max-tables", type=int, default=0, help="Limit tables (0 = all)")
    parser.add_argument(
        "--sqlserver-columns-file",
        help="Pipe-delimited TABLE_SCHEMA|TABLE_NAME|COLUMN_NAME export from SQL Server (pgloader lowercases names)",
    )
    args = parser.parse_args()

    identifier_maps = (
        load_sqlserver_identifier_maps(Path(args.sqlserver_columns_file))
        if args.sqlserver_columns_file
        else SqlServerIdentifierMaps()
    )

    pg_conninfo = (
        f"host={args.pg_host} port={args.pg_port} dbname={args.pg_db} "
        f"user={args.pg_user} password={args.pg_password}"
    )

    with psycopg.connect(pg_conninfo) as pg:
        with oracledb.connect(
            user=args.oracle_admin_user,
            password=args.oracle_admin_password,
            dsn=args.oracle_dsn,
        ) as ora:
            ora.autocommit = False
            cur = ora.cursor()

            for schema in SCHEMAS:
                ensure_schema_user(cur, SCHEMA_MAP[schema], args.schema_password)

            tables = pg.execute(
                """
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_schema = ANY(%s) AND table_type = 'BASE TABLE'
                ORDER BY table_schema, table_name
                """,
                (list(SCHEMAS),),
            ).fetchall()

            if args.max_tables > 0:
                tables = tables[: args.max_tables]

            for schema, table in tables:
                ora_schema = SCHEMA_MAP[schema]
                ora_table = identifier_maps.resolve_table(schema, table)
                qualified = f'"{ora_schema}"."{ora_table}"'

                cols = pg.execute(
                    """
                    SELECT column_name, data_type, character_maximum_length,
                           numeric_precision, numeric_scale
                    FROM information_schema.columns
                    WHERE table_schema = %s AND table_name = %s
                    ORDER BY ordinal_position
                    """,
                    (schema, table),
                ).fetchall()

                col_defs = []
                col_names = []
                for col_name, data_type, char_len, num_prec, num_scale in cols:
                    ora_col = identifier_maps.resolve_column(schema, table, col_name)
                    col_names.append(f'"{ora_col}"')
                    col_defs.append(
                        f'"{ora_col}" {oracle_type(data_type, char_len, num_prec, num_scale)}'
                    )

                try:
                    cur.execute(f"DROP TABLE {qualified} PURGE")
                except oracledb.DatabaseError:
                    pass
                cur.execute(f"CREATE TABLE {qualified} ({', '.join(col_defs)})")

                select_cols = ", ".join(f'"{c}"' for c, *_ in cols)
                insert_cols = ", ".join(col_names)
                raw_rows = pg.execute(
                    f'SELECT {select_cols} FROM "{schema}"."{table}"'
                ).fetchall()

                def convert_cell(value, ora_type: str):
                    if value is None:
                        return None
                    if isinstance(value, memoryview):
                        return bytes(value)
                    if isinstance(value, (bytes, bytearray)):
                        return bytes(value)
                    if isinstance(value, decimal.Decimal):
                        return float(value)
                    if isinstance(value, (datetime.datetime, datetime.date)):
                        return value
                    if isinstance(value, datetime.time):
                        return value.isoformat()
                    if isinstance(value, datetime.timedelta):
                        return str(value)
                    type_name = type(value).__name__
                    if type_name == "UUID":
                        return value.bytes if "RAW" in ora_type else str(value)
                    if type_name in ("dict", "list"):
                        return str(value)
                    if isinstance(value, (int, float, str)):
                        return value
                    return str(value)

                rows = [
                    tuple(
                        convert_cell(v, oracle_type(data_type, char_len, num_prec, num_scale))
                        for (col_name, data_type, char_len, num_prec, num_scale), v in zip(cols, row)
                    )
                    for row in raw_rows
                ]

                if rows:
                    placeholders = ", ".join(f":{i + 1}" for i in range(len(col_names)))
                    cur.executemany(
                        f"INSERT INTO {qualified} ({insert_cols}) VALUES ({placeholders})",
                        rows,
                    )

                cur.execute(f"GRANT SELECT ON {qualified} TO {args.oracle_app_user.upper()}")
                print(f"Copied {schema}.{table} ({len(rows)} rows)", file=sys.stderr)

            ora.commit()

    print("Oracle conversion complete", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
