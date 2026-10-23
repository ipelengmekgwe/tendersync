namespace TenderSync.Domain.Enums;

/// <summary>Specific requirement type used by the qualification evaluators (PLAN.md §7.4).</summary>
public enum RequirementType
{
    Other = 0,
    CsdRegistration = 1,
    TaxCompliance = 2,
    BbbeeCertificate = 3,
    CidbGrade = 4,
    CompanyRegistration = 5,
    Insurance = 6,
    YearsInBusiness = 7,
    MinimumTurnover = 8,
    References = 9,
    Certification = 10,
    Accreditation = 11,
    LocalContent = 12,
    JvSubcontractingRule = 13,
}
