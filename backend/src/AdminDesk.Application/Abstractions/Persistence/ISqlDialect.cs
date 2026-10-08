namespace AdminDesk.Application.Abstractions.Persistence;

// Every SQL difference between database engines lives behind this interface, so a
// move to another engine means one new implementation.
public interface ISqlDialect
{
    // Takes @ModuleCode and @Year, inserts or increments the counter row and returns last_value.
    string UpsertCounterReturningSql { get; }

    // Expression reading a value out of a JSON text column; the path is a literal such as $.amount.
    string JsonExtract(string column, string path);

    string LimitOffset(string limitParam, string offsetParam);

    // Expression for the current UTC instant in the stored text format.
    string NowUtc { get; }

    // Case-insensitive contains/prefix predicate; pair it with EscapeLikeValue on the input.
    string Like(string column, string param);

    // Equality of a column and a parameter ignoring letter case.
    string EqualsIgnoreCase(string column, string param);

    // Adds the clause that makes an INSERT statement return the new row's integer id.
    string InsertReturningId(string insertSql);

    // Escapes wildcard characters in user input used with Like.
    string EscapeLikeValue(string value);
}
