namespace AdminDesk.SharedKernel.Constants;

public static class Categories
{
    public const string TravelAndMoney = "Travel and money";
    public const string AssetsAndEquipment = "Assets and equipment";
    public const string Facilities = "Facilities";
    public const string ProcurementAndVendors = "Procurement and vendors";
    public const string TicketsAndHelpdesk = "Tickets and helpdesk";
    public const string VisitorsAndOthers = "Visitors and others";

    // In the order shown in the UI.
    public static readonly string[] All =
    {
        TravelAndMoney, AssetsAndEquipment, Facilities, ProcurementAndVendors, TicketsAndHelpdesk, VisitorsAndOthers
    };
}
