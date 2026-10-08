using System.Data.Common;

namespace AdminDesk.Application.Masters;

// The numbers that must stay unique across all rows, retired ones included.
public enum MasterNumberKind
{
    SimNumber,
    MobileNumber,
    AssetTag,
    SerialNumber,
    CardNumber
}

public sealed record HistoryEntry(
    string MasterType,
    long MasterId,
    string EventType,
    long? EmployeeId,
    long? RequestId,
    string? Condition,
    long? CostMinor,
    string? Notes);

// Access to the SIM, asset and ID card masters and their history. Reads open their own connection;
// every write takes the transaction of the caller. Nothing here removes a row: retiring is is_active = 0.
public interface IMasterAssetRepository
{
    // ------------------------------------------------------------------ lists

    Task<PagedResult<SimDto>> ListSimsAsync(MasterListQuery query, CancellationToken ct);

    Task<PagedResult<AssetDto>> ListAssetsAsync(MasterListQuery query, CancellationToken ct);

    Task<PagedResult<IdCardDto>> ListIdCardsAsync(MasterListQuery query, CancellationToken ct);

    Task<IReadOnlyList<MasterHistoryDto>> GetHistoryAsync(string masterType, long id, CancellationToken ct);

    // True for retired records too, so their history stays readable.
    Task<bool> ExistsAsync(string masterType, long id, CancellationToken ct);

    // True when the record exists but has been retired.
    Task<bool> IsRetiredAsync(DbTransaction tx, string masterType, long id, CancellationToken ct);

    // What the employee holds now, in one round trip.
    Task<HoldingsDto> ListHeldByAsync(long employeeId, CancellationToken ct);

    // ------------------------------------------------------- single rows (in a transaction)

    Task<SimDto?> GetSimAsync(DbTransaction tx, long id, CancellationToken ct);

    Task<AssetDto?> GetAssetAsync(DbTransaction tx, long id, CancellationToken ct);

    Task<IdCardDto?> GetIdCardAsync(DbTransaction tx, long id, CancellationToken ct);

    Task<bool> IsNumberTakenAsync(DbTransaction tx, MasterNumberKind kind, string value, long? exceptId, CancellationToken ct);

    Task<bool> HasActiveIdCardAsync(DbTransaction tx, long employeeId, CancellationToken ct);

    // ------------------------------------------------------------- owner maintenance

    Task<long> InsertSimAsync(DbTransaction tx, SimFields fields, CancellationToken ct);

    Task<long> InsertAssetAsync(DbTransaction tx, AssetFields fields, CancellationToken ct);

    Task<long> InsertIdCardAsync(DbTransaction tx, string cardNumber, long employeeId, DateOnly issuedDate, CancellationToken ct);

    Task UpdateSimAsync(DbTransaction tx, long id, SimFields fields, CancellationToken ct);

    Task UpdateAssetAsync(DbTransaction tx, long id, AssetFields fields, CancellationToken ct);

    Task UpdateIdCardAsync(DbTransaction tx, long id, IdCardFields fields, CancellationToken ct);

    // Soft retire: is_active = 0 and deleted_utc. Refused (false) while the item has a holder.
    Task<bool> RetireAsync(DbTransaction tx, string masterType, long id, CancellationToken ct);

    // -------------------------------------------------------------- request effects
    // Each guarded write returns false when the row was not in the expected state.

    Task<bool> AllocateSimAsync(DbTransaction tx, long simId, long employeeId, DateOnly? activationDate, CancellationToken ct);

    Task<bool> ReleaseSimAsync(DbTransaction tx, long simId, long fromEmployeeId, CancellationToken ct);

    Task<bool> TransferSimAsync(DbTransaction tx, long simId, long fromEmployeeId, long toEmployeeId, CancellationToken ct);

    Task<bool> AllocateAssetAsync(DbTransaction tx, long assetId, long employeeId, CancellationToken ct);

    Task<bool> ReleaseAssetAsync(
        DbTransaction tx, long assetId, long fromEmployeeId, string newStatus, string condition, CancellationToken ct);

    // Marks every active card of the employee as replaced and returns their ids.
    Task<IReadOnlyList<long>> ReplaceActiveIdCardsAsync(DbTransaction tx, long employeeId, CancellationToken ct);

    Task AppendHistoryAsync(DbTransaction tx, HistoryEntry entry, CancellationToken ct);
}
