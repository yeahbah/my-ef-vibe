-- Copy AdventureWorks tables from PostgreSQL (pgloader output) into a SQLite file.
-- Table names use "Schema.Table" so EF mappings (Production.Product, etc.) resolve.
INSTALL
postgres;
LOAD
postgres;
INSTALL
sqlite;
LOAD
sqlite;

ATTACH
'dbname=adventureworks user=postgres password=__POSTGRES_PASSWORD__ host=host.docker.internal port=__POSTGRES_PORT__' AS src (TYPE postgres, READ_ONLY);
ATTACH
'__SQLITE_PATH__' AS dst (TYPE sqlite);

CREATE TABLE dst."Production.Product" AS
SELECT *
FROM src.production.product;
CREATE TABLE dst."Person.Address" AS
SELECT *
FROM src.person.address;
CREATE TABLE dst."Sales.Customer" AS
SELECT *
FROM src.sales.customer;

DETACH
dst;
