namespace AdminDesk.SharedKernel.Constants;

// The three allocation masters. The value is what master_history.master_type stores.
public static class MasterTypes
{
    public const string Sim = "Sim";
    public const string Asset = "Asset";
    public const string IdCard = "IdCard";

    public static readonly string[] All = { Sim, Asset, IdCard };

    // Route segments of the masters API.
    public const string SimRoute = "sims";
    public const string AssetRoute = "assets";
    public const string IdCardRoute = "id-cards";

    public static string? FromRoute(string? segment) => segment?.ToLowerInvariant() switch
    {
        SimRoute => Sim,
        AssetRoute => Asset,
        IdCardRoute => IdCard,
        _ => null
    };
}

public static class SimStatuses
{
    public const string Available = "Available";
    public const string Allocated = "Allocated";
    public const string Deactivated = "Deactivated";

    public static readonly string[] All = { Available, Allocated, Deactivated };
}

public static class AssetStatuses
{
    public const string Available = "Available";
    public const string Allocated = "Allocated";
    public const string Damaged = "Damaged";
    public const string Lost = "Lost";

    public static readonly string[] All = { Available, Allocated, Damaged, Lost };
}

public static class IdCardStatuses
{
    public const string Active = "Active";
    public const string Replaced = "Replaced";

    public static readonly string[] All = { Active, Replaced };
}

public static class ItemConditions
{
    public const string Good = "Good";
    public const string Damaged = "Damaged";
    public const string Lost = "Lost";

    public static readonly string[] All = { Good, Damaged, Lost };
}

// Event names written to master_history.
public static class MasterEvents
{
    public const string Added = "Added";
    public const string Allocated = "Allocated";
    public const string Returned = "Returned";
    public const string Transferred = "Transferred";
    public const string Issued = "Issued";
    public const string Replaced = "Replaced";
    public const string Edited = "Edited";
    public const string Retired = "Retired";
}

// Module codes, step keys and form field keys the master hook and the module definitions share.
public static class MasterModules
{
    public const string Sim = "sim";
    public const string SimReturn = "sim-return";
    public const string Laptop = "laptop";
    public const string AssetReturn = "asset-return";
    public const string IdCard = "id-card";

    public const string SimAllocationStep = "sim-allocation";
    public const string TelecomActivationStep = "telecom-activation";
    public const string SimMasterUpdateStep = "sim-master-update";
    public const string AssetAllocationStep = "asset-allocation";
    public const string AssetMasterUpdateStep = "asset-master-update";
    public const string ConditionCheckStep = "condition-check";
    public const string DamageLossStep = "damage-loss-calculation";
    public const string AdminPrintingStep = "admin-printing";
    public const string IdCardMasterUpdateStep = "id-card-master-update";

    public const string SimField = "sim";
    public const string AssetField = "asset";
    public const string RequestTypeField = "requestType";
    public const string ReasonField = "reason";
    public const string TransferToField = "transferTo";
    public const string ActivationDateField = "activationDate";
    public const string ConditionField = "condition";
    public const string DamageCostField = "damageCost";
    public const string NewCardNumberField = "newCardNumber";
    public const string ReplacementCostField = "replacementCost";
    public const string OldCardStatusField = "oldCardStatus";

    public const string ReasonTransfer = "Transfer";
    public const string RequestTypeReplacement = "Replacement";
}

public static class MasterLookupKinds
{
    public const string AvailableSim = "availableSim";
    public const string AvailableAsset = "availableAsset";
    public const string HeldSim = "heldSim";
    public const string HeldAsset = "heldAsset";
}
