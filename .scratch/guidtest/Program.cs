using FirebirdSql.Data.FirebirdClient;

var cs = "User=aw_user;Password=aw_password;Database=/var/lib/firebird/data/adventureworks.fdb;DataSource=localhost;Port=3050;Charset=UTF8;";
await using var conn = new FbConnection(cs);
await conn.OpenAsync();
await using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT \"rowguid\" FROM \"Production_Product\" ROWS 1 TO 1";
await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"FieldType={reader.GetFieldType(0).Name}");
    Console.WriteLine($"ValueType={reader.GetValue(0).GetType().Name}");
    Console.WriteLine($"String={reader.GetString(0)}");
    try { Console.WriteLine($"Guid={reader.GetGuid(0)}"); } catch (Exception ex) { Console.WriteLine($"GetGuid failed: {ex.GetType().Name}: {ex.Message}"); }
}
