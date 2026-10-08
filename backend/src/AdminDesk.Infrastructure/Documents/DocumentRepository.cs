using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Documents;
using AdminDesk.Infrastructure.Persistence;
using Dapper;

namespace AdminDesk.Infrastructure.Documents;

// Dapper access to document metadata. Removing a document only marks the row.
public sealed class DocumentRepository : IDocumentRepository
{
    private const string Columns =
        "d.id, d.request_id, d.step_key, d.original_name, d.stored_name, d.size_bytes, d.content_type, " +
        "d.uploaded_by_user_id, d.uploaded_by_name, d.uploaded_utc";

    private readonly AuditStamper _stamper;
    private readonly ISqlDialect _dialect;

    public DocumentRepository(AuditStamper stamper, ISqlDialect dialect)
    {
        _stamper = stamper;
        _dialect = dialect;
    }

    public async Task<long> InsertAsync(DbTransaction tx, NewDocument document, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForCreate());
        p.Add("RequestId", document.RequestId);
        p.Add("StepKey", document.StepKey);
        p.Add("OriginalName", document.OriginalName);
        p.Add("StoredName", document.StoredName);
        p.Add("SizeBytes", document.SizeBytes);
        p.Add("ContentType", document.ContentType);
        p.Add("UploadedByUserId", document.UploadedByUserId);
        p.Add("UploadedByName", document.UploadedByName);
        p.Add("UploadedUtc", document.UploadedUtc);

        var sql = _dialect.InsertReturningId(
            "INSERT INTO request_documents (request_id, step_key, original_name, stored_name, size_bytes, content_type, " +
            "uploaded_by_user_id, uploaded_by_name, uploaded_utc, " + AuditSql.InsertColumns + ") VALUES (" +
            "@RequestId, @StepKey, @OriginalName, @StoredName, @SizeBytes, @ContentType, " +
            "@UploadedByUserId, @UploadedByName, @UploadedUtc, " + AuditSql.InsertValues + ")");
        return await tx.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, tx, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DocumentRow>> ListAsync(DbConnection connection, long requestId, CancellationToken ct)
    {
        var sql =
            "SELECT " + Columns + " FROM request_documents d WHERE d.request_id = @RequestId AND " + AuditSql.Active("d") +
            " ORDER BY d.id";
        return (await connection.QueryAsync<DocumentRow>(
            new CommandDefinition(sql, new { RequestId = requestId }, cancellationToken: ct))).ToList();
    }

    public async Task<DocumentRow?> GetAsync(DbTransaction tx, long requestId, long documentId, CancellationToken ct)
    {
        var sql =
            "SELECT " + Columns + " FROM request_documents d WHERE d.id = @Id AND d.request_id = @RequestId AND " +
            AuditSql.Active("d");
        return await tx.Connection!.QuerySingleOrDefaultAsync<DocumentRow>(
            new CommandDefinition(sql, new { Id = documentId, RequestId = requestId }, tx, cancellationToken: ct));
    }

    public async Task SoftDeleteAsync(DbTransaction tx, long documentId, string removedByName, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", documentId);
        p.Add("RemovedByName", removedByName);
        var sql =
            "UPDATE request_documents SET " + AuditSql.SoftDeleteSet + ", deleted_by_name = @RemovedByName " +
            "WHERE id = @Id AND is_active = 1";
        await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, p, tx, cancellationToken: ct));
    }

    public async Task<long> CountForStepAsync(DbTransaction tx, long requestId, string stepKey, CancellationToken ct)
    {
        var sql =
            "SELECT COUNT(*) FROM request_documents d WHERE d.request_id = @RequestId AND d.step_key = @StepKey AND " +
            AuditSql.Active("d");
        return await tx.Connection!.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, new { RequestId = requestId, StepKey = stepKey }, tx, cancellationToken: ct));
    }
}
