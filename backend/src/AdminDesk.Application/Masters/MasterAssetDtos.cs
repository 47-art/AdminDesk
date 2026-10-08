using AdminDesk.SharedKernel.Money;
using FluentValidation;

namespace AdminDesk.Application.Masters;

public sealed record SimDto(
    long Id,
    string SimNumber,
    string MobileNumber,
    string TelecomOperator,
    string Plan,
    DateOnly? ActivationDate,
    string Status,
    decimal MonthlyCost,
    long? HolderEmployeeId,
    string? HolderName,
    string? HolderCode,
    bool Retired,
    DateTime? RetiredUtc);

public sealed record AssetDto(
    long Id,
    string AssetTag,
    string AssetType,
    string MakeModel,
    string SerialNumber,
    string Status,
    string? Condition,
    long? HolderEmployeeId,
    string? HolderName,
    string? HolderCode,
    bool Retired,
    DateTime? RetiredUtc);

public sealed record IdCardDto(
    long Id,
    string CardNumber,
    long EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    string Status,
    DateOnly IssuedDate,
    bool Retired,
    DateTime? RetiredUtc);

public sealed record MasterHistoryDto(
    long Id,
    string EventType,
    long? EmployeeId,
    string? EmployeeName,
    string? EmployeeCode,
    long? RequestId,
    string? RequestNo,
    string? Condition,
    decimal? Cost,
    string? Notes,
    DateTime EventUtc);

public sealed record HoldingDto(string Type, long Id, string Label, string Since);

public sealed record HoldingsDto(IReadOnlyList<HoldingDto> Sims, IReadOnlyList<HoldingDto> Assets, HoldingDto? IdCard);

// Page, filters and search of a master list.
public sealed record MasterListQuery(int Page, int PageSize, string? Search, string? Status, long? HolderEmployeeId, bool IncludeRetired = false);

// Bodies of add and edit. Holder, status and condition are not part of them.
public sealed class SimFieldsBody
{
    public string? SimNumber { get; set; }
    public string? MobileNumber { get; set; }
    public string? TelecomOperator { get; set; }
    public string? Plan { get; set; }
    public decimal? MonthlyCost { get; set; }
}

public sealed class AssetFieldsBody
{
    public string? AssetTag { get; set; }
    public string? AssetType { get; set; }
    public string? MakeModel { get; set; }
    public string? SerialNumber { get; set; }
}

public sealed class IdCardAddBody
{
    public string? CardNumber { get; set; }
    public long? EmployeeId { get; set; }
    public DateOnly? IssuedDate { get; set; }
}

public sealed class IdCardEditBody
{
    public string? CardNumber { get; set; }
    public DateOnly? IssuedDate { get; set; }
}

public sealed record SimFields(string SimNumber, string MobileNumber, string TelecomOperator, string Plan, long MonthlyCostMinor);

public sealed record AssetFields(string AssetTag, string AssetType, string MakeModel, string SerialNumber);

public sealed record IdCardFields(string CardNumber, DateOnly IssuedDate);

public sealed class SimFieldsBodyValidator : AbstractValidator<SimFieldsBody>
{
    public SimFieldsBodyValidator()
    {
        RuleFor(x => x.SimNumber).NotEmpty().WithMessage("Enter the SIM number.").MaximumLength(30);
        RuleFor(x => x.MobileNumber).NotEmpty().WithMessage("Enter the mobile number.").MaximumLength(20);
        RuleFor(x => x.TelecomOperator).NotEmpty().WithMessage("Enter the telecom operator.").MaximumLength(60);
        RuleFor(x => x.Plan).NotEmpty().WithMessage("Enter the plan.").MaximumLength(80);
        RuleFor(x => x.MonthlyCost).NotNull().WithMessage("Enter the monthly cost.")
            .GreaterThanOrEqualTo(0).WithMessage("The monthly cost cannot be negative.")
            .LessThanOrEqualTo(MoneyConverter.MaxRupees).WithMessage(MoneyConverter.TooLargeMessage)
            .Must(v => v is null || decimal.Round(v.Value, 2) == v.Value).WithMessage("Enter an amount with at most two decimal places.");
    }
}

public sealed class AssetFieldsBodyValidator : AbstractValidator<AssetFieldsBody>
{
    public AssetFieldsBodyValidator()
    {
        RuleFor(x => x.AssetTag).NotEmpty().WithMessage("Enter the asset tag.").MaximumLength(40);
        RuleFor(x => x.AssetType).NotEmpty().WithMessage("Enter the asset type.").MaximumLength(40);
        RuleFor(x => x.MakeModel).NotEmpty().WithMessage("Enter the make and model.").MaximumLength(120);
        RuleFor(x => x.SerialNumber).NotEmpty().WithMessage("Enter the serial number.").MaximumLength(60);
    }
}

public sealed class IdCardAddBodyValidator : AbstractValidator<IdCardAddBody>
{
    public IdCardAddBodyValidator()
    {
        RuleFor(x => x.CardNumber).NotEmpty().WithMessage("Enter the card number.").MaximumLength(40);
        RuleFor(x => x.EmployeeId).NotNull().WithMessage("Choose the employee.");
        RuleFor(x => x.IssuedDate).NotNull().WithMessage("Enter the issued date.");
    }
}

public sealed class IdCardEditBodyValidator : AbstractValidator<IdCardEditBody>
{
    public IdCardEditBodyValidator()
    {
        RuleFor(x => x.CardNumber).NotEmpty().WithMessage("Enter the card number.").MaximumLength(40);
        RuleFor(x => x.IssuedDate).NotNull().WithMessage("Enter the issued date.");
    }
}
