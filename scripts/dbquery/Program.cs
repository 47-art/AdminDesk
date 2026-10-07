using Microsoft.Data.Sqlite;

// Tiny SQL runner: DbQuery <dbPath> <sql>
// Runs one statement. Query rows are printed tab-separated with a header line,
// other statements print "OK <rows affected>", errors print "ERROR <code>: <message>"
// and exit with code 1.

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: DbQuery <dbPath> <sql>");
    return 2;
}

try
{
    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = args[0],
        Mode = SqliteOpenMode.ReadWrite,
        ForeignKeys = true,
        DefaultTimeout = 30,
        Pooling = false
    }.ToString();

    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = args[1];
    using var reader = command.ExecuteReader();

    if (reader.FieldCount > 0)
    {
        var header = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            header[i] = reader.GetName(i);
        }
        Console.WriteLine(string.Join('\t', header));

        var values = new string[reader.FieldCount];
        while (reader.Read())
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.IsDBNull(i)
                    ? "NULL"
                    : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            }
            Console.WriteLine(string.Join('\t', values));
        }
    }
    else
    {
        reader.Close();
        Console.WriteLine($"OK {reader.RecordsAffected}");
    }
    return 0;
}
catch (SqliteException ex)
{
    Console.WriteLine($"ERROR {ex.SqliteErrorCode}: {ex.Message}");
    return 1;
}
